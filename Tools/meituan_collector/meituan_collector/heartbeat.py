"""向 SnowmeetApi 服务器上报心跳，服务器据此发企业微信提醒（登录失效、采集电脑停机）。

企业微信接口有 IP 白名单，门店电脑发不了，所以这里只上报状态，提醒一律由服务器发出。
上报内容只有运行状态，不含订单和顾客信息。没配置 server_token 时不上报。
"""

from __future__ import annotations

import asyncio
import http.client
import json
import platform
import time
from dataclasses import dataclass
from typing import Callable
from urllib.parse import urlsplit

from . import __version__
from .config import Config
from .direct_exit import find_interface_ip
from .log import log

INTERVAL_SECONDS = 300   # 服务器 20 分钟收不到就报停机，这里 5 分钟一次
POLL_SECONDS = 5         # 状态一变（比如登录失效）就立即上报，不等满 5 分钟
API_PATH = "/api/MeituanCollector"


@dataclass
class RoundState:
    """常驻程序的抓取进度，供心跳读取。时间都用 time.monotonic()，不受改系统时间影响。"""
    started_at: float
    last_round_at: float | None = None
    last_round_ok: bool | None = None
    last_problem: str | None = None
    backfill_pending: int = 0

    def round_done(self, ok: bool, problem: str | None, backfill_pending: int, now: float | None = None) -> None:
        self.last_round_at = time.monotonic() if now is None else now
        self.last_round_ok = ok
        self.last_problem = problem
        self.backfill_pending = backfill_pending


def build_payload(state: RoundState, waiting_login: bool, login_restored_at: float | None,
                  sanitize: Callable[[str], str], now: float | None = None) -> dict:
    now = time.monotonic() if now is None else now
    if waiting_login:
        status = "need_login"
    elif state.last_round_ok is False:
        status = "error"
    elif state.last_round_at is None:
        status = "running"   # 刚启动，还没完成第一轮
    else:
        status = "ok"
    # 服务器用它判断「程序在跑但一直完不成一轮」。刚启动、刚重新登录都从那一刻重新算，
    # 免得停机或等登录很久之后一恢复就被误报成卡住。
    baseline = max(t for t in (state.started_at, state.last_round_at, login_restored_at) if t is not None)
    message = sanitize(state.last_problem)[:300] if state.last_problem and status == "error" else None
    return {
        "status": status,
        "message": message,
        "machine": platform.node(),
        "version": __version__,
        "seconds_since_last_round": max(0, int(now - baseline)),
        "last_round_ok": state.last_round_ok,
        "backfill_pending": state.backfill_pending,
    }


def _bind_ip(config: Config) -> str | None:
    # 开着全局 VPN 的电脑要从指定网卡发（服务器在国内，走 VPN 不通）；网卡找不到就退回默认路由
    if not config.direct_interface:
        return None
    try:
        return find_interface_ip(config.direct_interface)
    except Exception:
        return None


def request(config: Config, method: str, action: str, body: dict | None = None,
            timeout: float = 15) -> tuple[int, str]:
    """同步发一个请求到 {server_url}/api/MeituanCollector/{action}，返回 (HTTP 状态码, 响应正文)。"""
    u = urlsplit(config.server_url.rstrip("/"))
    cls = http.client.HTTPSConnection if u.scheme == "https" else http.client.HTTPConnection
    bind_ip = _bind_ip(config)
    conn = cls(u.hostname, u.port, timeout=timeout, source_address=(bind_ip, 0) if bind_ip else None)
    try:
        headers = {"X-Collector-Token": config.server_token or "", "User-Agent": f"meituan-collector/{__version__}"}
        data = None
        if body is not None:
            data = json.dumps(body, ensure_ascii=False).encode("utf-8")
            headers["Content-Type"] = "application/json; charset=utf-8"
        conn.request(method, f"{u.path}{API_PATH}/{action}", body=data, headers=headers)
        resp = conn.getresponse()
        return resp.status, resp.read().decode("utf-8", "replace")
    finally:
        conn.close()


class HeartbeatClient:
    def __init__(self, config: Config, payload: Callable[[], dict]):
        self.config = config
        self.payload = payload
        self.enabled = bool(config.server_url and config.server_token)
        self._failing = False

    async def send(self) -> bool:
        body = self.payload()
        try:
            status, text = await asyncio.to_thread(request, self.config, "POST", "Heartbeat", body)
            ok = status == 200
            problem = f"HTTP {status}" + ("（令牌不对，检查 server_token 和服务器上的 config.meituanCollectorToken）"
                                          if status == 401 else "")
        except Exception as e:
            ok, problem = False, repr(e)
        # 只在开始失败、恢复时各记一次日志，免得断网时每 5 分钟刷一行
        if not ok and not self._failing:
            log.warning("心跳上报失败：%s。服务器收不到心跳，20 分钟后会发「采集电脑停机」提醒。", problem)
        elif ok and self._failing:
            log.info("心跳上报已恢复。")
        self._failing = not ok
        return ok

    async def run(self) -> None:
        if not self.enabled:
            log.info("没有配置 server_token，不上报心跳（登录失效、停机时不会有企业微信提醒）。")
            return
        log.info("心跳上报到 %s，每 %d 分钟一次。", self.config.server_url, INTERVAL_SECONDS // 60)
        last_status = None
        last_sent = 0.0
        while True:
            try:
                status = self.payload()["status"]
                if status != last_status or time.monotonic() - last_sent >= INTERVAL_SECONDS:
                    await self.send()
                    last_status, last_sent = status, time.monotonic()
            except Exception as e:  # 心跳出问题不能影响抓取
                log.warning("心跳出错：%r", e)
            await asyncio.sleep(POLL_SECONDS)
