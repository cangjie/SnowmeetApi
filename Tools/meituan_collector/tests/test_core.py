"""纯逻辑单元测试（不开浏览器）：打码、汇总行、配置。运行：python -m unittest discover -s tests"""

import json
import tempfile
import unittest
from datetime import datetime
from pathlib import Path

import csv
from datetime import date
from types import SimpleNamespace

from meituan_collector import changes
from meituan_collector import config as config_mod
from meituan_collector.dishes import is_combo_query, is_main_query
from meituan_collector.browser import Captured
from meituan_collector.orders import (
    SUMMARY_HEADER, OrderCollector, RoundResult, all_orders_ok, day_start_ms, fingerprint, is_refund_row, kind_from_detail,
    request_end_ms, request_start_ms, summary_row,
)
from meituan_collector.sanitizer import MASK, Sanitizer


def _detail(status="已结账", payed=2600, items=None, refund=False):
    d = {
        "orderBase": {"orderNo": "170549072609230005", "statusName": status, "status": 300,
                      "amount": 2600, "payed": payed, "income": payed, "businessTime": "2026/09/23"},
        "itemList": items if items is not None else [
            {"itemNo": "a", "name": "经典美式", "count": 1.0, "price": 1900, "totalPrice": 1900, "retreat": False},
            {"itemNo": "b", "name": "乌苏 红", "count": 1.0, "price": 700, "totalPrice": 700, "retreat": False},
        ],
        "payList": [{"payNo": "p1", "payTypeName": "扫码支付-微信", "payed": payed, "status": 1, "type": 1}],
    }
    refunds = [{"refundOrderBase": {"refundId": 2101224729512603701, "refundedAmount": 700, "refundStatus": 30,
                                    "refundReason": "错点"},
                "refundItemList": [{"spuName": "乌苏 红", "count": 1.0}]}] if refund else []
    return d, refunds


class ChangesTest(unittest.TestCase):
    def test_same_content_same_digest(self):
        d, r = _detail()
        self.assertEqual(changes.digest(changes.normalize(d, r)), changes.digest(changes.normalize(*_detail())))

    def test_partial_refund_described(self):
        old = changes.normalize(*_detail())
        new = changes.normalize(*_detail(refund=True))
        self.assertNotEqual(changes.digest(old), changes.digest(new))
        self.assertEqual(changes.describe(old, new), ["新增退款 7.00（乌苏 红×1.0），原因：错点"])

    def test_status_amount_and_items_described(self):
        old = changes.normalize(*_detail())
        items = [{"itemNo": "a", "name": "经典美式", "count": 2.0, "price": 1900, "totalPrice": 3800, "retreat": False},
                 {"itemNo": "c", "name": "拿铁", "count": 1.0, "price": 2500, "totalPrice": 2500, "retreat": False}]
        new = changes.normalize(*_detail(status="已撤单", payed=6300, items=items))
        text = changes.describe(old, new)
        self.assertIn("状态 已结账→已撤单", text)
        self.assertIn("实付 26.00→63.00", text)
        self.assertIn("加菜 拿铁×1.0", text)
        self.assertIn("菜品删除 乌苏 红×1.0", text)
        self.assertIn("经典美式 数量 1.0→2.0", text)


