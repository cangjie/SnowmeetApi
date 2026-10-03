"""本机小代理：把浏览器的请求从指定网卡（例如家里的 Wi-Fi）直接发出去，绕开全局 VPN。

用途：电脑必须开着 VPN 时，仍让美团看到国内宽带的出口 IP。门店电脑不开 VPN，用不到它。
只监听 127.0.0.1，只做转发，不记录请求内容。Windows、macOS、Linux 通用。
"""

from __future__ import annotations

import asyncio
import socket
from urllib.parse import urlsplit

import psutil

from .log import log


def find_interface_ip(name: str) -> str:
    """按网卡名称（Windows：Wi-Fi、以太网；macOS：en0）找它的 IPv4 地址。"""
    stats = psutil.net_if_stats()
    for nic, addrs in psutil.net_if_addrs().items():
        if nic.lower() != name.lower():
            continue
        if nic in stats and not stats[nic].isup:
            raise RuntimeError(f"网卡「{name}」没有连接。")
        for a in addrs:
            if a.family == socket.AF_INET:
                return a.address
        raise RuntimeError(f"网卡「{name}」没有 IPv4 地址。")
    up = [n for n, s in stats.items() if s.isup]
    raise RuntimeError(f"找不到网卡「{name}」。可用网卡：{'、'.join(up)}")


async def open_from(bind_ip: str, host: str, port: int, timeout: float = 15):
    """建立一条从指定本机地址发出的 TCP 连接。"""
    return await asyncio.wait_for(
        asyncio.open_connection(host, port, local_addr=(bind_ip, 0), family=socket.AF_INET), timeout)


def _split_host_port(target: str, default_port: int) -> tuple[str, int]:
    host, sep, port = target.rpartition(":")
    if sep and port.isdigit():
        return host.strip("[]"), int(port)
    return target, default_port


async def _copy(reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
    try:
        while True:
            data = await reader.read(65536)
            if not data:
                break
            writer.write(data)
            await writer.drain()
    except (ConnectionError, OSError, asyncio.CancelledError):
        pass
    finally:
        try:
            writer.close()
        except Exception:
            pass


async def _pipe(client_r, client_w, up_r, up_w) -> None:
    tasks = {asyncio.create_task(_copy(client_r, up_w)), asyncio.create_task(_copy(up_r, client_w))}
    _, pending = await asyncio.wait(tasks, return_when=asyncio.FIRST_COMPLETED)
    for t in pending:
        t.cancel()


class DirectExitProxy:
    def __init__(self, bind_ip: str):
        self.bind_ip = bind_ip
        self.port = 0
        self._server: asyncio.base_events.Server | None = None

    @classmethod
    async def start(cls, interface: str) -> "DirectExitProxy":
        proxy = cls(find_interface_ip(interface))
        proxy._server = await asyncio.start_server(proxy._handle, "127.0.0.1", 0)
        proxy.port = proxy._server.sockets[0].getsockname()[1]
        return proxy

    async def _handle(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        try:
            header = await reader.readuntil(b"\r\n\r\n")
            first, _, rest = header.partition(b"\r\n")
            method, target, version = first.decode("latin-1").split(" ", 2)
            if method.upper() == "CONNECT":
                host, port = _split_host_port(target, 443)
                up_r, up_w = await open_from(self.bind_ip, host, port)
                writer.write(b"HTTP/1.1 200 Connection Established\r\n\r\n")
                await writer.drain()
                await _pipe(reader, writer, up_r, up_w)
            else:
                # 明文 HTTP：把绝对地址改回路径形式再转发。美团基本都是 HTTPS，这里只是兜底。
                u = urlsplit(target)
                path = (u.path or "/") + (f"?{u.query}" if u.query else "")
                lines = [l for l in rest.decode("latin-1").split("\r\n") if not l.lower().startswith("proxy-")]
                up_r, up_w = await open_from(self.bind_ip, u.hostname or "", u.port or 80)
                up_w.write(f"{method} {path} {version}\r\n".encode("latin-1") + "\r\n".join(lines).encode("latin-1"))
                await up_w.drain()
                await _pipe(reader, writer, up_r, up_w)
        except (asyncio.IncompleteReadError, asyncio.LimitOverrunError, ConnectionError, OSError, TimeoutError):
            pass  # 连接被任一端关闭，正常情况
        except Exception as e:
            log.warning("直连代理转发失败：%s", e)
        finally:
            try:
                writer.close()
            except Exception:
                pass

    async def stop(self) -> None:
        if self._server:
            self._server.close()
