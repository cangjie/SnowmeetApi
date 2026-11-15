"""备份任务：full / diff / check / drill。所有外部依赖（SQL、S3、时钟、磁盘）都从 Env 注入，便于测试。"""
from __future__ import annotations

import contextlib
import dataclasses
import datetime as dt
import fcntl
import hashlib
import logging
import shutil
from pathlib import Path
from typing import Callable, Dict, List, Optional, Tuple

from . import core

log = logging.getLogger("snowmeet_backup")


class Busy(Exception):
    """另一个备份任务正在运行。"""


class BackupError(Exception):
    pass


MSDB_LAST_FULL = (
    "SELECT TOP 1 backup_set_id FROM msdb.dbo.backupset "
    "WHERE database_name = %s AND type = 'D' AND is_copy_only = 0 "
    "ORDER BY backup_finish_date DESC, backup_set_id DESC"
)
DB_SIZE_SQL = (
    "SELECT CAST(SUM(size) * 8 / 1024.0 AS int) FROM sys.master_files "
    "WHERE database_id = DB_ID(%s) AND type = 0"
)
KEEP_LOCAL_DIFFS = 2


@dataclasses.dataclass
class Env:
    sql: object                              # execute(sql, params=None) / query(sql, params=None) -> list[tuple]
    s3: object                               # upload(local: Path, key: str) / head_size(key) -> int
    notify: Callable[[str, str], None]       # notify(level, message)
    now: Callable[[], dt.datetime]
    staging: Path                            # SQL Server（mssql 用户）能写的目录
    state_path: Path
    lock_path: Path
    gzip_fn: Callable[[Path, Path], None]
    free_gb: Callable[[Path], float]