class SaveVersionTest(unittest.TestCase):
    def test_version_history_and_change_log(self):
        with tempfile.TemporaryDirectory() as d:
            cfg = config_mod.Config(output_dir="out")
            cfg.config_dir = Path(d)
            collector = OrderCollector(SimpleNamespace(config=cfg, listeners=[]))
            file = collector._order_file("2026-09-23", "店内", "170549072609230005")
            r = RoundResult()
            first = collector._save(file, "店内", "170549072609230005", "2026-09-23", None, *_detail(), None, r)
            self.assertEqual((first["version"], first["changed"], r.changed), (1, False, 0))
            same = collector._save(file, "店内", "170549072609230005", "2026-09-23", None, *_detail(), first, r)
            self.assertEqual((same["version"], same["changed"], r.changed), (1, False, 0))
            changed = collector._save(file, "店内", "170549072609230005", "2026-09-23", None,
                                      *_detail(refund=True), same, r)
            self.assertEqual((changed["version"], changed["changed"], r.changed), (2, True, 1))
            self.assertEqual(changed["changes"][-1]["items"], ["新增退款 7.00（乌苏 红×1.0），原因：错点"])
            self.assertTrue((file.parent / "history" / f"{file.stem}_v1.json").exists())
            logs = list(cfg.out.glob("changes_*.csv"))
            self.assertEqual(len(logs), 1)
            with logs[0].open(encoding="utf-8-sig") as f:
                rows = list(csv.reader(f))
            self.assertEqual(rows[1][3:], ["170549072609230005", "2", "新增退款 7.00（乌苏 红×1.0），原因：错点"])


class CoverageTest(unittest.TestCase):
    def _collector(self, d, start_date=None):
        cfg = config_mod.Config(output_dir="out", start_date=start_date)
        cfg.config_dir = Path(d)
        return OrderCollector(SimpleNamespace(config=cfg, listeners=[]))

    def test_first_run_starts_today_no_backfill(self):
        with tempfile.TemporaryDirectory() as d:
            c = self._collector(d)
            c._load_coverage(date(2026, 10, 3))
            self.assertEqual(c.coverage["start_date"], "2026-10-03")
            self.assertEqual(c._pending_days(date(2026, 10, 3)), [])
            # 10/5 时 10/4 还算「昨天」，由定时检查负责，不进补抓
            c._mark_complete(date(2026, 10, 3))
            self.assertEqual(c._pending_days(date(2026, 10, 5)), [])
            # 到了 10/6，10/4 若还没被查完整（比如 10/5 一整天没开机），就要补
            self.assertEqual(c._pending_days(date(2026, 10, 6)), [date(2026, 10, 4)])

    def test_downtime_gap_is_backfilled_oldest_first(self):
        with tempfile.TemporaryDirectory() as d:
            c = self._collector(d)
            c._load_coverage(date(2026, 10, 3))
            c._mark_complete(date(2026, 10, 3))
            c._mark_complete(date(2026, 10, 4))
            # 10/5～10/8 停机，10/9 开机：昨天（10/8）由定时检查负责，补 10/5～10/7
            self.assertEqual(c._pending_days(date(2026, 10, 9)),
                             [date(2026, 10, 5), date(2026, 10, 6), date(2026, 10, 7)])
            # 进度存在文件里，重启后还在
            again = self._collector(d)
            again._load_coverage(date(2026, 10, 9))
            self.assertEqual(again._pending_days(date(2026, 10, 9))[0], date(2026, 10, 5))

    def test_configured_start_date_moves_earlier(self):
        with tempfile.TemporaryDirectory() as d:
            c = self._collector(d)
            c._load_coverage(date(2026, 10, 3))
            later = self._collector(d, start_date="2026-09-29")
            later._load_coverage(date(2026, 10, 3))
            self.assertEqual(later._pending_days(date(2026, 10, 3)),
                             [date(2026, 9, 29), date(2026, 9, 30), date(2026, 10, 1)])

    def test_bad_start_date_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(ValueError):
                self._collector(d, start_date="2026/09/29")._load_coverage(date(2026, 10, 3))


