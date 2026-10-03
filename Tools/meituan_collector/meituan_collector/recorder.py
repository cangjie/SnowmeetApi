"""录制：把页面发出的所有接口返回（已打码）逐个存成文件。用于勘察接口，也用于把现场数据带回来分析。"""

from __future__ import annotations

import re
from datetime import datetime
from pathlib import Path
from urllib.parse import urlsplit

from .browser import Captured
from .sanitizer import Sanitizer
from .storage import read_json, write_json

_UNSAFE = re.compile(r"[^A-Za-z0-9_\-]+")


class Recorder:
    def __init__(self, output_dir: Path):
        self.directory = output_dir / "record" / datetime.now().strftime("%Y%m%d_%H%M%S")
        self.directory.mkdir(parents=True, exist_ok=True)
        self.index = self.directory / "index.tsv"
        self.index.write_text("seq\ttime\tmethod\tstatus\tfile\turl\n", encoding="utf-8")
        self.count = 0

    def save(self, c: Captured) -> None:
        self.count += 1
        name = _UNSAFE.sub("_", urlsplit(c.url).path.replace("/web/api/", "")).strip("_")[:80]
        file = f"{self.count:04d}_{name}.json"
        write_json(self.directory / file, {
            "capturedAt": c.at.strftime("%Y-%m-%d %H:%M:%S.%f")[:-3],
            "method": c.method, "url": c.url, "postData": c.post_data, "status": c.status, "body": c.body,
        })
        with self.index.open("a", encoding="utf-8") as f:
            f.write(f"{self.count}\t{c.at:%H:%M:%S}\t{c.method}\t{c.status}\t{file}\t{c.url}\n")


def resanitize(directory: Path, sanitizer: Sanitizer) -> int:
    """用当前打码规则重新处理一个录制目录或订单目录（打码规则更新后补救旧文件）。返回改动的文件数。"""
    changed = 0
    for file in directory.rglob("*.json"):
        doc = read_json(file)
        new = sanitizer.sanitize(doc)
        if new != doc:
            write_json(file, new)
            changed += 1
    return changed
