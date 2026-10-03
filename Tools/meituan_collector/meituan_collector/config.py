"""本机配置 collector.json（不进 git）。相对路径一律相对于配置文件所在目录。"""

from __future__ import annotations

import json
from dataclasses import dataclass, field, fields
from datetime import datetime, time
from pathlib import Path

PROJECT_DIR = Path(__file__).resolve().parent.parent


@dataclass
class Config:
    # 美团管家入口；登录后会跳回这里。
    base_url: str = "https://pos.meituan.com/web"
    # 只用于在登录页预填手机号；验证码必须由人在浏览器窗口里输入。
    phone: str | None = None
    output_dir: str = "out"
    # 浏览器独立配置目录，保存美团的登录状态。不要和平时用的浏览器共用。
    profile_dir: str = "profile"
    # 用电脑上已装的浏览器：msedge 或 chrome。
    browser_channel: str = "msedge"
    # 可选代理，例如 socks5://127.0.0.1:1080。和 direct_interface 二选一。
    proxy: str | None = None
    # 可选：让浏览器从这块网卡直接出网（例如 Wi-Fi、en0），绕开电脑上开着的全局 VPN。
    direct_interface: str | None = None
    # 常驻时的抓取间隔：营业时段内 interval_minutes，其余时间 idle_interval_minutes。
    interval_minutes: int = 3
    idle_interval_minutes: int = 60
    active_hours: str = "06:00-24:00"
    # 昨天的订单和退款每隔多久再查一次（过了零点仍要接住昨天订单的退款、取消）；前天及更早的订单不再检查。
    previous_day_check_minutes: int = 60
    # 每隔多久不管指纹变没变，把检查范围内所有订单的详情重抓一遍（兜底：指纹覆盖不到的变化）。
    full_refresh_minutes: int = 120
    # 从哪个营业日开始抓（YYYY-MM-DD）。留空：从程序第一次运行那天开始。
    # 改成更早的日期、或电脑停机几天后重新开机，程序都会把没抓完整的日子逐天补上。
    start_date: str | None = None
    # 补抓时每轮最多打开多少张详情，避免补抓占住太久、耽误当天的抓取。
    backfill_details_per_round: int = 20
    # 只记录这些地址片段命中的接口返回（页面自己发的请求）。
    capture_url_contains: list[str] = field(default_factory=lambda: ["pos.meituan.com/"])
    # 命中这些片段的接口不记录（轮询、埋点、AI 助手等与业务数据无关的请求）。
    capture_url_excludes: list[str] = field(default_factory=lambda: [
        "/messages/get-num", "/aiassistant/", "/abtest/", "/access-customer-service", "/pushes/bind-token",
        "/support/resources/", "/cms/", "/market/", "/apaas/api/rmspartner/compactBO/checkAccountAuth",
    ])
    # 除内置规则外，额外要打码的字段名（不区分大小写，整名匹配）。
    extra_mask_keys: list[str] = field(default_factory=list)
    # 单次运行（login / record / once）等人登录的最长时间；常驻时会一直等。
    login_wait_minutes: int = 10
    # 常驻浏览器的调试端口（只监听本机）。常驻进程开着时，once、cmd 都连到同一个浏览器。
    debug_port: int = 9222
    # 常驻时每轮都录 Playwright trace，出错时存进诊断包（较占磁盘，排查问题时再开）。
    trace_rounds: bool = False
    # 心跳上报到 SnowmeetApi 服务器，登录失效、采集电脑停机时由服务器发企业微信提醒。
    # server_token 与服务器工作目录 config.meituanCollectorToken 的内容一致；留空则不上报。
    server_url: str = "https://mini.snowmeet.top"
    server_token: str | None = None

    config_dir: Path = field(default=Path.cwd(), repr=False)

    def resolve(self, path: str) -> Path:
        p = Path(path)
        return p if p.is_absolute() else (self.config_dir / p).resolve()

    @property
    def out(self) -> Path:
        return self.resolve(self.output_dir)

    def is_active_now(self, now: datetime | None = None) -> bool:
        now = now or datetime.now()
        start_s, end_s = self.active_hours.split("-")
        start, end = _parse_hm(start_s), _parse_hm(end_s)
        t = now.time()
        if end == time(0, 0) or end_s.strip() == "24:00":
            return t >= start
        return start <= t < end if start <= end else (t >= start or t < end)


def _parse_hm(s: str) -> time:
    s = s.strip()
    if s == "24:00":
        return time(0, 0)
    h, m = s.split(":")
    return time(int(h), int(m))


def _strip_comments(text: str) -> str:
    # 允许整行 // 注释，方便在配置里写说明。
    return "\n".join(line for line in text.splitlines() if not line.lstrip().startswith("//"))


def load(explicit: str | None = None) -> Config:
    candidates = [Path(explicit)] if explicit else [Path.cwd() / "collector.json", PROJECT_DIR / "collector.json"]
    for path in candidates:
        if not path.exists():
            continue
        raw = json.loads(_strip_comments(path.read_text(encoding="utf-8")))
        known = {f.name for f in fields(Config)} - {"config_dir"}
        unknown = set(raw) - known
        if unknown:
            raise ValueError(f"配置里有不认识的项：{', '.join(sorted(unknown))}")
        cfg = Config(**raw)
        cfg.config_dir = path.resolve().parent
        return cfg
    if explicit:
        raise FileNotFoundError(f"找不到配置文件：{explicit}")
    cfg = Config()
    cfg.config_dir = PROJECT_DIR
    return cfg
