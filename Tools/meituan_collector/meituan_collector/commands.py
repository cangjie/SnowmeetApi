"""cmd：操作常驻浏览器（不会关掉它），供开发人员勘察页面用。

  pages                     列出所有标签页
  goto <url|路径>           当前标签页打开地址（路径自动补 https://pos.meituan.com）
  click <文字>              点击页面上（含 iframe）显示这段文字的元素，优先完全匹配
  click-css <选择器>        按 CSS 选择器点击（可写 "选择器 >> nth=1"）
  fill-css <选择器> <文字>  清空后填入
  press <按键>              按键，例如 Enter、Escape
  text [最大字数]           输出页面（含 iframe）的可见文字（已打码）
  shot                      截图，输出图片路径
  eval <js>                 在页面主框架里执行 JS，输出结果
  frames                    列出页面里的 iframe
  close-page                关闭当前标签页（至少保留一个）
"""

from __future__ import annotations

import json
from datetime import datetime

from playwright.async_api import Page

from . import locks
from .browser import BrowserSession
from .config import Config
from .sanitizer import Sanitizer

ORIGIN = "https://pos.meituan.com"


async def _settle(page: Page) -> None:
    try:
        await page.wait_for_load_state("networkidle", timeout=15_000)
    except Exception:
        pass
    await page.wait_for_timeout(1000)


async def _title(page: Page) -> str:
    try:
        return await page.title()
    except Exception:
        return "(无标题)"


async def _click_text(page: Page, text: str) -> bool:
    for exact in (True, False):
        for frame in page.frames:
            loc = frame.get_by_text(text, exact=exact)
            for i in range(await loc.count()):
                if await loc.nth(i).is_visible():
                    await loc.nth(i).click(timeout=10_000)
                    return True
    return False


async def run(config: Config, sanitizer: Sanitizer, args: list[str], page_index: int) -> int:
    if not args:
        print(__doc__)
        return 2
    # 先告诉常驻程序「有人在操作」，再等它正在跑的那一轮结束，避免两边同时操作同一个页面。
    locks.touch_cmd(config.out)
    if not await locks.wait_round_finished(config.out):
        print("等了 15 分钟常驻程序还在抓取，先不操作。")
        return 1
    session = BrowserSession(config, sanitizer)
    await session.start("attach_existing", page_index)
    try:
        page, name, arg = session.page, args[0], " ".join(args[1:])
        if name == "pages":
            for i, p in enumerate(session.context.pages):
                print(f"[{i}] {await _title(p)} | {sanitizer.sanitize_text(p.url)}")
        elif name == "goto":
            url = arg if arg.startswith(("http", "about:")) else ORIGIN + ("" if arg.startswith("/") else "/") + arg
            await page.goto(url, wait_until="domcontentloaded", timeout=60_000)
            await _settle(page)
            print(f"已打开：{await _title(page)} | {page.url}")
        elif name == "click":
            if not await _click_text(page, arg):
                print(f"没找到文字「{arg}」")
                return 1
            await _settle(page)
            print(f"已点击「{arg}」；当前：{await _title(page)}；标签页数：{len(session.context.pages)}")
        elif name in ("click-css", "fill-css"):
            selector = args[1] if len(args) > 1 else ""
            for frame in page.frames:
                loc = frame.locator(selector)
                if await loc.count() == 0:
                    continue
                if name == "click-css":
                    await loc.first.click(timeout=10_000)
                    await _settle(page)
                    print(f"已点击 {selector}")
                else:
                    await loc.first.fill(" ".join(args[2:]), timeout=10_000)
                    print(f"已填入 {selector}")
                return 0
            print(f"没找到 {selector}")
            return 1
        elif name == "press":
            await page.keyboard.press(arg)
            await _settle(page)
            print(f"已按 {arg}")
        elif name == "text":
            limit = int(arg) if arg.isdigit() else 6000
            parts = []
            for frame in page.frames:
                try:
                    text = await frame.evaluate("() => document.body ? document.body.innerText : ''")
                except Exception:
                    continue
                if text and text.strip():
                    parts.append(f"----- frame: {frame.url[:140]}\n{text.strip()}")
            all_text = sanitizer.sanitize_text("\n".join(parts))
            print(all_text if len(all_text) <= limit else all_text[:limit] + f"\n…（共 {len(all_text)} 字，已截断）")
        elif name == "shot":
            path = config.out / "shots" / f"{datetime.now():%Y%m%d_%H%M%S}.png"
            path.parent.mkdir(parents=True, exist_ok=True)
            await page.screenshot(path=str(path))
            print(path)
        elif name == "eval":
            print(sanitizer.sanitize_text(json.dumps(await page.evaluate(arg), ensure_ascii=False)))
        elif name == "frames":
            for frame in page.frames:
                print(f"{frame.name} | {frame.url[:140]}")
        elif name == "close-page":
            if len(session.context.pages) <= 1:
                print("只剩一个标签页，不关。")
                return 1
            await page.close()
            print("已关闭。")
        else:
            print(f"不认识的命令：{name}\n{__doc__}")
            return 2
        return 0
    finally:
        await session.close()
