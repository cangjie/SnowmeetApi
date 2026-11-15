from __future__ import annotations

import argparse
import datetime as dt
import logging
import sys

from . import core, jobs

log = logging.getLogger("snowmeet_backup")


def _plan() -> str:
    day = dt.date.today()
    full_t = dt.datetime.combine(day, core.FULL_START) + dt.timedelta(minutes=10)
    lines = [
        "备份计划（%s）" % core.PRIMARY_DB,
        "  完整备份：每天 %s，S3 保留 %d 天 → %s/%s/full/" % (core.FULL_START.strftime("%H:%M"), core.FULL_DAYS,
                                                       core.S3_PREFIX, core.PRIMARY_DB),
        "  差异备份：%s" % "、".join(t.strftime("%H:%M") for t in sorted(core.DIFF_TIMES)),
        "            共 %d 次/天，S3 保留 %d 天 → %s/%s/diff/" % (len(core.DIFF_TIMES), core.DIFF_DAYS,
                                                           core.S3_PREFIX, core.PRIMARY_DB),
        "",
        "过期对齐检查（S3 按 UTC 零点取整）：",
        "  02:40 上传的完整备份过期于 %s UTC" % core.expiry_utc(full_t, core.FULL_DAYS),
    ]
    for t in (dt.time(9, 1), dt.time(17, 1)):
        d = dt.datetime.combine(day, t)
        lines.append("  %s 上传的差异备份过期于 %s UTC" % (t.strftime("%H:%M"), core.expiry_utc(d, core.DIFF_DAYS)))
    d = dt.datetime.combine(day + dt.timedelta(days=1), dt.time(0, 11))
    lines.append("  次日 00:11 上传的差异备份过期于 %s UTC" % core.expiry_utc(d, core.DIFF_DAYS))
    return "\n".join(lines)


def main(argv=None) -> int:
    p = argparse.ArgumentParser(prog="snowmeet_backup", description="snowmeet 数据库备份")
    sub = p.add_subparsers(dest="cmd", required=True)
    f = sub.add_parser("full", help="完整备份并上传 S3")
    f.add_argument("--db", default=core.PRIMARY_DB)
    f.add_argument("--copy-only", action="store_true",
                   help="手动备份用：不影响差异备份的基准；上传到 adhoc/ 目录，没有自动过期")
    f.add_argument("--no-keep-local", action="store_true", help="上传后不在本机留备份文件（非主库默认如此）")
    d = sub.add_parser("diff", help="差异备份并上传 S3")
    d.add_argument("--db", default=core.PRIMARY_DB)
    c = sub.add_parser("check", help="健康检查，有问题就告警")
    c.add_argument("--db", default=core.PRIMARY_DB)
    dr = sub.add_parser("drill", help="还原演练：还原到临时库 <db>_rt，检查完整性，再删掉")
    dr.add_argument("--db", default=core.PRIMARY_DB)
    dr.add_argument("--yes", action="store_true", help="确认要在生产 SQL Server 上做演练")
    sub.add_parser("plan", help="打印备份计划和 S3 过期对齐检查（不连接任何东西）")
    args = p.parse_args(argv)

    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    if args.cmd == "plan":
        print(_plan())
        return 0
    if args.cmd == "drill" and not args.yes:
        print("演练会在生产 SQL Server 上还原一个临时库，需要加 --yes 确认", file=sys.stderr)
        return 2

    from . import real
    try:
        env = real.build_env()
        if args.cmd == "full":
            keep_local = (args.db == core.PRIMARY_DB) and not args.no_keep_local
            jobs.run_full(env, args.db, copy_only=args.copy_only, keep_local=keep_local)
        elif args.cmd == "diff":
            jobs.run_diff(env, args.db)
        elif args.cmd == "check":
            problems = jobs.run_check(env, args.db)
            for _, msg in problems:
                print("问题：" + msg)
            if not problems:
                print("OK")
            return 2 if problems else 0
        elif args.cmd == "drill":
            jobs.run_drill(env, args.db)
        return 0
    except jobs.Busy:
        log.warning("另一个备份任务正在运行，本次 %s 跳过", args.cmd)
        return 0
    except Exception as e:
        log.error("%s 失败：%s", args.cmd, e)
        return 1
