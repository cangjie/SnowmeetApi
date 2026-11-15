"""备份方案里不碰外部系统的部分：命名、S3 过期对齐、状态文件、健康检查。

时间一律是服务器本地时间（北京时间，UTC+8），不带时区。
"""
from __future__ import annotations

import datetime as dt
import json
import os
import re
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

# ---- 方案参数（改方案只改这里） ----
S3_PREFIX = "private/backup/db"
PRIMARY_DB = "snowmeet_new"
FULL_DAYS = 7    # S3 生命周期规则：<db>/full/ 目录
DIFF_DAYS = 6    # S3 生命周期规则：<db>/diff/ 目录（比 full 少 1 天，原因见 expiry_utc）
FULL_START = dt.time(2, 30)
FULL_DEADLINE = dt.time(4, 15)   # 04:30 服务器每天重启，full 必须在这之前做完
ALIGN_CUTOFF = dt.time(8, 0)     # 北京时间 08:00 = UTC 零点；full 必须在这之前上传完，才能和 diff 一起过期
DIFF_TIMES = [dt.time(h, m) for h in range(9, 17) for m in (0, 30)] + [dt.time(17, 0), dt.time(0, 10)]
MIN_FREE_GB = 5.0
LOCAL_UTC_OFFSET = dt.timedelta(hours=8)

_DB_RE = re.compile(r"^[A-Za-z0-9_]+$")
_LOCAL_RE = re.compile(
    r"^(?P<db>[A-Za-z0-9_]+?)_(?P<kind>full|diff)_(?P<stamp>\d{8}_\d{4})(?:_base_(?P<base>\d{8}_\d{4}))?\.bak$"
)


def check_db_name(name: str) -> str:
    if not name or not _DB_RE.match(name):
        raise ValueError("不合法的库名: %r" % (name,))
    return name


def stamp(t: dt.datetime) -> str:
    return t.strftime("%Y%m%d_%H%M")


def full_key(db: str, t: dt.datetime, adhoc: bool = False) -> str:
    kind = "adhoc" if adhoc else "full"
    check_db_name(db)
    return "%s/%s/%s/%s_full_%s.bak.gz" % (S3_PREFIX, db, kind, db, stamp(t))


def diff_key(db: str, t: dt.datetime, base_stamp: str) -> str:
    check_db_name(db)
    return "%s/%s/diff/%s_diff_%s_base_%s.bak.gz" % (S3_PREFIX, db, db, stamp(t), base_stamp)


def local_bak_name(db: str, kind: str, t: dt.datetime, base_stamp: Optional[str] = None) -> str:
    check_db_name(db)
    if kind == "full":
        return "%s_full_%s.bak" % (db, stamp(t))
    if kind == "diff":
        if not base_stamp:
            raise ValueError("差异备份文件名必须带基准完整备份的时间")
        return "%s_diff_%s_base_%s.bak" % (db, stamp(t), base_stamp)
    raise ValueError(kind)


def parse_local_name(name: str) -> Optional[Dict[str, Optional[str]]]:
    m = _LOCAL_RE.match(name)
    return m.groupdict() if m else None


def expiry_utc(created_local: dt.datetime, days: int) -> dt.datetime:
    """S3 生命周期规则 Days=N 的实际过期时刻（UTC）。

    S3 把「创建时间 + N 天」向后取整到下一个 UTC 零点。北京时间 02:30 的完整备份属于前一个 UTC 日，
    09:00 之后的差异备份属于当天 UTC 日，所以差异备份目录要比完整备份目录少 1 天，两者才同时过期。
    """
    u = created_local - LOCAL_UTC_OFFSET
    x = u + dt.timedelta(days=days)
    return dt.datetime(x.year, x.month, x.day) + dt.timedelta(days=1)


def full_in_alignment_window(created_local: dt.datetime) -> bool:
    return created_local.time() < ALIGN_CUTOFF


