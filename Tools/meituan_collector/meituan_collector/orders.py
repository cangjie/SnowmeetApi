"""抓一轮订单。

检查范围：当天每轮都查；昨天每隔 previous_day_check_minutes 查一次（过了零点仍要接住昨天订单的退款、取消）；
前天及更早的订单不再检查。
补抓：从 start_date 起，某天的订单、详情、退款都抓全了（且那天已经过去）就记为「完整」（out/coverage.json）。
电脑停机几天、或把 start_date 改早，没完整的日子会按从早到晚逐天补上；每轮最多补 backfill_details_per_round 张详情。

每次检查：
1. 打开报表里的订单列表页（店内、外卖平台、外卖自营），切到「全部订单」，需要时再切到「昨日」；
2. 列表指纹（状态、金额、退款）有变化的单、新单，打开订单详情（菜品明细在详情里），有退款的再打开退款单详情；
3. 打开「退款单明细」列表：有新退款、本地还没记录的原订单，重新打开详情和退款单详情
   （店内部分退款后原订单的状态、金额都不变，只能靠退款单列表发现）；
4. 每隔 full_refresh_minutes 不看指纹，把检查范围内所有订单的详情重抓一遍兜底。
内容和上次保存的不同，就标记为变化：版本号 +1、写明变化内容、旧版本移到 history/，并记入当天的变化日志。

程序只截获页面自己发出的请求的返回结果。接口与字段说明见
snowmeet_ai_doc/docs/fnb/2026-10-03-meituan-pos-order-api.md。
"""

from __future__ import annotations

import asyncio
import json
import re
import time
from dataclasses import dataclass, field
from datetime import date, datetime, timedelta, timezone
from pathlib import Path
from typing import Any

from playwright.async_api import Error as PlaywrightError

from . import changes
from .browser import BrowserSession, Captured, NeedLogin
from .log import log
from .storage import read_json, write_csv, write_json

ORIGIN = "https://pos.meituan.com"
DETAIL_PAGE = ORIGIN + "/web/fe.rms-portal/rms-report.html#/rms-report/orderDetail?orderId="
REFUND_DETAIL_PAGE = ORIGIN + "/web/fe.rms-portal/rms-report.html#/rms-report/refundOrderDetail?refundId="
REFUND_LIST_PAGE = ORIGIN + "/web/report/refundOrderList#/rms-report/refundOrderList"
REFUND_LIST_API = "/reports/refund-order/list"
# 列表页默认只查「已结账」（店内）或「有效订单」（外卖），切到「全部订单」才包含取消、退单。
ALL_ORDERS_TAB = "全部订单"
NEXT_PAGE = ("li.saas-pagination-next:not(.saas-pagination-disabled), "
             "li[title='下一页']:not([class*=disabled]), "
             ".ant-pagination-next:not(.ant-pagination-disabled)")
DATE_PICKER = "input.ant-calendar-picker-input"
CALENDAR_POPUP = ".ant-calendar-picker-container"

# 列表页地址与它发出的列表接口（2026-10-03 勘察所得）。
LIST_PAGES = [
    ("店内", ORIGIN + "/web/report/orderList#/rms-report/orderList", "/reports/order-detail-instore/list"),
    ("外卖", ORIGIN + "/web/report/orderListWM#/rms-report/orderListWM", "/reports/order-detail-platform/list"),
    ("外卖自营", ORIGIN + "/web/report/orderListWMSelf#/rms-report/orderListWMSelf", "/reports/order-detail-self/list"),
]
PLATFORM_SOURCES = ("美团外卖", "饿了么", "淘宝闪购")

SUMMARY_HEADER = ["营业日", "类型", "来源", "订单号", "下单时间", "状态", "订单金额", "实付", "收入",
                  "菜品行数", "菜品", "退款金额", "退款菜品", "版本", "最后变化", "备注"]
CHANGES_HEADER = ["发现时间", "营业日", "类型", "订单号", "版本", "变化内容"]

BEIJING = timezone(timedelta(hours=8))


