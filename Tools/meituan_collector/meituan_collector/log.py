"""控制台 + 按天的日志文件。不要往这里写 Cookie、验证码或顾客信息。"""

from __future__ import annotations

import logging
import sys
from datetime import datetime
from pathlib import Path

log = logging.getLogger("meituan_collector")


class _DailyFileHandler(logging.Handler):
    def __init__(self, directory: Path):
        super().__init__()
        self.directory = directory
        directory.mkdir(parents=True, exist_ok=True)

    def emit(self, record: logging.LogRecord) -> None:
        try:
            path = self.directory / f"collector_{datetime.now():%Y%m%d}.log"
            with path.open("a", encoding="utf-8") as f:
                f.write(self.format(record) + "\n")
        except Exception:
            self.handleError(record)


def init(output_dir: Path) -> None:
    if log.handlers:
        return
    fmt = logging.Formatter("%(asctime)s [%(levelname)s] %(message)s", "%Y-%m-%d %H:%M:%S")
    if sys.stdout is not None:  # 用 pythonw 无窗口运行时没有控制台，只写文件
        console = logging.StreamHandler(sys.stdout)
        console.setFormatter(fmt)
        log.addHandler(console)
    file = _DailyFileHandler(output_dir / "logs")
    file.setFormatter(fmt)
    log.addHandler(file)
    log.setLevel(logging.INFO)
    log.propagate = False