def parse_sql_config(text: str) -> Tuple[str, str]:
    """从 config.sqlServer 的连接串里取账号和密码。出错信息里不带文件内容。"""

    def pick(*keys: str) -> Optional[str]:
        for k in keys:
            m = re.search(r"(?i)(?:^|[;\r\n])\s*" + k + r"\s*=\s*([^;\r\n]*)", text)
            if m:
                return m.group(1).strip()
        return None

    user = pick("uid", "user id")
    pw = pick("pwd", "password")
    if not user or pw is None:
        raise ValueError("config.sqlServer 里找不到账号或密码")
    return user, pw


# ---- 状态文件 ----

def _iso(t: dt.datetime) -> str:
    return t.isoformat(timespec="seconds")


def _parse(s: Optional[str]) -> Optional[dt.datetime]:
    return dt.datetime.fromisoformat(s) if s else None


def load_state(path: Path) -> Dict[str, Any]:
    try:
        return json.loads(Path(path).read_text(encoding="utf-8"))
    except FileNotFoundError:
        return {}


def save_state(path: Path, state: Dict[str, Any]) -> None:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_text(json.dumps(state, ensure_ascii=False, indent=2, sort_keys=True), encoding="utf-8")
    os.replace(str(tmp), str(path))


def record_ok(state: Dict[str, Any], job: str, now: dt.datetime, **info: Any) -> None:
    state.setdefault("jobs", {})[job] = dict(info, ok_at=_iso(now))
    state.setdefault("failures", {}).pop(job, None)


def record_fail(state: Dict[str, Any], job: str, now: dt.datetime, msg: str) -> None:
    state.setdefault("failures", {})[job] = {"at": _iso(now), "msg": msg[:300]}


# ---- 健康检查（由 cron 每 30 分钟调用） ----

def latest_scheduled(now: dt.datetime, times: List[dt.time]) -> Optional[dt.datetime]:
    best = None
    for back in (0, 1):
        d = (now - dt.timedelta(days=back)).date()
        for t in times:
            c = dt.datetime.combine(d, t)
            if c <= now and (best is None or c > best):
                best = c
    return best


def evaluate_health(state: Dict[str, Any], now: dt.datetime, free_gb: float,
                    db: str = PRIMARY_DB) -> List[Tuple[str, str]]:
    problems: List[Tuple[str, str]] = []
    jobs = state.get("jobs", {})
    fails = state.get("failures", {})
    enabled_from = _parse(state.get("enabled_from"))

    def stale(job: str, times: List[dt.time], grace_min: int, label: str) -> None:
        # 只看「已经过了宽限期」的最近一次计划时间：健康检查和备份同在整点/半点跑时，
        # 当前这一次还在进行中，不能算漏做；上一次没成功才算。
        sched = latest_scheduled(now - dt.timedelta(minutes=grace_min), times)
        if sched is None or (enabled_from is not None and sched < enabled_from):
            return
        ok = _parse((jobs.get(job) or {}).get("ok_at"))
        if ok is None or ok < sched:
            problems.append(("%s:missed" % job, "%s：应在 %s 完成，至今没有成功记录" % (label, sched.strftime("%m-%d %H:%M"))))

    stale("full:%s" % db, [FULL_START], 100, "完整备份")
    stale("diff:%s" % db, DIFF_TIMES, 25, "差异备份")

    for job, f in fails.items():
        at = _parse(f.get("at"))
        ok = _parse((jobs.get(job) or {}).get("ok_at"))
        if at and (ok is None or ok < at) and (enabled_from is None or at >= enabled_from):
            problems.append(("%s:failed" % job, "%s 失败：%s" % (job, f.get("msg", ""))))

    if free_gb < MIN_FREE_GB:
        problems.append(("disk:low", "磁盘剩余 %.1f GB，低于 %.0f GB" % (free_gb, MIN_FREE_GB)))
    return problems


def should_alert(state: Dict[str, Any], key: str, now: dt.datetime, cooldown_hours: float = 6) -> bool:
    last = _parse(state.get("alerts", {}).get(key))
    return last is None or now - last >= dt.timedelta(hours=cooldown_hours)


def mark_alerted(state: Dict[str, Any], key: str, now: dt.datetime) -> None:
    state.setdefault("alerts", {})[key] = _iso(now)