@dataclass
class RoundResult:
    orders: int = 0
    details_fetched: int = 0
    details_skipped: int = 0
    changed: int = 0
    refunds_seen: int = 0
    days: list[str] = field(default_factory=list)
    backfill_days: list[str] = field(default_factory=list)
    backfill_pending: int = 0
    warnings: list[str] = field(default_factory=list)


def _code(body: Any) -> int | None:
    return body.get("code") if isinstance(body, dict) else None


def _yuan(fen: Any) -> str:
    return f"{fen / 100:.2f}" if isinstance(fen, (int, float)) else ""


def _now() -> str:
    return datetime.now().strftime("%Y-%m-%d %H:%M:%S")


def day_start_ms(day: date) -> int:
    return int(datetime(day.year, day.month, day.day, tzinfo=BEIJING).timestamp() * 1000)


def request_start_ms(c: Captured) -> int | None:
    """列表请求里的开始日期：订单列表在顶层，退款单列表包在 reqJson 字符串里。"""
    try:
        post = json.loads(c.post_data or "{}")
        if isinstance(post.get("reqJson"), str):
            post = json.loads(post["reqJson"])
        v = post.get("startDate") or post.get("beginTime")
        return int(v) if v is not None else None
    except (ValueError, TypeError):
        return None


def request_end_ms(c: Captured) -> int | None:
    try:
        post = json.loads(c.post_data or "{}")
        if isinstance(post.get("reqJson"), str):
            post = json.loads(post["reqJson"])
        v = post.get("endDate") or post.get("endTime")
        return int(v) if v is not None else None
    except (ValueError, TypeError):
        return None


def all_orders_ok(kind: str, c: Captured) -> bool:
    """确认请求查的是「全部订单」：店内不带 statusList；外卖 tabType=1。"""
    try:
        post = json.loads(c.post_data or "{}")
    except ValueError:
        return False
    if kind == "店内":
        return not post.get("statusList")
    return str(post.get("tabType")) == "1"


def is_refund_row(ob: dict) -> bool:
    """「全部订单」列表里单独出现的退款单行（单号 88 开头、状态 30「退款完成」、金额为负）。
    它没有订单详情（打开会提示「查询结果为空」），要按退款单详情去取。"""
    amount = ob.get("amount")
    return ob.get("status") == 30 or (isinstance(amount, (int, float)) and amount < 0)


def fingerprint(ob: dict) -> str:
    """订单有变化（状态、金额、退款）才重新打开详情。"""
    return "|".join(str(ob.get(k)) for k in ("status", "amount", "payed", "refundStatus", "partRefundCnt"))


def kind_from_detail(detail: dict) -> str:
    ob = detail.get("orderBase") or {}
    if ob.get("typeName") == "外卖" or detail.get("wm"):
        return "外卖" if any(s in (ob.get("sourceName") or "") for s in PLATFORM_SOURCES) else "外卖自营"
    return "店内"


def summary_row(kind: str, ob: dict, detail: dict | None, refunds: list[dict], saved: dict | None = None) -> list[str]:
    items = (detail or {}).get("itemList") or []
    dishes = "；".join(
        f"{i.get('name')}{'(' + i['specs'] + ')' if i.get('specs') else ''}×{i.get('count')}" for i in items)
    refund_fen = 0
    refund_dishes = []
    for r in refunds:
        amount = (r.get("refundOrderBase") or {}).get("refundedAmount")
        if isinstance(amount, (int, float)):
            refund_fen += amount
        for it in r.get("refundItemList") or []:
            refund_dishes.append(f"{it.get('spuName') or it.get('name')}×{it.get('count')}")
    ordered = ob.get("orderTime")
    order_time = (datetime.fromtimestamp(ordered / 1000, BEIJING).strftime("%Y-%m-%d %H:%M:%S")
                  if isinstance(ordered, (int, float)) and ordered > 0 else "")
    saved = saved or {}
    last = (saved.get("changes") or [None])[-1]
    return [
        ob.get("businessTime") or "", kind, ob.get("sourceName") or "", str(ob.get("orderNo") or ""),
        order_time, ob.get("statusName") or "", _yuan(ob.get("amount")), _yuan(ob.get("payed")),
        _yuan(ob.get("income")), str(len(items)), dishes,
        _yuan(refund_fen) if refunds else "", "；".join(refund_dishes),
        str(saved.get("version") or ""), f"{last['at']} {'；'.join(last['items'])}" if last else "",
        "" if detail is not None else ("退款单（明细见原订单）" if is_refund_row(ob) and refunds else "详情未取到"),
    ]


