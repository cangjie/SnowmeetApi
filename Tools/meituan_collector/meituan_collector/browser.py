"""一个保留登录状态的真实浏览器窗口。

所有美团接口请求都由页面自己发出，本程序只截获返回结果，不在浏览器外构造请求，也不处理滑块验证。
"""

from __future__ import annotations

import asyncio
import json
import time
import urllib.request
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any, Awaitable, Callable

from playwright.async_api import (
    BrowserContext, Error as PlaywrightError, Page, Playwright, Response, async_playwright,
)

from .config import Config
from .direct_exit import DirectExitProxy
from .log import log
from .sanitizer import Sanitizer


@dataclass
class Captured:
    at: datetime
    method: str
    url: str
    post_data: str | None
    status: int
    body: Any


class NeedLogin(Exception):
    """美团管家登录已失效，需要人在浏览器窗口里重新登录。"""


def serve_running(port: int) -> bool:
    try:
        with urllib.request.urlopen(f"http://127.0.0.1:{port}/json/version", timeout=2) as r:
            return r.status == 200
    except Exception:
        return False


class BrowserSession:
    """mode：
    attach_or_launch —— 常驻浏览器开着就连上去（另开一个自己的标签页），否则自己启动；
    attach_existing  —— 只连常驻浏览器，沿用已有标签页（cmd 用）；
    launch_only      —— 只自己启动（serve 用）。
    """

    def __init__(self, config: Config, sanitizer: Sanitizer):
        self.config = config
        self.sanitizer = sanitizer
        self.context: BrowserContext | None = None
        self.page: Page | None = None
        self.attached = False
        self.listeners: list[Callable[[Captured], None]] = []
        self._pw: Playwright | None = None
        self._own_page = False
        self._proxy: DirectExitProxy | None = None
        self._tasks: set[asyncio.Task] = set()
        # 心跳上报用：正在等人登录；最近一次等到登录的时间（time.monotonic()）
        self.waiting_login = False
        self.login_restored_at: float | None = None

    async def start(self, mode: str = "attach_or_launch", page_index: int = 0) -> None:
        self._pw = await async_playwright().start()
        running = await asyncio.to_thread(serve_running, self.config.debug_port)

        if mode != "launch_only" and running:
            browser = await self._pw.chromium.connect_over_cdp(f"http://127.0.0.1:{self.config.debug_port}")
            self.context = browser.contexts[0]
            self.attached = True
            self.context.on("response", self._on_response_event)
            if mode == "attach_existing" and len(self.context.pages) > page_index:
                self.page = self.context.pages[page_index]
            else:
                self.page = await self.context.new_page()
                self._own_page = True
            return
        if mode == "attach_existing":
            raise RuntimeError(f"常驻浏览器没有在运行（127.0.0.1:{self.config.debug_port} 无响应），请先运行 serve。")
        if mode == "launch_only" and running:
            raise RuntimeError("常驻浏览器已经在运行了。")

        kwargs: dict[str, Any] = dict(
            headless=False,
            channel=self.config.browser_channel,
            no_viewport=True,  # 跟随窗口大小，方便人操作
            locale="zh-CN",
            timezone_id="Asia/Shanghai",
            # 只监听本机，供后续的 once、cmd 连到同一个浏览器。
            args=[f"--remote-debugging-port={self.config.debug_port}", "--remote-debugging-address=127.0.0.1"],
        )
        if self.config.direct_interface:
            if self.config.proxy:
                raise RuntimeError("配置里 proxy 和 direct_interface 只能填一个。")
            self._proxy = await DirectExitProxy.start(self.config.direct_interface)
            kwargs["proxy"] = {"server": f"http://127.0.0.1:{self._proxy.port}"}
            log.info("浏览器经网卡「%s」(%s) 直接出网，不走 VPN。", self.config.direct_interface, self._proxy.bind_ip)
        elif self.config.proxy:
            kwargs["proxy"] = {"server": self.config.proxy}
            log.info("浏览器经代理 %s 出网。", self.config.proxy)

        profile = self.config.resolve(self.config.profile_dir)
        profile.mkdir(parents=True, exist_ok=True)
        for attempt in range(1, 7):
            try:
                self.context = await self._pw.chromium.launch_persistent_context(str(profile), **kwargs)
                break
            except PlaywrightError as e:
                in_use = "ProcessSingleton" in str(e) or "already in use" in str(e) or "exitCode=21" in str(e)
                if not in_use:
                    raise
                if attempt == 6:
                    raise RuntimeError(f"浏览器配置目录被占用（是不是已经有一个采集程序在运行？）：{profile}") from e
                # 刚重启时上一个浏览器可能还没完全退出，等一会儿再试
                log.warning("浏览器配置目录还被占用，%d 秒后重试（第 %d 次）。", 10, attempt)
                await asyncio.sleep(10)
        self.context.on("response", self._on_response_event)
        self.page = self.context.pages[0] if self.context.pages else await self.context.new_page()

    # ---------- 截获接口返回 ----------

    def _on_response_event(self, response: Response) -> None:
        task = asyncio.create_task(self._on_response(response))
        self._tasks.add(task)
        task.add_done_callback(self._tasks.discard)

    async def _on_response(self, response: Response) -> None:
        url = response.url
        cfg = self.config
        if not any(s in url for s in cfg.capture_url_contains) or any(s in url for s in cfg.capture_url_excludes):
            return
        if response.request.resource_type not in ("xhr", "fetch"):
            return
        if "json" not in response.headers.get("content-type", ""):
            return
        try:
            text = await response.text()
        except PlaywrightError as e:
            # 页面紧接着跳转了（例如未登录被带去登录页），返回体已取不到，属正常情况。
            if "No resource with given identifier" not in str(e) and "closed" not in str(e):
                log.warning("读取接口返回失败：%s：%s", url[:120], e)
            return
        try:
            body = self.sanitizer.sanitize(json.loads(text))
        except ValueError:
            body = self.sanitizer.sanitize_text(text)

        post = response.request.post_data
        if post is not None:
            try:
                post = json.dumps(self.sanitizer.sanitize(json.loads(post)), ensure_ascii=False)
            except ValueError:
                post = self.sanitizer.sanitize_text(post)

        captured = Captured(datetime.now(), response.request.method, self.sanitizer.sanitize_text(url),
                            post, response.status, body)
        for listener in list(self.listeners):
            try:
                listener(captured)
            except Exception as e:
                log.warning("处理接口返回出错：%s", e)

    async def capture_during(self, action: Callable[[], Awaitable[Any]], url_contains: str, timeout: float = 60,
                             match: Callable[[Captured], bool] | None = None) -> Captured:
        """执行一个页面操作，并等待页面随之发出的、地址含 url_contains 的接口返回（已打码）。"""
        future: asyncio.Future[Captured] = asyncio.get_running_loop().create_future()

        def offer(c: Captured) -> None:
            if not future.done() and url_contains in c.url and (match is None or match(c)):
                future.set_result(c)

        self.listeners.append(offer)
        try:
            await action()
            return await asyncio.wait_for(future, timeout)
        finally:
            self.listeners.remove(offer)

    # ---------- 登录 ----------

    @property
    def on_login_page(self) -> bool:
        # 登录页的地址里带 rms-account（例如 /web/rms-account#/login）。
        return "/rms-account" in (self.page.url if self.page else "")

    async def goto_home(self) -> None:
        await self.page.goto(self.config.base_url, wait_until="networkidle", timeout=60_000)

    async def ensure_logged_in(self, wait_forever: bool = False) -> bool:
        """停在登录页时预填手机号，然后等人自己获取并输入验证码（以及可能出现的滑块）。"""
        await self.goto_home()
        if not self.on_login_page:
            return True
        log.warning("美团管家未登录。请在浏览器窗口里登录：选择「验证码登录」，自己获取并输入验证码；出现滑块也请手动完成。")
        try:
            await self.page.bring_to_front()
        except PlaywrightError:
            pass
        self.waiting_login = True
        try:
            await self._prefill_phone()
            waited = 0
            while wait_forever or waited < self.config.login_wait_minutes * 60:
                await asyncio.sleep(2)
                waited += 2
                if self.page.is_closed():
                    return False
                if not self.on_login_page:
                    await self.page.wait_for_load_state("networkidle")
                    log.info("已登录。")
                    self.login_restored_at = time.monotonic()
                    return True
                if wait_forever and waited % 600 == 0:
                    log.warning("仍在等待登录（已等 %d 分钟）。", waited // 60)
            log.error("等了 %d 分钟仍未登录。", self.config.login_wait_minutes)
            return False
        finally:
            self.waiting_login = False

    async def _prefill_phone(self) -> None:
        # 登录表单在 rmslogin.meituan.com 的 iframe 里，要逐个 frame 找输入框。
        if not self.config.phone:
            return
        for _ in range(15):
            for frame in self.page.frames:
                try:
                    box = frame.get_by_placeholder("输入手机号")
                    if await box.count() == 0:
                        continue
                    if not await box.first.input_value():
                        await box.first.fill(self.config.phone)
                    return
                except PlaywrightError:
                    pass  # frame 正在加载或已销毁，下一轮再试
            await asyncio.sleep(1)
        log.warning("没找到手机号输入框，请手动填写（不影响登录）。")

    # ---------- 诊断 ----------

    async def save_debug_bundle(self, error: BaseException, page: Page | None = None, trace: bool = False) -> Path:
        """出错时保存截图 + trace + 当前地址，方便把现场带回来分析。"""
        page = page or self.page
        directory = self.config.out / "debug" / datetime.now().strftime("%Y%m%d_%H%M%S")
        directory.mkdir(parents=True, exist_ok=True)
        try:
            if page and not page.is_closed():
                await page.screenshot(path=str(directory / "screen.png"), full_page=True)
        except Exception as e:
            log.warning("截图失败：%s", e)
        if trace:
            try:
                await self.context.tracing.stop(path=str(directory / "trace.zip"))
            except Exception as e:
                log.warning("保存 trace 失败：%s", e)
        url = "(页面已关闭)" if not page or page.is_closed() else self.sanitizer.sanitize_text(page.url)
        (directory / "error.txt").write_text(
            f"时间：{datetime.now():%Y-%m-%d %H:%M:%S}\n地址：{url}\n错误：{error!r}\n", encoding="utf-8")
        return directory

    async def close(self) -> None:
        if self.attached:
            # 连上去的常驻浏览器不能关：只关自己开的标签页，然后断开。
            try:
                if self._own_page and self.page and not self.page.is_closed():
                    await self.page.close()
            except Exception:
                pass
        elif self.context:
            try:
                await self.context.close()
            except Exception:
                pass
        if self._pw:
            await self._pw.stop()
        if self._proxy:
            await self._proxy.stop()