class ListRequestTest(unittest.TestCase):
    def _c(self, post):
        return Captured(datetime.now(), "POST", "https://pos.meituan.com/x", json.dumps(post), 200, {})

    def test_start_date_top_level_and_req_json(self):
        ms = day_start_ms(date(2026, 10, 2))
        self.assertEqual(request_start_ms(self._c({"startDate": ms})), ms)
        self.assertEqual(request_start_ms(self._c({"reqJson": json.dumps({"startDate": ms})})), ms)
        self.assertEqual(day_start_ms(date(2026, 10, 3)), 1790956800000)  # 与页面默认请求一致
        self.assertEqual(request_end_ms(self._c({"endDate": 1791043199999})), 1791043199999)
        self.assertEqual(request_end_ms(self._c({"reqJson": json.dumps({"endDate": 5})})), 5)

    def test_all_orders(self):
        self.assertTrue(all_orders_ok("店内", self._c({"pageNo": 1})))
        self.assertFalse(all_orders_ok("店内", self._c({"statusList": ["300"]})))
        self.assertTrue(all_orders_ok("外卖", self._c({"tabType": "1"})))
        self.assertFalse(all_orders_ok("外卖", self._c({"tabType": "2"})))

    def test_refund_row(self):
        refund_ob = {"orderNo": "88170549072610020001", "status": 30, "statusName": "退款完成", "amount": -4900}
        self.assertTrue(is_refund_row(refund_ob))
        self.assertFalse(is_refund_row({"status": 300, "amount": 2500}))
        refunds = [{"refundOrderBase": {"refundedAmount": 4900}, "refundItemList": [{"spuName": "玛格丽特", "count": 1.0}]}]
        row = summary_row("店内", refund_ob, None, refunds)
        self.assertEqual(row[6], "-49.00")
        self.assertEqual(row[12], "玛格丽特×1.0")
        self.assertEqual(row[15], "退款单（明细见原订单）")

    def test_kind_from_detail(self):
        self.assertEqual(kind_from_detail({"orderBase": {"typeName": "堂食"}}), "店内")
        self.assertEqual(kind_from_detail({"orderBase": {"typeName": "外卖", "sourceName": "美团外卖"}}), "外卖")
        self.assertEqual(kind_from_detail({"orderBase": {"typeName": "外卖", "sourceName": "小程序外卖"}}), "外卖自营")


class SanitizerTest(unittest.TestCase):
    s = Sanitizer()

    def test_recipient_fields_masked_whole(self):
        out = self.s.sanitize({"wm": {"recipientName": "张三", "recipientPhone": "13800001234",
                                      "recipientAddress": {"city": "张家口"}, "secretMobile": "x", "boxFee": 0}})
        self.assertEqual(out["wm"]["recipientName"], MASK)
        self.assertEqual(out["wm"]["recipientPhone"], MASK)
        self.assertEqual(out["wm"]["recipientAddress"], MASK)
        self.assertEqual(out["wm"]["secretMobile"], MASK)
        self.assertEqual(out["wm"]["boxFee"], 0)

    def test_dish_names_kept(self):
        item = {"name": "冰·拿铁", "spuName": "冰·拿铁", "skuName": "标准", "dishName": "x"}
        self.assertEqual(self.s.sanitize(item), item)

    def test_person_names_masked(self):
        out = self.s.sanitize({"userName": "李四", "customerName": "王五", "cashierName": "顾客自助"})
        self.assertEqual(out["userName"], MASK)
        self.assertEqual(out["customerName"], MASK)
        self.assertEqual(out["cashierName"], "顾客自助")

    def test_phone_in_free_text_masked(self):
        out = self.s.sanitize({"comment": "收餐人隐私号 17531103964_6603，手机号 137****3386"})
        self.assertNotIn("17531103964", out["comment"])
        self.assertIn("137****3386", out["comment"])  # 平台已打码的保持原样

    def test_identifier_that_looks_like_phone_kept(self):
        data = {"skuId": 13926234565, "spuId": "13926234564", "orderNo": "15012345678", "count": 3}
        self.assertEqual(self.s.sanitize(data), data)

    def test_order_numbers_not_masked(self):
        self.assertEqual(self.s.sanitize_text("orderNo=170549072610030014"), "orderNo=170549072610030014")