async def click_text(session: BrowserSession, text: str) -> bool:
    """页面上（含 iframe）显示这段文字的可见元素，完全匹配，点第一个。"""
    for frame in session.page.frames:
        loc = frame.get_by_text(text, exact=True)
        for i in range(await loc.count()):
            if await loc.nth(i).is_visible():
                await loc.nth(i).click(timeout=10_000)
                return True
    return False


async def click_css(session: BrowserSession, selector: str, wait_ms: int = 5000) -> bool:
    """在页面（含 iframe）里找到可见的元素并点击；弹层有动画，最多等 wait_ms。"""
    deadline = time.monotonic() + wait_ms / 1000
    while True:
        for frame in session.page.frames:
            loc = frame.locator(selector)
            for i in range(await loc.count()):
                if await loc.nth(i).is_visible():
                    await loc.nth(i).click(timeout=10_000)
                    return True
        if time.monotonic() >= deadline:
            return False
        await asyncio.sleep(0.3)


async def click_next_page(session: BrowserSession, list_api: str, match=None) -> Captured | None:
    """分页组件：报表页和菜品库都是 saas-pagination。"""
    for frame in session.page.frames:
        nxt = frame.locator(NEXT_PAGE)
        if await nxt.count() == 0:
            continue
        try:
            return await session.capture_during(lambda: nxt.first.click(), list_api, 30, match)
        except (TimeoutError, PlaywrightError):
            return None
    return None


