"""常驻：浏览器一直开着并保持登录，按间隔自动抓订单，菜品档案每天抓一次。

部署时只需要让 `python -m meituan_collector serve` 随电脑登录自动启动（见 README）。
"""

from __future__ import annotations

import asyncio
import time

from . import locks
from .browser import BrowserSession, NeedLogin
from .config import Config
from .dishes import DishCatalogCollector
from .heartbeat import HeartbeatClient, RoundState, build_payload
from .log import log
from .orders import OrderCollector
from .recorder import Recorder
from .sanitizer import Sanitizer


async def run_round(session: BrowserSession, orders: OrderCollector, force_dishes: bool = False,
                    dishes_only: bool = False) -> list[str]:
    """在当前 session.page 上抓一轮：订单（含详情、退款）+ 当天还没抓过的菜品档案。"""
    warnings: list[str] = []
    if not dishes_only:
        r = await orders.run_once()
        log.info("订单完成（%s）：列表共 %d 单，抓详情 %d 张，未变化跳过 %d 张，内容有变化 %d 张，退款单 %d 笔。%s",
                 "、".join(r.days + [f"补抓 {d}" for d in r.backfill_days]), r.orders, r.details_fetched,
                 r.details_skipped, r.changed, r.refunds_seen,
                 f"还有 {r.backfill_pending} 天待补抓。" if r.backfill_pending else "")
        warnings += r.warnings
    catalog = DishCatalogCollector(session)
    if force_dishes or dishes_only or not catalog.today_file.exists():
        n_dishes, n_combos, w = await catalog.run()
        log.info("菜品档案完成：%d 道菜，%d 个套餐。", n_dishes, n_combos)
        warnings += w
    for w in warnings:
        log.warning(w)
    return warnings


async def serve(config: Config, sanitizer: Sanitizer, record: bool) -> int:
    session = BrowserSession(config, sanitizer)
    state = RoundState(started_at=time.monotonic())
    beat = HeartbeatClient(config, lambda: build_payload(
        state, session.waiting_login, session.login_restored_at, sanitizer.sanitize_text))
    beat_task = asyncio.create_task(beat.run())
    closed = asyncio.Event()
    try:
        await session.start("launch_only")
        session.context.on("close", lambda _: closed.set())
        if record:
            recorder = Recorder(config.out)
            session.listeners.append(recorder.save)
            log.info("接口返回记录在：%s", recorder.directory)
        if not await session.ensure_logged_in(wait_forever=True):
            return 1
        main = session.page
        orders = OrderCollector(session)
        log.info("常驻浏览器已就绪（调试端口 127.0.0.1:%d）。营业时段 %s 每 %d 分钟抓一轮，其余时间每 %d 分钟。",
                 config.debug_port, config.active_hours, config.interval_minutes, config.idle_interval_minutes)

        loop = asyncio.get_running_loop()
        next_run = loop.time()
        while not closed.is_set():
            if loop.time() >= next_run:
                if locks.cmd_recent(config.out):
                    # 有人刚用 cmd 操作过浏览器，先别抓，免得两边同时操作页面
                    log.info("最近有人在手动操作浏览器，本轮推迟 1 分钟。")
                    next_run = loop.time() + 60
                    continue
                async with locks.holding_round(config.out):
                    await _one_round(session, main, orders, state)
                # 有待补抓的日子时，非营业时段也按短间隔跑，尽快补完
                busy = config.is_active_now() or orders.backfill_pending > 0
                minutes = config.interval_minutes if busy else config.idle_interval_minutes
                next_run = loop.time() + minutes * 60
            try:
                await asyncio.wait_for(closed.wait(), 5)
            except TimeoutError:
                pass
        log.warning("浏览器窗口被关闭，常驻结束。")
        return 1  # 非 0 退出，让计划任务/启动项把它重新拉起来
    finally:
        beat_task.cancel()
        await session.close()


async def _one_round(session: BrowserSession, main, orders: OrderCollector, state: RoundState) -> None:
    # 每轮在新标签页里抓，抓完关掉；主标签页留给人看和登录。
    tracing = session.config.trace_rounds
    work = await session.context.new_page()
    session.page = work
    try:
        if tracing:
            await session.context.tracing.start(screenshots=True, snapshots=True)
        warnings = await run_round(session, orders)
        state.round_done(not warnings, warnings[0] if warnings else None, orders.backfill_pending)
        if tracing:
            await session.context.tracing.stop()
    except NeedLogin:
        log.warning("美团管家登录已失效，等人在浏览器窗口里重新登录。")
        if tracing:
            await session.context.tracing.stop()
        session.page = main
        await session.ensure_logged_in(wait_forever=True)
    except Exception as e:
        log.error("本轮出错：%r", e)
        state.round_done(False, repr(e), orders.backfill_pending)
        try:
            directory = await session.save_debug_bundle(e, work, trace=tracing)
            log.error("诊断包已保存：%s", directory)
        except Exception:
            pass
    finally:
        session.page = main
        try:
            await work.close()
        except Exception:
            pass
