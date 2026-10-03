"""连通性自检：输出一段纯文本，门店电脑上运行后复制给开发人员即可。"""

from __future__ import annotations

import http.client
import json
import platform
import socket
import time
from datetime import datetime

from .browser import BrowserSession
from .config import Config
from .direct_exit import find_interface_ip
from .heartbeat import request
from .sanitizer import Sanitizer
from .storage import write_json

HOST = "pos.meituan.com"


def _https_get(host: str, path: str, bind_ip: str | None) -> tuple[int, str]:
    conn = http.client.HTTPSConnection(host, timeout=15, source_address=(bind_ip, 0) if bind_ip else None)
    try:
        conn.request("GET", path, headers={"User-Agent": "Mozilla/5.0 meituan-collector-probe"})
        resp = conn.getresponse()
        return resp.status, resp.read().decode("utf-8", "replace")
    finally:
        conn.close()


def _local_time(iso: str) -> str:
    try:
        return datetime.fromisoformat(iso).astimezone().strftime("%m-%d %H:%M:%S")
    except ValueError:
        return iso


async def run(config: Config, sanitizer: Sanitizer, check_login: bool) -> int:
    lines: list[str] = []

    def line(s: str) -> None:
        lines.append(s)
        print(s, flush=True)

    ok = True
    line(f"== 美团管家采集 自检 {datetime.now():%Y-%m-%d %H:%M:%S} ==")
    line(f"机器名：{platform.node()}；系统：{platform.platform()}；Python {platform.python_version()}")

    bind_ip = None
    if config.direct_interface:
        try:
            bind_ip = find_interface_ip(config.direct_interface)
            line(f"出网方式：经网卡「{config.direct_interface}」({bind_ip}) 直接出网，不走 VPN")
        except Exception as e:
            ok = False
            line(f"[失败] 出网网卡：{e}")
    else:
        line(f"出网方式：{'代理 ' + config.proxy + '（以下命令行检查不经过代理）' if config.proxy else '系统默认路由'}")

    # 1. DNS
    try:
        t = time.perf_counter()
        addrs = sorted({a[4][0] for a in socket.getaddrinfo(HOST, 443, socket.AF_INET)})
        line(f"[通过] DNS {HOST} → {', '.join(addrs)}（{(time.perf_counter() - t) * 1000:.0f} ms）")
    except Exception as e:
        ok = False
        line(f"[失败] DNS {HOST}：{e}")

    # 2. 443 端口
    try:
        t = time.perf_counter()
        with socket.create_connection((HOST, 443), timeout=10, source_address=(bind_ip, 0) if bind_ip else None):
            pass
        line(f"[通过] 连接 {HOST}:443（{(time.perf_counter() - t) * 1000:.0f} ms）")
    except Exception as e:
        ok = False
        line(f"[失败] 连接 {HOST}:443：{e}")

    # 3. 出口 IP（与浏览器走同一条出网路径）
    try:
        _, text = _https_get("myip.ipip.net", "/", bind_ip)
        text = text.strip()
        line(f"[信息] 出口 IP：{text}")
        if "中国" not in text or any(x in text for x in ("香港", "台湾", "澳门")):
            line("[警告] 出口不在中国大陆。用海外 IP 登录美团容易触发异地风控，建议关闭 VPN 或配置 direct_interface。")
    except Exception as e:
        line(f"[警告] 查询出口 IP 失败：{e}")

    # 4. 页面返回码
    try:
        t = time.perf_counter()
        status, _ = _https_get(HOST, "/web/rms-account", bind_ip)
        if status == 200:
            line(f"[通过] GET https://{HOST}/web/rms-account → 200（{(time.perf_counter() - t) * 1000:.0f} ms）")
        else:
            ok = False
            line(f"[失败] GET https://{HOST}/web/rms-account → {status}")
    except Exception as e:
        ok = False
        line(f"[失败] GET https://{HOST}/web/rms-account：{e}")

    # 5. 心跳服务器（登录失效、停机提醒靠它）。只读状态，不冒充常驻程序上报。
    if not config.server_token:
        line("[警告] 没有配置 server_token：不上报心跳，登录失效、电脑停机时不会有企业微信提醒。")
    else:
        try:
            t = time.perf_counter()
            status, text = request(config, "GET", "Status")
            ms = (time.perf_counter() - t) * 1000
            if status == 200:
                data = (json.loads(text) or {}).get("data") or {}
                last = data.get("last_heartbeat_utc")
                line(f"[通过] 心跳服务器 {config.server_url}（{ms:.0f} ms）。服务器记录的最后一次心跳："
                     f"{_local_time(last) if last else '还没有'}，状态：{data.get('status') or '无'}")
            elif status == 401:
                ok = False
                line(f"[失败] 心跳服务器拒绝了令牌（401）：server_token 和服务器上的 config.meituanCollectorToken 不一致。")
            else:
                ok = False
                line(f"[失败] 心跳服务器 {config.server_url} → HTTP {status}")
        except Exception as e:
            ok = False
            line(f"[失败] 连接心跳服务器 {config.server_url}：{e}")

    # 6. 浏览器出口 IP 与登录状态（常驻开着时连上去看，不会另开窗口）
    if check_login:
        session = BrowserSession(config, sanitizer)
        try:
            await session.start()
            try:
                await session.page.goto("https://myip.ipip.net", timeout=20_000)
                line(f"[信息] 浏览器出口 IP：{(await session.page.inner_text('body')).strip()}")
            except Exception as e:
                line(f"[警告] 浏览器查询出口 IP 失败：{e}")
            await session.goto_home()
            if session.on_login_page:
                line("[警告] 美团管家未登录（需要有人在采集程序的浏览器窗口里重新登录）。")
            else:
                line("[通过] 美团管家已登录。")
            line(f"[信息] 常驻浏览器：{'在运行' if session.attached else '没在运行（本次自检临时打开）'}")
        except Exception as e:
            ok = False
            line(f"[失败] 打开浏览器检查登录状态：{e}")
        finally:
            await session.close()

    line("== 结论：网络可用 ==" if ok else "== 结论：有失败项，请把以上全文发给开发人员 ==")
    report = config.out / "probe" / f"probe_{datetime.now():%Y%m%d_%H%M%S}.txt"
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(sanitizer.sanitize_text("\n".join(lines)) + "\n", encoding="utf-8")
    print(f"自检报告已保存：{report}")
    return 0 if ok else 1
