"""去掉与菜品无关的顾客个人信息：收货人姓名、电话、地址、经纬度。

订单和菜品的其它字段一律原样保留（第一期要求「能抓尽抓」）。
"""

from __future__ import annotations

import re
from typing import Any, Iterable

MASK = "***"

# 字段名拆成单词后，含其中任意一个就打码。
_SENSITIVE = {
    "phone", "mobile", "tel", "telephone", "address", "addr",
    "recipient", "receiver", "consignee", "latitude", "longitude", "lat", "lng",
}
# 「名字」类字段只在同时出现这些词时打码，避免误伤 dishName、skuName 等菜品字段。
_PERSON = {"user", "customer", "nick", "buyer", "member", "guest"}

# 完整的大陆手机号（已被平台打码的 138****1234 不会命中）。
_MOBILE_VALUE = re.compile(r"^\+?(86)?1[3-9]\d{9}$")
_MOBILE_IN_TEXT = re.compile(r"(?<!\d)1[3-9]\d{9}(?!\d)")
_WORD_SPLIT = re.compile(r"(?<=[a-z0-9])(?=[A-Z])|[_\-\s.]+")


class Sanitizer:
    def __init__(self, extra_keys: Iterable[str] = ()):
        self._extra = {k.lower() for k in extra_keys}

    def is_sensitive_key(self, key: str) -> bool:
        if key.lower() in self._extra:
            return True
        tokens = [t.lower() for t in _WORD_SPLIT.split(key) if t]
        if any(t in _SENSITIVE for t in tokens):
            return True
        return "name" in tokens and any(t in _PERSON for t in tokens)

    @staticmethod
    def is_identifier_key(key: str) -> bool:
        # skuId、orderNo 之类的编号可能恰好是 11 位，不能当手机号打码。
        return key.endswith(("Id", "No", "Code", "_id")) or key in ("id", "no", "code")

    def sanitize(self, node: Any) -> Any:
        if isinstance(node, dict):
            out = {}
            for key, value in node.items():
                if self.is_sensitive_key(key) and value is not None:
                    out[key] = MASK  # 整个值（含嵌套的地址对象）一起打码
                elif not isinstance(value, (dict, list)) and self.is_identifier_key(key):
                    out[key] = value
                else:
                    out[key] = self.sanitize(value)
            return out
        if isinstance(node, list):
            return [self.sanitize(x) for x in node]
        if isinstance(node, str):
            # 备注等自由文本里也会出现手机号或隐私号（例如「收餐人隐私号 175xxxxxxxx_6603」）。
            return MASK if _MOBILE_VALUE.match(node) else _MOBILE_IN_TEXT.sub(MASK, node)
        return node

    def sanitize_text(self, text: str) -> str:
        """非 JSON 文本（URL、表单提交内容、页面文字）只做手机号打码。"""
        return _MOBILE_IN_TEXT.sub(MASK, text)