class SummaryTest(unittest.TestCase):
    ob = {"businessTime": "2026/10/03", "sourceName": "美团外卖", "orderNo": "302327462057407338",
          "orderTime": 1790982488000, "statusName": "已完成", "amount": 2900, "payed": 2900, "income": 1828,
          "status": 1400, "refundStatus": None, "partRefundCnt": None}

    def test_row_with_items_and_refund(self):
        detail = {"itemList": [{"name": "澳白·热", "specs": "280毫升", "count": 1.0},
                               {"name": "乌苏 红", "specs": "", "count": 2.0}]}
        refunds = [{"refundOrderBase": {"refundedAmount": 700},
                    "refundItemList": [{"spuName": "乌苏 红", "count": 1.0}]}]
        saved = {"version": 2, "changes": [{"at": "2026-10-03 20:00:00", "items": ["新增退款 7.00（乌苏 红×1.0）"]}]}
        row = summary_row("外卖", self.ob, detail, refunds, saved)
        self.assertEqual(len(row), len(SUMMARY_HEADER))
        self.assertEqual(row[4], "2026-10-03 07:08:08")  # 北京时间
        self.assertEqual(row[6:9], ["29.00", "29.00", "18.28"])
        self.assertEqual(row[9], "2")
        self.assertEqual(row[10], "澳白·热(280毫升)×1.0；乌苏 红×2.0")
        self.assertEqual(row[11:13], ["7.00", "乌苏 红×1.0"])
        self.assertEqual(row[13:15], ["2", "2026-10-03 20:00:00 新增退款 7.00（乌苏 红×1.0）"])
        self.assertEqual(row[15], "")

    def test_row_without_detail(self):
        row = summary_row("店内", self.ob, None, [])
        self.assertEqual(row[9], "0")
        self.assertEqual(row[11], "")
        self.assertEqual(row[15], "详情未取到")

    def test_fingerprint_changes_with_refund(self):
        changed = dict(self.ob, refundStatus=2)
        self.assertNotEqual(fingerprint(self.ob), fingerprint(changed))


class DishQueryTest(unittest.TestCase):
    def _c(self, post):
        return Captured(datetime.now(), "POST", "https://pos.meituan.com/web/api/v1/admin/poi/goods/filter-es",
                        post, 200, {})

    def test_combo_vs_main(self):
        combo = self._c(json.dumps({"pageNo": 1, "spuType": 20}, ensure_ascii=False))
        main = self._c(json.dumps({"pageNo": 1, "pageSize": 20}, ensure_ascii=False))
        self.assertTrue(is_combo_query(combo) and not is_main_query(combo))
        self.assertTrue(is_main_query(main) and not is_combo_query(main))


class ConfigTest(unittest.TestCase):
    def test_comments_and_paths(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "collector.json"
            p.write_text('// 说明\n{\n  // 注释\n  "phone": "1",\n  "output_dir": "data"\n}\n', encoding="utf-8")
            cfg = config_mod.load(str(p))
            self.assertEqual(cfg.phone, "1")
            self.assertEqual(cfg.out, (Path(d) / "data").resolve())

    def test_unknown_key_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "collector.json"
            p.write_text('{"phnoe": "1"}', encoding="utf-8")
            with self.assertRaises(ValueError):
                config_mod.load(str(p))

    def test_active_hours(self):
        cfg = config_mod.Config(active_hours="06:00-24:00")
        self.assertTrue(cfg.is_active_now(datetime(2026, 10, 3, 6, 0)))
        self.assertTrue(cfg.is_active_now(datetime(2026, 10, 3, 23, 59)))
        self.assertFalse(cfg.is_active_now(datetime(2026, 10, 3, 3, 0)))
        night = config_mod.Config(active_hours="22:00-02:00")
        self.assertTrue(night.is_active_now(datetime(2026, 10, 3, 1, 0)))
        self.assertFalse(night.is_active_now(datetime(2026, 10, 3, 12, 0)))


if __name__ == "__main__":
    unittest.main()
