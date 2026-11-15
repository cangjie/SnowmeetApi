"""真实环境的封装：pymssql、aws 命令行、gzip、告警。"""
from __future__ import annotations

import atexit
import datetime as dt
import logging
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Optional

from . import core
from .jobs import BackupError, Env

log = logging.getLogger("snowmeet_backup")

DEFAULT_SQL_CONFIG = "/home/ubuntu/webs/SnowmeetApi/config.sqlServer"
DEFAULT_PYDEPS = "/home/ubuntu/fnb-v4-deploy-20261006/deps"      # 服务器上现成的 pymssql；有条件请 pip 装到系统里
DEFAULT_STAGING = "/var/opt/mssql/backup"                        # mssql 用户可写
DEFAULT_STATE_DIR = "/var/lib/snowmeet-backup"
ALERT_HOOK = "/etc/snowmeet-backup/alert.sh"


def _import_pymssql():
    try:
        import pymssql  # type: ignore
        return pymssql
    except ImportError:
        sys.path.insert(0, os.environ.get("SNOWMEET_BACKUP_PYDEPS", DEFAULT_PYDEPS))
        import pymssql  # type: ignore
        return pymssql


class Sql:
    def __init__(self, config_path: str, host: str = "127.0.0.1", port: int = 1433):
        user, pw = core.parse_sql_config(Path(config_path).read_text(encoding="utf-8-sig"))
        self._user, self._pw = user, pw
        self._host, self._port = host, port
        self._pymssql = _import_pymssql()

    def _connect(self):
        return self._pymssql.connect(server=self._host, port=self._port, user=self._user, password=self._pw,
                                     database="master", autocommit=True, login_timeout=15, timeout=7200)

    def execute(self, sql: str, params=None) -> None:
        conn = self._connect()
        try:
            cur = conn.cursor()
            cur.execute(sql, params) if params else cur.execute(sql)
        finally:
            conn.close()

    def query(self, sql: str, params=None):
        conn = self._connect()
        try:
            cur = conn.cursor()
            cur.execute(sql, params) if params else cur.execute(sql)
            return cur.fetchall()
        finally:
            conn.close()


class S3:
    def __init__(self, bucket: str, region: str = "cn-northwest-1", aws: str = "aws"):
        self.bucket, self.region, self.aws = bucket, region, aws
        fd, self._cfg = tempfile.mkstemp(suffix=".awscfg")
        with os.fdopen(fd, "w") as f:   # 降低并发和对线上的影响
            f.write("[default]\nregion = %s\ns3 =\n  max_concurrent_requests = 3\n"
                    "  multipart_threshold = 64MB\n  multipart_chunksize = 64MB\n" % region)
        atexit.register(lambda: os.path.exists(self._cfg) and os.unlink(self._cfg))

    def _run(self, args, timeout: int):
        env = dict(os.environ, AWS_CONFIG_FILE=self._cfg)
        r = subprocess.run([self.aws] + args, env=env, capture_output=True, text=True, timeout=timeout)
        if r.returncode != 0:
            raise BackupError("aws %s 失败：%s" % (" ".join(args[:2]), (r.stderr or r.stdout).strip()[:300]))
        return r.stdout

    def upload(self, local: Path, key: str) -> None:
        self._run(["s3", "cp", str(local), "s3://%s/%s" % (self.bucket, key), "--region", self.region,
                   "--sse", "AES256", "--no-progress", "--only-show-errors"], 7200)

    def head_size(self, key: str) -> int:
        out = self._run(["s3api", "head-object", "--bucket", self.bucket, "--key", key, "--region", self.region,
                         "--query", "ContentLength", "--output", "text"], 120)
        return int(out.strip())


def gzip_file(src: Path, dst: Path) -> None:
    with open(str(dst), "wb") as out:
        r = subprocess.run(["gzip", "-6", "-c", str(src)], stdout=out)
    if r.returncode != 0:
        try:
            Path(dst).unlink()
        except FileNotFoundError:
            pass
        raise BackupError("gzip 失败（返回码 %d）" % r.returncode)


def free_gb(path: Path) -> float:
    p = Path(path)
    while not p.exists() and p != p.parent:
        p = p.parent
    return shutil.disk_usage(str(p)).free / 1e9


def make_notifier(hook: str = ALERT_HOOK):
    """日志 + syslog；如果存在可执行的 /etc/snowmeet-backup/alert.sh，就把 (级别, 内容) 传给它（用来接企业微信等）。"""

    def notify(level: str, msg: str) -> None:
        {"error": log.error, "warning": log.warning}.get(level, log.info)(msg)
        prio = {"error": "user.err", "warning": "user.warning"}.get(level, "user.info")
        try:
            subprocess.run(["logger", "-t", "snowmeet-backup", "-p", prio, msg], timeout=10)
        except Exception:
            pass
        if level in ("error", "warning") and os.access(hook, os.X_OK):
            try:
                subprocess.run([hook, level, msg], timeout=30)
            except Exception as e:
                log.warning("告警脚本执行失败：%s", e)

    return notify


def build_env() -> Env:
    bucket = os.environ.get("SNOWMEET_BACKUP_BUCKET")
    if not bucket:
        raise BackupError("没有设置环境变量 SNOWMEET_BACKUP_BUCKET（S3 桶名）")
    state_dir = Path(os.environ.get("SNOWMEET_BACKUP_STATE_DIR", DEFAULT_STATE_DIR))
    return Env(
        sql=Sql(os.environ.get("SNOWMEET_SQL_CONFIG", DEFAULT_SQL_CONFIG)),
        s3=S3(bucket),
        notify=make_notifier(),
        now=dt.datetime.now,
        staging=Path(os.environ.get("SNOWMEET_BACKUP_STAGING", DEFAULT_STAGING)),
        state_path=state_dir / "state.json",
        lock_path=state_dir / "lock",
        gzip_fn=gzip_file,
        free_gb=free_gb,
    )
