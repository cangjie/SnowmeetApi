"""订单内容变化检测：把一张单规范化成「影响业务的字段」，比较前后两次抓取，生成可读的变化说明。

只比较订单状态、金额、菜品、支付、优惠、退款等业务内容；抓取时间、操作日志之类每次都可能不同的字段不算变化。
"""

from __future__ import annotations

import hashlib
import json
from typing import Any

_ORDER_FIELDS = {
    "status": "状态码", "statusName": "状态", "amount": "订单金额", "payed": "实付", "income": "收入",
    "discount": "优惠", "refundStatus": "退款状态", "isPartRefund": "部分退款", "partRefundCnt": "部分退款次数",
    "comment": "整单备注", "customerCount": "人数", "tableName": "桌台",
}
_MONEY = {"amount", "payed", "income", "discount"}
_ITEM_FIELDS = ("name", "specs", "count", "price", "actualPrice", "totalPrice", "retreat", "present",
                "status", "attrs", "comment")
_PAY_FIELDS = ("payTypeName", "payed", "status", "type")


def _yuan(v: Any) -> str:
    return f"{v / 100:.2f}" if isinstance(v, (int, float)) else str(v)


def normalize(detail: dict | None, refunds: list[dict], wm_list: dict | None = None) -> dict:
    detail = detail or {}
    ob = detail.get("orderBase") or {}
    items = {}
    for i, it in enumerate(detail.get("itemList") or []):
        key = str(it.get("itemNo") or f"#{i}")
        items[key] = {k: it.get(k) for k in _ITEM_FIELDS}
    pays = {}
    for i, p in enumerate(detail.get("payList") or []):
        key = str(p.get("payNo") or f"#{i}")
        pays[key] = {k: p.get(k) for k in _PAY_FIELDS}
    refund_map = {}
    for r in refunds:
        base = r.get("refundOrderBase") or {}
        rid = str(base.get("refundId") or base.get("refundNo") or len(refund_map))
        refund_map[rid] = {
            "amount": base.get("refundedAmount"),
            "status": base.get("refundStatus"),
            "reason": base.get("refundReason"),
            "items": sorted(f"{i.get('spuName') or i.get('name')}×{i.get('count')}"
                            for i in r.get("refundItemList") or []),
        }
    wm = detail.get("wm") or wm_list or {}
    return {
        "order": {k: ob.get(k) for k in _ORDER_FIELDS},
        "items": items,
        "pays": pays,
        "discounts": detail.get("discountList") or [],
        "refunds": refund_map,
        "wm": {k: wm.get(k) for k in ("finalSettlement", "partRefundPrice", "settlement")} if wm else {},
    }


def digest(norm: dict) -> str:
    return hashlib.sha1(json.dumps(norm, ensure_ascii=False, sort_keys=True).encode("utf-8")).hexdigest()


def describe(old: dict, new: dict) -> list[str]:
    """把两次规范化结果的差异写成人能看懂的几句话。"""
    out: list[str] = []
    for k, label in _ORDER_FIELDS.items():
        a, b = old["order"].get(k), new["order"].get(k)
        if a != b and k != "status":  # 状态码和状态名一起变，只报状态名
            out.append(f"{label} {_yuan(a) if k in _MONEY else a}→{_yuan(b) if k in _MONEY else b}")

    oi, ni = old["items"], new["items"]
    for key in ni.keys() - oi.keys():
        it = ni[key]
        out.append(f"加菜 {it['name']}×{it['count']}")
    for key in oi.keys() - ni.keys():
        it = oi[key]
        out.append(f"菜品删除 {it['name']}×{it['count']}")
    for key in oi.keys() & ni.keys():
        a, b = oi[key], ni[key]
        if a == b:
            continue
        name = b["name"] or a["name"]
        if a["count"] != b["count"]:
            out.append(f"{name} 数量 {a['count']}→{b['count']}")
        if a["retreat"] != b["retreat"] and b["retreat"]:
            out.append(f"退菜 {name}")
        if a["present"] != b["present"] and b["present"]:
            out.append(f"赠送 {name}")
        if a["totalPrice"] != b["totalPrice"]:
            out.append(f"{name} 小计 {_yuan(a['totalPrice'])}→{_yuan(b['totalPrice'])}")
        rest = [f for f in _ITEM_FIELDS if f not in ("count", "retreat", "present", "totalPrice") and a[f] != b[f]]
        if rest:
            out.append(f"{name} 其它变化（{'、'.join(rest)}）")

    for rid in new["refunds"].keys() - old["refunds"].keys():
        r = new["refunds"][rid]
        items = "、".join(r["items"])
        out.append(f"新增退款 {_yuan(r['amount'])}" + (f"（{items}）" if items else "") +
                   (f"，原因：{r['reason']}" if r["reason"] else ""))
    for rid in old["refunds"].keys() & new["refunds"].keys():
        if old["refunds"][rid] != new["refunds"][rid]:
            out.append(f"退款单 {rid} 有变化")

    if old["pays"] != new["pays"]:
        out.append("支付记录变化")
    if old["discounts"] != new["discounts"]:
        out.append("优惠变化")
    if old["wm"] != new["wm"]:
        out.append("外卖结算变化")
    return out or ["其它字段变化"]
