"""真实环境的封装：pymssql、aws 命令行、gzip、告警。"""
from __future__ import annotations

import atexit
import datetime as dt
import hashlib
import json
import logging
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import urllib.request
from pathlib import Path
from typing import Callable, Optional

from . import core
from .jobs import BackupError, Env

log = logging.getLogger("snowmeet_backup")

DEFAULT_SQL_CONFIG = "/home/ubuntu/webs/SnowmeetApi/config.sqlServer"
DEFAULT_PYDEPS = "/home/ubuntu/fnb-v4-deploy-20261006/deps"      # 服务器上现成的 pymssql；有条件请 pip 装到系统里
DEFAULT_STAGING = "/var/opt/mssql/backup"                        # mssql 用户可写
DEFAULT_STATE_DIR = "/var/lib/snowmeet-backup"
ALERT_HOOK = "/etc/snowmeet-backup/alert.sh"
# 企业微信提醒：调本机后端的 BackupAlert/Notify，令牌和后端读的是同一个文件 config.backupAlertToken
DEFAULT_ALERT_URL = "http://127.0.0.1:5000/api/BackupAlert/Notify"
DEFAULT_ALERT_TOKEN_FILE = "/home/ubuntu/webs/SnowmeetApi/config.backupAlertToken"


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

    def sha256(self, key: str) -> str:
        """从 S3 流式读回，不在服务器落第二份备份文件。"""
        args = [self.aws, "s3", "cp", "s3://%s/%s" % (self.bucket, key), "-", "--region", self.region, "--only-show-errors"]
        env = dict(os.environ, AWS_CONFIG_FILE=self._cfg)
        digest = hashlib.sha256()
        errors = []
        with tempfile.TemporaryFile() as stderr:
            proc = subprocess.Popen(args, env=env, stdout=subprocess.PIPE, stderr=stderr)
            def read():
                try:
                    for chunk in iter(lambda: proc.stdout.read(1 << 20), b""):
                        digest.update(chunk)
                except Exception as exc:
                    errors.append(exc)
                    proc.kill()
                finally:
                    proc.stdout.close()
            thread = threading.Thread(target=read, daemon=True)
            thread.start()
            try:
                code = proc.wait(timeout=7200)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait()
                thread.join()
                raise BackupError("从 S3 读回备份超时")
            thread.join()
            if errors:
                raise BackupError("读取 S3 备份失败：%s" % errors[0])
            if code:
                stderr.seek(0)
                raise BackupError("从 S3 读回备份失败：%s" % stderr.read(300).decode("utf-8", errors="replace").strip())
        return digest.hexdigest()


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


def post_alert(url: str, token: str, level: str, msg: str, opener=None, timeout: int = 15) -> bool:
    """把提醒发给后端 BackupAlert/Notify（由服务器发企业微信）。成功返回 True；失败只记日志，不抛异常。"""
    opener = opener or urllib.request.urlopen
    body = json.dumps({"level": level, "message": msg}, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(url, data=body, method="POST", headers={
        "Content-Type": "application/json; charset=utf-8", "X-Backup-Token": token})
    try:
        with opener(req, timeout=timeout) as r:
            data = json.loads(r.read().decode("utf-8") or "{}")
        if data.get("code", 0) != 0:
            log.warning("企业微信提醒没有发出：%s", data.get("message"))
            return False
        return True
    except Exception as e:
        log.warning("企业微信提醒发送失败：%s", e)
        return False


def _default_poster(level: str, msg: str) -> bool:
    token_file = os.environ.get("SNOWMEET_BACKUP_ALERT_TOKEN_FILE", DEFAULT_ALERT_TOKEN_FILE)
    try:
        token = Path(token_file).read_text(encoding="utf-8").strip()
    except OSError:
        return False            # 没配令牌就不发（后端也会拒绝）
    if not token:
        return False
    return post_alert(os.environ.get("SNOWMEET_BACKUP_ALERT_URL", DEFAULT_ALERT_URL), token, level, msg)


def make_notifier(hook: str = ALERT_HOOK, poster: Optional[Callable[[str, str], bool]] = None):
    """日志 + syslog + 企业微信（经后端）；如果存在可执行的 /etc/snowmeet-backup/alert.sh，还会把 (级别, 内容) 传给它。"""
    poster = poster or _default_poster

    def notify(level: str, msg: str) -> None:
        {"error": log.error, "warning": log.warning}.get(level, log.info)(msg)
        prio = {"error": "user.err", "warning": "user.warning"}.get(level, "user.info")
        try:
            subprocess.run(["logger", "-t", "snowmeet-backup", "-p", prio, msg], timeout=10)
        except Exception:
            pass
        if level in ("error", "warning"):
            try:
                poster(level, msg)
            except Exception as e:
                log.warning("企业微信提醒出错：%s", e)
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
