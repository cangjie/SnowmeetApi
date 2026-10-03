"""命令行入口：python -m meituan_collector <命令>

  serve [--record]                 常驻：浏览器一直开着并保持登录，按间隔自动抓取（部署用这个）
  once [--dishes | --dishes-only] [--trace]
                                   抓一轮当天订单和详情；菜品档案每天抓一次（--dishes 强制重抓）
  cmd <命令> [参数] [--page N]     操作常驻浏览器：pages / goto / click / click-css / fill-css / press /
                                   text / shot / eval / frames / close-page
  probe [--no-browser]             连通性自检，结果保存在 out/probe/
  login                            打开浏览器窗口，等人登录
  record [--trace]                 录制：保存页面发出的所有接口返回到 out/record/
  resanitize <目录>                用当前打码规则重新处理已保存的 JSON
全局选项：--config <path>         指定配置文件（默认当前目录或程序目录下的 collector.json）
"""

from __future__ import annotations

import argparse
import asyncio
import sys
from pathlib import Path

from . import commands, config as config_mod, locks, probe
from .browser import BrowserSession
from .log import init as init_log, log
from .orders import OrderCollector
from .recorder import Recorder, resanitize
from .sanitizer import Sanitizer
from .serve import run_round, serve


async def _once(cfg, sanitizer, args) -> int:
    session = BrowserSession(cfg, sanitizer)
    try:
        await session.start()
        if args.trace:
            await session.context.tracing.start(screenshots=True, snapshots=True)
        if not await session.ensure_logged_in():
            return 1
        async with locks.holding_round(cfg.out):  # 和常驻程序的一轮互斥
            warnings = await run_round(session, OrderCollector(session), args.dishes, args.dishes_only)
        if args.trace:
            await session.context.tracing.stop()
        return 0 if not warnings else 3
    except Exception as e:
        log.error("抓取出错：%r", e)
        directory = await session.save_debug_bundle(e, trace=args.trace)
        log.error("诊断包已保存：%s", directory)
        return 1
    finally:
        await session.close()


async def _login(cfg, sanitizer) -> int:
    session = BrowserSession(cfg, sanitizer)
    try:
        await session.start()
        return 0 if await session.ensure_logged_in() else 1
    finally:
        await session.close()


async def _record(cfg, sanitizer, trace: bool) -> int:
    session = BrowserSession(cfg, sanitizer)
    try:
        await session.start()
        recorder = Recorder(cfg.out)
        session.listeners.append(recorder.save)
        closed = asyncio.Event()
        session.context.on("close", lambda _: closed.set())
        session.page.on("close", lambda _: closed.set())
        if trace:
            await session.context.tracing.start(screenshots=True, snapshots=True)
        if not await session.ensure_logged_in():
            return 1
        log.info("录制中：请在浏览器窗口里打开要勘察的页面，关掉这个标签页或按 Ctrl+C 结束。保存位置：%s", recorder.directory)
        chunk = 0
        while not closed.is_set():
            try:
                await asyncio.wait_for(closed.wait(), 30)
            except TimeoutError:
                if trace:  # trace 每 30 秒存一段：直接关窗口时最多丢最后 30 秒
                    chunk += 1
                    await session.context.tracing.stop_chunk(path=str(recorder.directory / f"trace_{chunk:03d}.zip"))
                    await session.context.tracing.start_chunk()
        log.info("录制结束，共保存 %d 个接口返回。", recorder.count)
        return 0
    finally:
        await session.close()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="meituan_collector", description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--config")
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("serve")
    p.add_argument("--record", action="store_true")
    p = sub.add_parser("once")
    p.add_argument("--dishes", action="store_true")
    p.add_argument("--dishes-only", action="store_true")
    p.add_argument("--trace", action="store_true")
    p = sub.add_parser("cmd")
    p.add_argument("--page", type=int, default=0)
    p.add_argument("args", nargs=argparse.REMAINDER)
    p = sub.add_parser("probe")
    p.add_argument("--no-browser", action="store_true")
    sub.add_parser("login")
    p = sub.add_parser("record")
    p.add_argument("--trace", action="store_true")
    p = sub.add_parser("resanitize")
    p.add_argument("directory")
    args = parser.parse_args(argv)

    try:
        cfg = config_mod.load(args.config)
    except Exception as e:
        print(f"读取配置失败：{e}", file=sys.stderr)
        return 2
    cfg.out.mkdir(parents=True, exist_ok=True)
    init_log(cfg.out)
    sanitizer = Sanitizer(cfg.extra_mask_keys)

    try:
        if args.command == "serve":
            return asyncio.run(serve(cfg, sanitizer, args.record))
        if args.command == "once":
            return asyncio.run(_once(cfg, sanitizer, args))
        if args.command == "cmd":
            return asyncio.run(commands.run(cfg, sanitizer, args.args, args.page))
        if args.command == "probe":
            return asyncio.run(probe.run(cfg, sanitizer, not args.no_browser))
        if args.command == "login":
            return asyncio.run(_login(cfg, sanitizer))
        if args.command == "record":
            return asyncio.run(_record(cfg, sanitizer, args.trace))
        if args.command == "resanitize":
            n = resanitize(Path(args.directory), sanitizer)
            print(f"已按当前打码规则重新处理，改动 {n} 个文件。")
            return 0
    except KeyboardInterrupt:
        return 130
    except Exception as e:
        # 用 pythonw 无窗口运行时看不到控制台，致命错误一定要写进日志文件
        log.exception("程序出错退出：%s", e)
        if sys.stderr is not None:
            print(f"出错：{e}", file=sys.stderr)
        return 1
    return 2


if __name__ == "__main__":
    sys.exit(main())
