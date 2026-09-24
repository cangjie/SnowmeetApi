"""Run food-inventory integration tests against a disposable SQL Server database.

Requires pyodbc, ODBC Driver 13 for SQL Server, and SnowmeetApi/config.sqlServer.
Only schema is read from snowmeet_new; test rows are written to a new database.
The disposable database is dropped even when a test fails.
"""

from __future__ import annotations

import os
from pathlib import Path
import subprocess
import sys
from uuid import uuid4

import pyodbc


API_DIR = Path(__file__).resolve().parents[1]
ROOT = API_DIR.parent
parts = {
    key.strip().lower(): value
    for key, value in (
        entry.split("=", 1)
        for entry in (API_DIR / "config.sqlServer").read_text().strip().split(";")
        if "=" in entry
    )
}
SOURCE_DATABASE = parts["database"]
if SOURCE_DATABASE != "snowmeet_new":
    raise SystemExit("此运行器只接受 snowmeet_new 作为只读结构来源")
TEST_DATABASE = "snowmeet_fnb_test_" + uuid4().hex[:12]


def connect(database: str) -> pyodbc.Connection:
    connection_string = (
        "DRIVER={{ODBC Driver 13 for SQL Server}};SERVER={server};DATABASE={};"
        "UID={uid};PWD={pwd};Encrypt=no;TrustServerCertificate=yes;"
    ).format(database, **parts)
    return pyodbc.connect(connection_string, timeout=20, autocommit=True)


def bootstrap() -> None:
    with connect(TEST_DATABASE) as target:
        cursor = target.cursor()
        old_tables = (
            "shop_list", "staff", "mini_upload", "category", "product", "order", "fd_order",
            "fnb_material_batch", "fnb_material_alert_log",
        )
        for table in old_tables:
            cursor.execute(
                f"SELECT TOP (0) * INTO [dbo].[{table}] "
                f"FROM [{SOURCE_DATABASE}].[dbo].[{table}]"
            )
            cursor.execute(
                f"ALTER TABLE [dbo].[{table}] ADD CONSTRAINT "
                f"[PK_test_{table}] PRIMARY KEY ([id])"
            )

        with connect(SOURCE_DATABASE) as source:
            defaults = source.cursor().execute(
                """SELECT t.name, c.name, d.definition
                FROM sys.default_constraints AS d
                JOIN sys.tables AS t ON t.object_id = d.parent_object_id
                JOIN sys.columns AS c ON c.object_id = t.object_id
                                  AND c.column_id = d.parent_column_id
                WHERE t.name IN ('shop_list','staff','mini_upload','category','product','order',
                                 'fd_order','fnb_material_batch','fnb_material_alert_log')"""
            ).fetchall()
        for table, column, definition in defaults:
            cursor.execute(
                f"ALTER TABLE [dbo].[{table}] ADD CONSTRAINT [DF_test_{table}_{column}] "
                f"DEFAULT {definition} FOR [{column}]"
            )

        for script in ("2026-09-22_fnb_inventory_other_tables.sql", "2026-09-24_fnb_category_name_unique_valid.sql",
                       "2026-09-24_fnb_item_expiry_settings.sql"):
            cursor.execute((ROOT / "snowmeet_ai_doc/sql" / script).read_text(encoding="utf-8"))
            while cursor.nextset():
                pass
        actual_tables = cursor.execute(
            "SELECT COUNT(*) FROM sys.tables WHERE name LIKE 'fnb_%'"
        ).fetchone()[0]
        actual_views = cursor.execute(
            "SELECT COUNT(*) FROM sys.views WHERE name LIKE 'vw_fnb_%'"
        ).fetchone()[0]
        if (actual_tables, actual_views) != (19, 2):
            raise RuntimeError(f"测试库结构不完整：{actual_tables} 张表、{actual_views} 个视图")


def drop_database() -> None:
    with connect("master") as master:
        master.execute(
            f"ALTER DATABASE [{TEST_DATABASE}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE"
        )
        master.execute(f"DROP DATABASE [{TEST_DATABASE}]")


def main() -> int:
    with connect("master") as master:
        master.execute(f"CREATE DATABASE [{TEST_DATABASE}] COLLATE Chinese_PRC_CI_AS")
    try:
        bootstrap()
        env = os.environ.copy()
        env["SNOWMEET_FNB_TEST_SQLSERVER"] = (
            "Server={server};Database={};User Id={uid};Password={pwd};"
            "Encrypt=False;TrustServerCertificate=True;Connect Timeout=20;"
        ).format(TEST_DATABASE, **parts)
        print(f"SQL Server 隔离测试库已准备：{TEST_DATABASE}", flush=True)
        test_filter = "FullyQualifiedName~FnbSqlServerIntegrationTests"
        if len(sys.argv) == 2:
            test_filter += "&FullyQualifiedName~" + sys.argv[1]
        result = subprocess.run(
            ["dotnet", "test", "SnowmeetApi.sln", "--no-restore",
             "--filter", test_filter,
             "--verbosity", "quiet"],
            cwd=API_DIR,
            env=env,
            check=False,
        )
        return result.returncode
    finally:
        drop_database()
        print(f"已删除隔离测试库：{TEST_DATABASE}", flush=True)


if __name__ == "__main__":
    sys.exit(main())
