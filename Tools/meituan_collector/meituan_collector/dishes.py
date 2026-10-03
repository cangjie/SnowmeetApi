"""菜品档案：打开「运营中心 → 菜品管理 → 菜品库」，由页面自己请求菜品列表，逐页翻完。

每道菜含规格与价格、分类、单位、做法、加料、标签、备注、图片；套餐另有一份带组成明细的列表。
"""

from __future__ import annotations

from datetime import datetime
from pathlib import Path

from playwright.async_api import Error as PlaywrightError

from .browser import BrowserSession, Captured
from .log import log
from .orders import click_next_page
from .storage import write_json

GOODS_PAGE = "https://pos.meituan.com/web/operation/goods/list#/rms-goods/goods/list"
LIST_API = "/admin/poi/goods/filter-es"
CATEGORY_TREE_API = "/goods/common/poi/categories/tree"


def is_combo_query(c: Captured) -> bool:
    """套餐列表请求带 spuType=20；菜品库主列表不带 spuType。"""
    return '"spuType": 20' in (c.post_data or "") or '"spuType":20' in (c.post_data or "")


def is_main_query(c: Captured) -> bool:
    return c.post_data is not None and '"spuType"' not in c.post_data


class DishCatalogCollector:
    def __init__(self, session: BrowserSession):
        self.session = session

    @property
    def today_file(self) -> Path:
        return self.session.config.out / "dishes" / f"catalog_{datetime.now():%Y-%m-%d}.json"

    async def run(self) -> tuple[int, int, list[str]]:
        warnings: list[str] = []
        side: dict[str, Captured] = {}

        def collect(c: Captured) -> None:
            if LIST_API in c.url and is_combo_query(c):
                side.setdefault("combos", c)
            if CATEGORY_TREE_API in c.url:
                side.setdefault("tree", c)

        self.session.listeners.append(collect)
        goods: list[dict] = []
        total = 0
        try:
            await self.session.page.goto("about:blank")
            page = await self.session.capture_during(
                lambda: self.session.page.goto(GOODS_PAGE, wait_until="domcontentloaded", timeout=60_000),
                LIST_API, 60, is_main_query)
            page_no = 1
            while True:
                data = (page.body or {}).get("data") or {}
                goods += data.get("goods") or []
                total = data.get("totalCount") or len(goods)
                total_pages = data.get("totalPageCount") or 1
                log.info("菜品库：第 %d/%d 页，累计 %d/%d 道", page_no, total_pages, len(goods), total)
                if page_no >= total_pages:
                    break
                nxt = await click_next_page(self.session, LIST_API, is_main_query)
                if nxt is None:
                    warnings.append(f"菜品库：共 {total_pages} 页，翻到第 {page_no} 页后没能继续")
                    break
                page, page_no = nxt, page_no + 1
        except (TimeoutError, PlaywrightError) as e:
            reason = str(e).splitlines()[0] if str(e) else "超时"
            warnings.append(f"菜品库：没等到菜品列表数据（{reason}）")
        finally:
            self.session.listeners.remove(collect)

        if len(goods) != total:
            warnings.append(f"菜品库：抓到 {len(goods)} 道，页面显示共 {total} 道")
        if "combos" not in side:
            warnings.append("菜品库：没等到套餐列表（spuType=20）")

        combos = (side["combos"].body or {}).get("data", {}).get("goods") if "combos" in side else None
        write_json(self.today_file, {
            "capturedAt": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
            "totalCount": total,
            "goods": goods,
            "combos": combos,
            "categoriesTree": (side["tree"].body or {}).get("data") if "tree" in side else None,
        })
        log.info("菜品档案：%s", self.today_file)
        return len(goods), len(combos or []), warnings