class OrderCollector:
    def __init__(self, session: BrowserSession):
        self.session = session
        self.config = session.config
        self.out = session.config.out
        self.state_path = self.out / "state.json"
        # 订单号 → 指纹，用来跳过没变化的详情
        self.state: dict[str, str] = read_json(self.state_path, {})
        # 已经抓完整的营业日（补抓用）
        self.coverage_path = self.out / "coverage.json"
        self.coverage: dict = {}
        # 常驻时这两个时间跨轮保留；单次运行（once）每次都会查昨天、都会全量刷新
        self.last_previous_day_check: float | None = None
        self.last_full_refresh: float | None = None
        self.backfill_pending = 0
        self._failures = 0  # 详情、退款单打开失败的次数，用来判断某天是否抓完整

    async def run_once(self) -> RoundResult:
        self.session.listeners.append(self._save_dictionaries)
        try:
            return await self._run_once()
        finally:
            self.session.listeners.remove(self._save_dictionaries)

    def _due(self, last: float | None, minutes: int) -> bool:
        return last is None or time.monotonic() - last >= minutes * 60

    async def _run_once(self) -> RoundResult:
        result = RoundResult()
        today = date.today()
        self._load_coverage(today)
        check_previous = self._due(self.last_previous_day_check, self.config.previous_day_check_minutes)
        force = self._due(self.last_full_refresh, self.config.full_refresh_minutes)
        regular = [today] + ([today - timedelta(days=1)] if check_previous else [])
        if force:
            log.info("本轮不看指纹，重抓检查范围内（%s）所有订单的详情。", "、".join(d.isoformat() for d in regular))

        # 1. 今天（每轮）和昨天（每隔一段时间）
        for day in regular:
            complete, _ = await self._check_day(day, force, None, result)
            result.days.append(day.isoformat())
            if complete:
                self._mark_complete(day)

        # 2. 补抓：start_date 起没抓完整的日子，从早到晚，一天补完再补下一天
        budget = [self.config.backfill_details_per_round]
        for day in self._pending_days(today):
            if budget[0] <= 0:
                break
            log.info("补抓 %s（每轮最多 %d 张详情）……", day.isoformat(), self.config.backfill_details_per_round)
            complete, remaining = await self._check_day(day, False, budget, result)
            result.backfill_days.append(day.isoformat())
            if complete:
                self._mark_complete(day)
                log.info("补抓 %s 完成。", day.isoformat())
                continue
            if remaining:
                log.info("补抓 %s：还剩 %d 张详情，下一轮继续。", day.isoformat(), remaining)
            break
        result.backfill_pending = self.backfill_pending = len(self._pending_days(today))

        if check_previous:
            self.last_previous_day_check = time.monotonic()
        if force:
            self.last_full_refresh = time.monotonic()
        return result

    async def _check_day(self, day: date, force: bool, budget: list[int] | None,
                         result: RoundResult) -> tuple[bool, int]:
        """查一个营业日：订单列表 → 需要的详情 → 退款单列表 → 汇总表。
        budget 不为空时最多打开这么多张详情（补抓用），剩下的下一轮再来。
        返回（这一天是否已抓完整, 因额度不够还没抓的详情数）。"""
        failures_before = self._failures
        lists_ok = True
        orders: list[tuple[str, dict]] = []
        for kind, page_url, list_api in LIST_PAGES:
            items, ok = await self._fetch_list(kind, page_url, list_api, day, result.warnings)
            lists_ok &= ok
            orders += [(kind, i) for i in items]
        result.orders += len(orders)

        rows: list[list[str]] = []
        remaining = 0
        for kind, item in orders:
            ob = item.get("orderBase") or {}
            order_id, order_no = ob.get("id"), ob.get("orderNo")
            if not order_id or not order_no:
                result.warnings.append(f"{kind} 列表里有一张单缺少 id 或订单号，已跳过")
                continue
            business_date = (ob.get("businessTime") or day.strftime("%Y/%m/%d")).replace("/", "-")
            if (budget is not None and budget[0] <= 0
                    and self._needs_fetch(kind, str(order_no), business_date, item, force)):
                remaining += 1
                old = read_json(self._order_file(business_date, kind, str(order_no)))
                rows.append(summary_row(kind, ob, (old or {}).get("detail"), (old or {}).get("refunds") or [], old))
                continue
            fetched_before = result.details_fetched
            saved = await self._process_order(kind, str(order_id), str(order_no), business_date, item, force, result)
            if budget is not None:
                budget[0] -= result.details_fetched - fetched_before
            rows.append(summary_row(kind, ob, (saved or {}).get("detail"), (saved or {}).get("refunds") or [], saved))

        # 退款单列表：接住订单字段不变的退款（店内部分退款），以及过了零点才发生的退款
        refunds, refunds_ok = await self._fetch_refund_list(day, result.warnings)
        for refund in refunds:
            result.refunds_seen += 1
            await self._process_refund(refund, result)

        write_json(self.state_path, self.state)
        write_csv(self.out / f"summary_{day.isoformat()}.csv", SUMMARY_HEADER, rows)
        complete = (day < date.today() and lists_ok and refunds_ok and remaining == 0
                    and self._failures == failures_before)
        return complete, remaining

    # ---------- 补抓进度 ----------

    def _load_coverage(self, today: date) -> dict:
        cov = read_json(self.coverage_path, {}) or {}
        # 配置里写了 start_date 就以配置为准；没写就沿用上次记录的；第一次运行从今天开始。
        start = self.config.start_date or cov.get("start_date") or today.isoformat()
        date.fromisoformat(start)  # 格式不对就直接报错
        cov.setdefault("complete", {})
        if cov.get("start_date") != start:
            cov["start_date"] = start
            write_json(self.coverage_path, cov)
        self.coverage = cov
        return cov

    def _mark_complete(self, day: date) -> None:
        if day.isoformat() not in self.coverage["complete"]:
            self.coverage["complete"][day.isoformat()] = _now()
            write_json(self.coverage_path, self.coverage)

    def _pending_days(self, today: date) -> list[date]:
        """start_date 到前天之间还没抓完整的日子（昨天由定时的检查负责）。"""
        d = date.fromisoformat(self.coverage["start_date"])
        days = []
        while d <= today - timedelta(days=2):
            if d.isoformat() not in self.coverage["complete"]:
                days.append(d)
            d += timedelta(days=1)
        return days

    # ---------- 单张订单 ----------

    def _order_file(self, business_date: str, kind: str, order_no: str) -> Path:
        return self.out / "orders" / business_date / f"{kind}_{order_no}.json"

    def _find_order_file(self, order_no: str) -> Path | None:
        found = sorted((self.out / "orders").glob(f"*/*_{order_no}.json"))
        return found[-1] if found else None

    def _needs_fetch(self, kind: str, order_no: str, business_date: str, item: dict, force: bool) -> bool:
        if force or not self._order_file(business_date, kind, order_no).exists():
            return True
        return self.state.get(order_no) != fingerprint(item.get("orderBase") or {})

    async def _process_order(self, kind: str, order_id: str, order_no: str, business_date: str,
                             item: dict | None, force: bool, result: RoundResult) -> dict | None:
        file = self._order_file(business_date, kind, order_no)
        old = read_json(file)
        fp = fingerprint(item.get("orderBase") or {}) if item else None
        if not force and old is not None and fp is not None and self.state.get(order_no) == fp:
            result.details_skipped += 1
            return old

        if item is not None and is_refund_row(item.get("orderBase") or {}):
            refund = await self._fetch_refund(order_id, result.warnings)
            if refund is None:
                return old
            result.details_fetched += 1
            saved = self._save(file, kind, order_no, business_date, item, None, [refund], old, result)
            if fp is not None:
                self.state[order_no] = fp
            return saved

        detail = await self._fetch_detail(order_id, result.warnings)
        if detail is None:
            return old  # 这次没取到就保留上次的
        refunds = []
        # 有退款单的，再打开退款单详情：退了哪道菜、几份、退款方式都在那里。
        for vo in detail.get("refundOrderDetailVOs") or []:
            refund_id = (vo.get("refundOrderBase") or {}).get("refundId")
            if refund_id:
                refund = await self._fetch_refund(str(refund_id), result.warnings)
                if refund is not None:
                    refunds.append(refund)
        result.details_fetched += 1
        saved = self._save(file, kind, order_no, business_date,
                           item if item is not None else (old or {}).get("list"), detail, refunds, old, result)
        if fp is not None:
            self.state[order_no] = fp
        await asyncio.sleep(1.5)  # 低频：逐张打开详情时稍作间隔
        return saved

    def _save(self, file: Path, kind: str, order_no: str, business_date: str, item: dict | None,
              detail: dict | None, refunds: list[dict], old: dict | None, result: RoundResult) -> dict:
        norm = changes.normalize(detail, refunds)
        new_digest = changes.digest(norm)
        now = _now()
        doc = {
            "kind": kind, "list": item, "detail": detail, "refunds": refunds,
            "firstCapturedAt": now, "capturedAt": now, "version": 1, "changed": False, "changes": [],
            "digest": new_digest,
        }
        if old is not None:
            old_norm = changes.normalize(old.get("detail"), old.get("refunds") or [])
            old_digest = old.get("digest") or changes.digest(old_norm)
            doc["firstCapturedAt"] = old.get("firstCapturedAt") or old.get("capturedAt") or now
            doc["version"] = old.get("version") or 1
            doc["changes"] = old.get("changes") or []
            doc["changed"] = bool(doc["changes"])
            if new_digest != old_digest:
                described = changes.describe(old_norm, norm)
                doc["version"] += 1
                doc["changed"] = True
                doc["changes"] = doc["changes"] + [{"at": now, "version": doc["version"], "items": described}]
                # 旧版本留底
                history = file.parent / "history" / f"{file.stem}_v{doc['version'] - 1}.json"
                write_json(history, old)
                self._log_change(now, business_date, kind, order_no, doc["version"], described)
                result.changed += 1
                log.info("订单 %s 有变化（第 %d 版）：%s", order_no, doc["version"], "；".join(described))
        write_json(file, doc)
        return doc

    def _log_change(self, at: str, business_date: str, kind: str, order_no: str, version: int,
                    items: list[str]) -> None:
        path = self.out / f"changes_{at[:10]}.csv"
        existing: list[list[str]] = []
        if path.exists():
            import csv
            with path.open(encoding="utf-8-sig", newline="") as f:
                existing = list(csv.reader(f))[1:]
        write_csv(path, CHANGES_HEADER, existing + [[at, business_date, kind, order_no, str(version), "；".join(items)]])

    async def _process_refund(self, refund: dict, result: RoundResult) -> None:
        refund_id = str(refund.get("refundOrderId") or "")
        order_id = str(refund.get("originalOrderId") or "")
        order_no = str(refund.get("originOrderNo") or "")
        if not refund_id or not order_id or not order_no:
            return
        file = self._find_order_file(order_no)
        saved = read_json(file) if file else None
        if saved is not None:
            known = {str((r.get("refundOrderBase") or {}).get("refundId")) for r in saved.get("refunds") or []}
            if refund_id in known:
                return
            kind = saved.get("kind") or file.stem.split("_", 1)[0]
            business_date = file.parent.name
            log.info("退款单 %s 对应的订单 %s 还没有这笔退款，重新抓取。", refund_id, order_no)
            await self._process_order(kind, order_id, order_no, business_date, None, True, result)
            return
        # 本地没有这张单（例如更早的订单今天才退款）：直接按详情保存
        detail = await self._fetch_detail(order_id, result.warnings)
        if detail is None:
            return
        ob = detail.get("orderBase") or {}
        kind = kind_from_detail(detail)
        business_date = (ob.get("businessTime") or "").replace("/", "-") or date.today().isoformat()
        log.info("退款单 %s 对应的订单 %s（%s）本地没有，补抓。", refund_id, order_no, business_date)
        refunds = []
        for vo in detail.get("refundOrderDetailVOs") or []:
            rid = (vo.get("refundOrderBase") or {}).get("refundId")
            if rid:
                r = await self._fetch_refund(str(rid), result.warnings)
                if r is not None:
                    refunds.append(r)
        result.details_fetched += 1
        self._save(self._order_file(business_date, kind, order_no), kind, order_no, business_date,
                   None, detail, refunds, None, result)

    # ---------- 页面操作 ----------

    async def _goto(self, url: str) -> None:
        await self.session.page.goto(url, wait_until="domcontentloaded", timeout=60_000)

    async def _date_inputs(self) -> list[str]:
        values: list[str] = []
        for frame in self.session.page.frames:
            try:
                values += await frame.eval_on_selector_all(DATE_PICKER, "els => els.map(e => e.value)")
            except PlaywrightError:
                pass
        return values

    async def _visible(self, selector: str):
        for frame in self.session.page.frames:
            loc = frame.locator(selector)
            for i in range(await loc.count()):
                if await loc.nth(i).is_visible():
                    return loc.nth(i)
        return None

    async def _pick_date(self, index: int, day: date) -> bool:
        """点开第 index 个日期框（0 开始、1 结束），在日历里翻到那个月，点那一天。出任何问题都返回 False。"""
        try:
            return await self._pick_date_once(index, day)
        except (PlaywrightError, TimeoutError) as e:
            log.info("选日期 %s 出错：%s", day.isoformat(), str(e).splitlines()[0])
            try:
                await self.session.page.keyboard.press("Escape")  # 关掉可能还开着的弹层
            except PlaywrightError:
                pass
            return False

    async def _pick_date_once(self, index: int, day: date) -> bool:
        pickers = []
        for frame in self.session.page.frames:
            loc = frame.locator(DATE_PICKER)
            for i in range(await loc.count()):
                if await loc.nth(i).is_visible():
                    pickers.append(loc.nth(i))
        if len(pickers) <= index:
            return False
        await asyncio.sleep(0.4)
        already_open = await self._visible(CALENDAR_POPUP)
        if index == 1 and already_open is not None:
            # 选完开始日期后，页面会自动弹出结束日期的日历；这时再点结束框反而会把它关掉
            pass
        else:
            if already_open is not None:
                await self.session.page.keyboard.press("Escape")  # 关掉残留的弹层再开
                await asyncio.sleep(0.4)
            await pickers[index].click(timeout=10_000)
        await asyncio.sleep(0.6)  # 弹层有展开动画，动画中点格子会「不稳定」甚至被重新渲染
        title = f"{day.year}年{day.month}月{day.day}日"
        for _ in range(60):
            popup = await self._visible(CALENDAR_POPUP)  # 每次重新找：弹层可能被重新渲染
            if popup is None:
                await asyncio.sleep(0.25)
                continue
            cell = popup.locator(f"td[title='{title}']")
            if await cell.count():
                if "disabled" in (await cell.first.get_attribute("class") or ""):
                    return False
                await cell.first.click(timeout=5_000)
                break
            m = re.search(r"(\d+)年(\d+)月", await popup.locator(".ant-calendar-ym-select").first.inner_text())
            if not m:
                return False
            current = (int(m.group(1)), int(m.group(2)))
            button = ".ant-calendar-prev-month-btn" if (day.year, day.month) < current else ".ant-calendar-next-month-btn"
            await popup.locator(button).first.click(timeout=5_000)
            await asyncio.sleep(0.3)
        else:
            return False
        shown = day.strftime("%Y/%m/%d")
        for _ in range(20):  # 点完后日期框是异步更新的
            if await pickers[index].input_value() == shown:
                return True
            await asyncio.sleep(0.25)
        return False

    async def _switch_to_day(self, api: str, day: date, label: str, warnings: list[str],
                             extra_match=None) -> Captured | None:
        """把开始、结束日期都设成 day，等日期框真的变了再点查询，等待开始、结束日期都对的那次列表请求。"""
        want_start = day_start_ms(day)
        want_end = want_start + 86_400_000 - 1

        async def query():
            if not await click_text(self.session, "查询"):
                raise TimeoutError()

        def match(c: Captured) -> bool:
            return (request_start_ms(c) == want_start and request_end_ms(c) == want_end
                    and (extra_match is None or extra_match(c)))

        for attempt in (1, 2):
            if not await self._pick_date(0, day) or not await self._pick_date(1, day):
                log.info("%s：第 %d 次没能在日历里选中 %s", label, attempt, day.isoformat())
                continue
            await asyncio.sleep(1)  # 日期框显示变了之后，页面内部状态还要一会儿才跟上
            try:
                return await self.session.capture_during(query, api, 15 if attempt == 1 else 30, match)
            except (TimeoutError, PlaywrightError):
                log.info("%s：第 %d 次查询没等到这一天的数据，%s", label, attempt, "重试" if attempt == 1 else "放弃")
        warnings.append(f"{label}：没能切到 {day.isoformat()} 的列表")
        return None

    async def _fetch_list(self, kind: str, page_url: str, list_api: str, day: date,
                          warnings: list[str]) -> tuple[list[dict], bool]:
        label = f"{kind}（{day.isoformat()}）"
        result: list[dict] = []
        ok = True
        try:
            await self.session.page.goto("about:blank")
            page = await self.session.capture_during(lambda: self._goto(page_url), list_api, 60)
        except (TimeoutError, PlaywrightError) as e:
            if self.session.on_login_page:
                raise NeedLogin()
            warnings.append(f"{label}：打开列表页没等到列表数据（{list_api}）：{str(e).splitlines()[0] if str(e) else '超时'}")
            return result, False

        # 切到「全部订单」，这样取消单、退单也会出现在列表里。
        async def click_all():
            if not await click_text(self.session, ALL_ORDERS_TAB):
                raise TimeoutError()
        try:
            page = await self.session.capture_during(click_all, list_api, 30, lambda c: all_orders_ok(kind, c))
        except (TimeoutError, PlaywrightError):
            warnings.append(f"{label}：没能切到「{ALL_ORDERS_TAB}」，可能缺取消单、退单")
            ok = False

        if day != date.today():
            switched = await self._switch_to_day(list_api, day, label, warnings, lambda c: all_orders_ok(kind, c))
            if switched is None:
                return result, False
            page = switched
        if not all_orders_ok(kind, page):
            warnings.append(f"{label}：列表请求不是「全部订单」，可能缺取消单、退单")
            ok = False

        page_no = 1
        while True:
            body = page.body
            if _code(body) not in (0, None):
                warnings.append(f"{label}：列表接口返回 code={_code(body)} {body.get('message') or body.get('msg')}")
                ok = False
                break
            data = (body or {}).get("data") or {}
            result += data.get("orderList") or []
            total_pages = data.get("totalPageCount") or 1
            log.info("%s：第 %d/%d 页，累计 %d 单（共 %s 单）", label, page_no, max(total_pages, 1), len(result),
                     data.get("total"))
            if page_no >= total_pages:
                break
            nxt = await click_next_page(self.session, list_api)
            if nxt is None:
                warnings.append(f"{label}：共 {total_pages} 页，但翻到第 {page_no} 页后没能继续")
                ok = False
                break
            page, page_no = nxt, page_no + 1
        return result, ok

    async def _fetch_refund_list(self, day: date, warnings: list[str]) -> tuple[list[dict], bool]:
        label = f"退款单列表（{day.isoformat()}）"
        try:
            await self.session.page.goto("about:blank")
            page = await self.session.capture_during(lambda: self._goto(REFUND_LIST_PAGE), REFUND_LIST_API, 60)
        except (TimeoutError, PlaywrightError):
            warnings.append(f"{label}：60 秒内没等到数据")
            return [], False
        if day != date.today():
            switched = await self._switch_to_day(REFUND_LIST_API, day, label, warnings)
            if switched is None:
                return [], False
            page = switched
        items: list[dict] = []
        ok = True
        page_no = 1
        while True:
            data = (page.body or {}).get("data")
            if isinstance(data, str):  # 这个接口的 data 是 JSON 字符串
                try:
                    data = json.loads(data)
                except ValueError:
                    warnings.append(f"{label}：返回内容无法解析")
                    ok = False
                    break
            data = data or {}
            items += data.get("items") or []
            total_pages = (data.get("page") or {}).get("totalPageSize") or 1
            if page_no >= total_pages:
                break
            nxt = await click_next_page(self.session, REFUND_LIST_API)
            if nxt is None:
                warnings.append(f"{label}：共 {total_pages} 页，翻到第 {page_no} 页后没能继续")
                ok = False
                break
            page, page_no = nxt, page_no + 1
        log.info("%s：%d 笔", label, len(items))
        return items, ok

    async def _open_and_capture(self, url: str, api: str, label: str, warnings: list[str]) -> dict | None:
        try:
            # 同一个单页应用里只改 # 后面不一定重新请求，先回到空白页再打开。
            await self.session.page.goto("about:blank")
            r = await self.session.capture_during(lambda: self._goto(url), api, 60)
        except TimeoutError:
            warnings.append(f"{label}：60 秒内没等到数据")
            self._failures += 1
            return None
        except PlaywrightError as e:
            warnings.append(f"{label}：打开页面出错：{e}")
            self._failures += 1
            return None
        if _code(r.body) not in (0, None):
            warnings.append(f"{label} 返回 code={_code(r.body)}")
            self._failures += 1
            return None
        return (r.body or {}).get("data")

    async def _fetch_detail(self, order_id: str, warnings: list[str]) -> dict | None:
        return await self._open_and_capture(DETAIL_PAGE + order_id, f"/v1/orders/detail?orderId={order_id}",
                                            f"订单 {order_id} 详情", warnings)

    async def _fetch_refund(self, refund_id: str, warnings: list[str]) -> dict | None:
        data = await self._open_and_capture(REFUND_DETAIL_PAGE + refund_id,
                                            f"/v1/orders/refund/detail-pos?refundId={refund_id}",
                                            f"退款单 {refund_id} 详情", warnings)
        await asyncio.sleep(1.5)
        return data

    def _save_dictionaries(self, c: Captured) -> None:
        """报表页顺带请求的菜品分类、菜品名字典，按天保存一份。"""
        name = ("categories" if "/reports/goods/poi/categories/list" in c.url
                else "dish-names" if "/reports/dict/search-deleted-dish-list" in c.url else None)
        if name:
            write_json(self.out / "dishes" / f"{name}_{datetime.now():%Y-%m-%d}.json", c.body)