@contextlib.contextmanager
def file_lock(path: Path):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    f = open(str(path), "w")
    try:
        try:
            fcntl.flock(f, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise Busy()
        yield
    finally:
        try:
            fcntl.flock(f, fcntl.LOCK_UN)
        finally:
            f.close()


def _q(path) -> str:
    s = str(path)
    if "'" in s:
        raise BackupError("路径里不能有单引号: %s" % s)
    return s


def _prepare_dir(path: Path) -> None:
    """建目录，并尽量让 mssql 用户能写（脚本以 root 运行时才会成功）。"""
    path = Path(path)
    path.mkdir(parents=True, exist_ok=True)
    try:
        shutil.chown(str(path), "mssql", "mssql")
        path.chmod(0o750)
    except Exception:
        pass


def _bak_sql(db: str, path, differential: bool = False, copy_only: bool = False) -> str:
    opts: List[str] = []
    if copy_only:
        opts.append("COPY_ONLY")
    if differential:
        opts.append("DIFFERENTIAL")
    opts += ["INIT", "CHECKSUM", "STATS = 100"]
    return "BACKUP DATABASE [%s] TO DISK = N'%s' WITH %s" % (core.check_db_name(db), _q(path), ", ".join(opts))


def _verify_sql(path) -> str:
    return "RESTORE VERIFYONLY FROM DISK = N'%s' WITH CHECKSUM" % _q(path)


def _sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(str(path), "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def _iso(t: dt.datetime) -> str:
    return t.isoformat(timespec="seconds")


def _ensure_space(env: Env, db: str) -> None:
    rows = env.sql.query(DB_SIZE_SQL, (db,))
    size_mb = rows[0][0] if rows and rows[0][0] else 0
    need_gb = (size_mb * 1.5 + 1024) / 1024.0
    free = env.free_gb(env.staging)
    if free < need_gb:
        raise BackupError("磁盘不够：剩 %.1f GB，需要至少 %.1f GB" % (free, need_gb))


def _ship(env: Env, bak: Path, key: str) -> Tuple[int, str]:
    """gzip → 算 sha256 → 上传 → 核对 S3 上的大小 → 上传 sha256 旁文件。无论成败都清掉临时的 .gz 和 .sha256。"""
    gz = bak.with_name(bak.name + ".gz")
    sha_file = bak.with_name(bak.name + ".gz.sha256")
    try:
        env.gzip_fn(bak, gz)
        size = gz.stat().st_size
        sha = _sha256(gz)
        sha_file.write_text("%s  %s\n" % (sha, key.rsplit("/", 1)[-1]), encoding="utf-8")
        env.s3.upload(gz, key)
        remote = env.s3.head_size(key)
        if remote != size:
            raise BackupError("S3 上的大小 %s 和本机 %s 不一致：%s" % (remote, size, key))
        env.s3.upload(sha_file, key + ".sha256")
        if env.s3.head_size(key + ".sha256") != sha_file.stat().st_size:
            raise BackupError("S3 上的校验文件大小不一致：%s" % key)
        return size, sha
    finally:
        for p in (gz, sha_file):
            try:
                p.unlink()
            except FileNotFoundError:
                pass


def _prune_local(staging: Path, db: str, keep: List[str]) -> None:
    for p in staging.glob("%s_*.bak" % db):
        info = core.parse_local_name(p.name)
        if info and info["db"] == db and p.name not in keep:
            p.unlink()


def _fail(env: Env, job: str, msg: str) -> None:
    state = core.load_state(env.state_path)
    core.record_fail(state, job, env.now(), msg)
    core.save_state(env.state_path, state)
    env.notify("error", "%s 失败：%s" % (job, msg))


def run_full(env: Env, db: str, copy_only: bool = False, keep_local: bool = True, deployment: bool = False) -> Dict[str, object]:
    core.check_db_name(db)
    if deployment and not copy_only:
        raise ValueError("部署前备份必须使用 COPY_ONLY")
    job = ("deployment:%s" if deployment else ("adhoc:%s" if copy_only else "full:%s")) % db
    with file_lock(env.lock_path):
        t0 = env.now()
        staging = env.staging / "deployment" if deployment else env.staging
        bak = staging / core.local_bak_name(db, "full", t0)
        try:
            _prepare_dir(staging)
            _ensure_space(env, db)
            if bak.exists():
                bak.unlink()
            env.sql.execute(_bak_sql(db, bak, copy_only=copy_only))
            env.sql.execute(_verify_sql(bak))
            key = core.deployment_key(db, t0) if deployment else core.full_key(db, t0, adhoc=copy_only)
            size, sha = _ship(env, bak, key)
            if deployment and env.s3.sha256(key) != sha:
                raise BackupError("从 S3 读回的备份 SHA-256 不一致：%s" % key)
            info = {"stamp": core.stamp(t0), "key": key, "bytes": size, "sha256": sha}
            if not copy_only:
                rows = env.sql.query(MSDB_LAST_FULL, (db,))
                if not rows:
                    raise BackupError("msdb 里查不到刚做的完整备份记录")
                info["msdb_set_id"] = rows[0][0]
            state = core.load_state(env.state_path)
            core.record_ok(state, job, env.now(), **info)
            core.save_state(env.state_path, state)
            if copy_only or not keep_local:
                if bak.exists():
                    bak.unlink()
            else:
                _prune_local(env.staging, db, [bak.name])      # 旧的完整备份和旧基准的差异备份都不要了
            done = env.now()
            if not copy_only:
                if core.FULL_DEADLINE < done.time() < core.ALIGN_CUTOFF:
                    env.notify("warning", "%s 做完时间 %s 超过了 %s（04:30 服务器会重启）" % (
                        job, done.strftime("%H:%M"), core.FULL_DEADLINE.strftime("%H:%M")))
                if done.time() >= core.ALIGN_CUTOFF:
                    env.notify("warning", "%s 在 08:00 之后才上传完，S3 过期时间会和它的差异备份错开一天" % job)
            log.info("%s 完成：%s（%d 字节）", job, key, size)
            return dict(info, job=job)
        except Exception as e:
            _fail(env, job, str(e))
            try:
                if bak.exists() and (copy_only or not keep_local):
                    bak.unlink()
            except OSError:
                pass
            raise


def run_diff(env: Env, db: str) -> Dict[str, object]:
    core.check_db_name(db)
    job = "diff:%s" % db
    with file_lock(env.lock_path):
        t0 = env.now()
        bak: Optional[Path] = None
        try:
            state = core.load_state(env.state_path)
            base = (state.get("jobs") or {}).get("full:%s" % db)
            if not base:
                raise BackupError("没有基准完整备份：先运行一次 full")
            rows = env.sql.query(MSDB_LAST_FULL, (db,))
            if not rows or rows[0][0] != base.get("msdb_set_id"):
                raise BackupError("msdb 里最新的完整备份不是本脚本做的（可能有人手动做了没带 COPY_ONLY 的完整备份），"
                                  "差异备份已停止；请先重新跑一次 full")
            _prepare_dir(env.staging)
            _ensure_space(env, db)
            bak = env.staging / core.local_bak_name(db, "diff", t0, base["stamp"])
            if bak.exists():
                bak.unlink()
            env.sql.execute(_bak_sql(db, bak, differential=True))
            env.sql.execute(_verify_sql(bak))
            key = core.diff_key(db, t0, base["stamp"])
            size, sha = _ship(env, bak, key)
            state = core.load_state(env.state_path)
            core.record_ok(state, job, env.now(), stamp=core.stamp(t0), key=key, bytes=size, sha256=sha,
                           base=base["stamp"])
            core.save_state(env.state_path, state)
            # 本机只留当前基准的完整备份和最近 2 份差异备份
            diffs = sorted(
                (p.name for p in env.staging.glob("%s_diff_*_base_%s.bak" % (db, base["stamp"]))), reverse=True)
            keep = ["%s_full_%s.bak" % (db, base["stamp"])] + diffs[:KEEP_LOCAL_DIFFS]
            _prune_local(env.staging, db, keep)
            log.info("%s 完成：%s（%d 字节）", job, key, size)
            return {"job": job, "key": key, "bytes": size, "sha256": sha, "base": base["stamp"]}
        except Exception as e:
            _fail(env, job, str(e))
            try:
                if bak is not None and bak.exists():
                    bak.unlink()
            except OSError:
                pass
            raise


def run_check(env: Env, db: str) -> List[Tuple[str, str]]:
    """健康检查：有问题就通知（同一问题 6 小时内只提醒一次），问题消失后清掉提醒记录。"""
    now = env.now()
    state = core.load_state(env.state_path)
    if not state.get("enabled_from"):
        state["enabled_from"] = _iso(now)
    problems = core.evaluate_health(state, now, env.free_gb(env.staging), db)
    keys = {k for k, _ in problems}
    for k in list(state.get("alerts", {})):
        if k not in keys:
            del state["alerts"][k]
    for key, msg in problems:
        if core.should_alert(state, key, now):
            env.notify("error", msg)
            core.mark_alerted(state, key, now)
    core.save_state(env.state_path, state)
    return problems


def build_restore_statements(rt: str, full_bak, diff_bak, filelist, rt_dir) -> List[str]:
    """还原演练用的语句：把完整备份（可带差异备份）还原成临时库，再 DBCC CHECKDB。"""
    core.check_db_name(rt)
    moves = []
    for i, row in enumerate(filelist):
        logical, ftype = str(row[0]), str(row[2])
        ext = "ldf" if ftype == "L" else ("mdf" if i == 0 else "ndf")
        moves.append("MOVE N'%s' TO N'%s/%s_%d.%s'" % (logical.replace("'", "''"), _q(rt_dir), rt, i, ext))
    withs = ", ".join(moves + ["NORECOVERY" if diff_bak else "RECOVERY", "STATS = 100"])
    stmts = ["RESTORE DATABASE [%s] FROM DISK = N'%s' WITH %s" % (rt, _q(full_bak), withs)]
    if diff_bak:
        stmts.append("RESTORE DATABASE [%s] FROM DISK = N'%s' WITH RECOVERY, STATS = 100" % (rt, _q(diff_bak)))
    stmts.append("DBCC CHECKDB ([%s]) WITH NO_INFOMSGS" % rt)
    return stmts


def run_drill(env: Env, db: str) -> Dict[str, object]:
    """还原演练：用本机留着的最新完整备份（+最新差异备份）还原到临时库 <db>_rt，检查完整性，然后删掉。"""
    core.check_db_name(db)
    rt = "%s_rt" % db
    job = "drill:%s" % db
    with file_lock(env.lock_path):
        rt_dir = env.staging / "rt"
        started = False      # 只有我们自己开始还原之后，才允许删除临时库（避免误删同名的已有库）
        try:
            state = core.load_state(env.state_path)
            base = (state.get("jobs") or {}).get("full:%s" % db)
            if not base:
                raise BackupError("没有完整备份记录，无法演练")
            full = env.staging / ("%s_full_%s.bak" % (db, base["stamp"]))
            if not full.exists():
                raise BackupError("本机没有完整备份文件 %s" % full.name)
            diffs = sorted(env.staging.glob("%s_diff_*_base_%s.bak" % (db, base["stamp"])))
            diff = diffs[-1] if diffs else None
            rows = env.sql.query("SELECT DB_ID(%s)", (rt,))
            if rows and rows[0][0] is not None:
                raise BackupError("%s 已经存在，先处理掉再演练" % rt)
            # 还原会按源库数据文件的原始大小分配磁盘（不是按备份文件大小），所以以数据文件大小为准
            rows = env.sql.query(DB_SIZE_SQL, (db,))
            size_mb = rows[0][0] if rows and rows[0][0] else 0
            need_gb = max(full.stat().st_size * 2.2 / 1e9, size_mb * 1.2 / 1024.0) + 1
            if env.free_gb(env.staging) < need_gb:
                raise BackupError("磁盘不够做演练：需要至少 %.1f GB" % need_gb)
            _prepare_dir(env.staging)
            _prepare_dir(rt_dir)
            filelist = env.sql.query("RESTORE FILELISTONLY FROM DISK = N'%s'" % _q(full))
            t = env.now()
            started = True
            for s in build_restore_statements(rt, full, diff, filelist, rt_dir):
                env.sql.execute(s)
            state = core.load_state(env.state_path)
            core.record_ok(state, job, env.now(), full=full.name, diff=diff.name if diff else None)
            core.save_state(env.state_path, state)
            log.info("%s 通过：%s%s，用时 %d 秒", job, full.name, (" + " + diff.name) if diff else "",
                     (env.now() - t).total_seconds())
            return {"job": job, "full": full.name, "diff": diff.name if diff else None}
        except Exception as e:
            _fail(env, job, str(e))
            raise
        finally:
            if started:
                try:
                    env.sql.execute("IF DB_ID(N'%s') IS NOT NULL DROP DATABASE [%s]" % (rt, rt))
                except Exception as e:  # 清理失败要让人看到
                    env.notify("error", "临时库 %s 没删掉，请手动处理：%s" % (rt, e))
            shutil.rmtree(str(rt_dir), ignore_errors=True)
