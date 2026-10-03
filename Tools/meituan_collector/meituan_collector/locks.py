"""常驻抓取、单次抓取（once）、手动操作（cmd）共用一个浏览器，用两个锁文件避免互相干扰。

- round.lock：正在抓一轮。once 和常驻的一轮互斥；cmd 等它结束再操作。
- cmd.lock：最近有人用 cmd 操作浏览器。常驻在 CMD_QUIET_SECONDS 内不开始新的一轮。
锁文件超过 STALE_SECONDS 视为残留（进程崩溃没删掉），直接忽略。
"""

from __future__ import annotations

import asyncio
import os
import time
from contextlib import asynccontextmanager
from pathlib import Path

from .log import log

STALE_SECONDS = 30 * 60
CMD_QUIET_SECONDS = 3 * 60


def _age(path: Path) -> float | None:
    try:
        return time.time() - path.stat().st_mtime
    except FileNotFoundError:
        return None


def round_lock(out: Path) -> Path:
    return out / "round.lock"


def cmd_lock(out: Path) -> Path:
    return out / "cmd.lock"


def round_running(out: Path) -> bool:
    age = _age(round_lock(out))
    return age is not None and age < STALE_SECONDS


def cmd_recent(out: Path) -> bool:
    age = _age(cmd_lock(out))
    return age is not None and age < CMD_QUIET_SECONDS


def touch_cmd(out: Path) -> None:
    out.mkdir(parents=True, exist_ok=True)
    cmd_lock(out).write_text(str(os.getpid()), encoding="utf-8")


async def wait_round_finished(out: Path, timeout: float = 15 * 60) -> bool:
    waited = 0.0
    if round_running(out):
        print("常驻程序正在抓取，等这一轮结束后再操作……", flush=True)
    while round_running(out):
        if waited >= timeout:
            return False
        await asyncio.sleep(2)
        waited += 2
    return True


@asynccontextmanager
async def holding_round(out: Path):
    """抓一轮期间持有 round.lock；别人正在抓就先等。"""
    lock = round_lock(out)
    while True:
        try:
            fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, str(os.getpid()).encode())
            os.close(fd)
            break
        except FileExistsError:
            if not round_running(out):  # 残留的旧锁
                try:
                    lock.unlink()
                except FileNotFoundError:
                    pass
                continue
            log.info("另一个进程正在抓取，等它结束……")
            await asyncio.sleep(5)
    try:
        yield
    finally:
        try:
            lock.unlink()
        except FileNotFoundError:
            pass
