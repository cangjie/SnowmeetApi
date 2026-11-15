import datetime as dt
import re
import subprocess
import tempfile
import unittest
from pathlib import Path

from snowmeet_backup import core, jobs


class FakeSql:
    def __init__(self):
        self.executed = []
        self.set_id = 100
        self.size_mb = 100
        self.rt_exists = False
        self.fail_on = None

    def execute(self, sql, params=None):
        self.executed.append(sql)
        if self.fail_on and self.fail_on in sql:
            raise RuntimeError("sql boom")
        m = re.match(r"BACKUP DATABASE \[(\w+)\] TO DISK = N'([^']+)'", sql)
        if m:
            Path(m.group(2)).write_bytes(b"BAK" * 1000)

    def query(self, sql, params=None):
        if "backupset" in sql:
            return [(self.set_id,)]
        if "master_files" in sql:
            return [(self.size_mb,)]
        if sql.startswith("SELECT DB_ID"):
            return [(1 if self.rt_exists else None,)]
        if sql.startswith("RESTORE FILELISTONLY"):
            return [("snowmeet_new", "x.mdf", "D"), ("snowmeet_new_log", "x.ldf", "L")]
        raise AssertionError(sql)


class FakeS3:
    def __init__(self):
        self.objects = {}
        self.corrupt = False

    def upload(self, local, key):
        self.objects[key] = Path(local).stat().st_size

    def head_size(self, key):
        return self.objects[key] + (1 if self.corrupt else 0)


def gz(src, dst):
    with open(str(dst), "wb") as out:
        subprocess.run(["gzip", "-6", "-c", str(src)], stdout=out, check=True)


class JobTestBase(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        root = Path(self._tmp.name)
        self.staging = root / "staging"
        self.staging.mkdir()
        self.sql, self.s3 = FakeSql(), FakeS3()
        self.notes = []
        self.clock = dt.datetime(2026, 11, 16, 2, 30)
        self.free = 50.0
        self.env = jobs.Env(
            sql=self.sql, s3=self.s3, notify=lambda lvl, msg: self.notes.append((lvl, msg)),
            now=lambda: self.clock, staging=self.staging, state_path=root / "state" / "state.json",
            lock_path=root / "state" / "lock", gzip_fn=gz, free_gb=lambda p: self.free)

    def tearDown(self):
        self._tmp.cleanup()

    def state(self):
        return core.load_state(self.env.state_path)

    def at(self, hh, mm=0, day=16):
        self.clock = dt.datetime(2026, 11, day, hh, mm)

    def local_files(self):
        return sorted(p.name for p in self.staging.iterdir())


class FullTests(JobTestBase):
    def test_full_happy_path(self):
        r = jobs.run_full(self.env, "snowmeet_new")
        self.assertTrue(self.sql.executed[0].startswith("BACKUP DATABASE [snowmeet_new] TO DISK = N'"))
        for must in ("INIT", "CHECKSUM"):
            self.assertIn(must, self.sql.executed[0])
        for must_not in ("COPY_ONLY", "DIFFERENTIAL"):
            self.assertNotIn(must_not, self.sql.executed[0])
        self.assertTrue(self.sql.executed[1].startswith("RESTORE VERIFYONLY"))
        key = "private/backup/db/snowmeet_new/full/snowmeet_new_full_20261116_0230.bak.gz"
        self.assertIn(key, self.s3.objects)
        self.assertIn(key + ".sha256", self.s3.objects)
        info = self.state()["jobs"]["full:snowmeet_new"]
        self.assertEqual((info["stamp"], info["msdb_set_id"], info["key"]), ("20261116_0230", 100, key))
        self.assertEqual(self.local_files(), ["snowmeet_new_full_20261116_0230.bak"])   # 临时 .gz/.sha256 已清
        self.assertEqual(self.notes, [])
        self.assertEqual(r["key"], key)

    def test_second_full_replaces_old_local_files(self):
        jobs.run_full(self.env, "snowmeet_new")
        self.at(9, 0)
        jobs.run_diff(self.env, "snowmeet_new")
        self.at(2, 30, day=17)
        self.sql.set_id = 101
        jobs.run_full(self.env, "snowmeet_new")
        self.assertEqual(self.local_files(), ["snowmeet_new_full_20261117_0230.bak"])

    def test_copy_only_is_adhoc_and_does_not_touch_state(self):
        jobs.run_full(self.env, "snowmeet_new")
        before = self.state()["jobs"]["full:snowmeet_new"]
        self.at(15, 0)
        jobs.run_full(self.env, "snowmeet_new", copy_only=True)
        self.assertIn("COPY_ONLY", self.sql.executed[-2])
        self.assertIn("private/backup/db/snowmeet_new/adhoc/snowmeet_new_full_20261116_1500.bak.gz", self.s3.objects)
        self.assertEqual(self.state()["jobs"]["full:snowmeet_new"], before)
        self.assertEqual(self.local_files(), ["snowmeet_new_full_20261116_0230.bak"])

    def test_other_db_does_not_keep_local_copy(self):
        self.at(3, 0, day=1)
        jobs.run_full(self.env, "legacy", keep_local=False)
        self.assertIn("private/backup/db/legacy/full/legacy_full_20261101_0300.bak.gz", self.s3.objects)
        self.assertEqual(self.local_files(), [])
        self.assertIn("full:legacy", self.state()["jobs"])

    def test_upload_size_mismatch_fails_and_cleans_up(self):
        self.s3.corrupt = True
        with self.assertRaises(jobs.BackupError):
            jobs.run_full(self.env, "snowmeet_new")
        self.assertIn("full:snowmeet_new", self.state()["failures"])
        self.assertNotIn("full:snowmeet_new", self.state().get("jobs", {}))
        self.assertEqual([n for n in self.local_files() if n.endswith((".gz", ".sha256"))], [])
        self.assertEqual(self.notes[0][0], "error")

    def test_sql_error_is_recorded(self):
        self.sql.fail_on = "VERIFYONLY"
        with self.assertRaises(RuntimeError):
            jobs.run_full(self.env, "snowmeet_new")
        self.assertIn("full:snowmeet_new", self.state()["failures"])
        self.assertEqual(self.s3.objects, {})

    def test_not_enough_disk_refuses_before_backup(self):
        self.free = 0.5
        with self.assertRaises(jobs.BackupError):
            jobs.run_full(self.env, "snowmeet_new")
        self.assertEqual(self.sql.executed, [])

    def test_late_full_warns(self):
        self.at(8, 30)
        jobs.run_full(self.env, "snowmeet_new")
        self.assertTrue(any("08:00" in m for _, m in self.notes))

    def test_busy_when_another_job_holds_the_lock(self):
        with jobs.file_lock(self.env.lock_path):
            with self.assertRaises(jobs.Busy):
                jobs.run_full(self.env, "snowmeet_new")
        self.assertEqual(self.sql.executed, [])


class DiffTests(JobTestBase):
    def test_diff_happy_path(self):
        jobs.run_full(self.env, "snowmeet_new")
        self.at(9, 0)
        r = jobs.run_diff(self.env, "snowmeet_new")
        self.assertIn("DIFFERENTIAL", self.sql.executed[-2])
        self.assertTrue(self.sql.executed[-1].startswith("RESTORE VERIFYONLY"))
        key = "private/backup/db/snowmeet_new/diff/snowmeet_new_diff_20261116_0900_base_20261116_0230.bak.gz"
        self.assertIn(key, self.s3.objects)
        self.assertIn(key + ".sha256", self.s3.objects)
        self.assertEqual(r["base"], "20261116_0230")
        self.assertEqual(self.state()["jobs"]["diff:snowmeet_new"]["key"], key)

    def test_keeps_only_two_latest_local_diffs(self):
        jobs.run_full(self.env, "snowmeet_new")
        for hh, mm in ((9, 0), (9, 30), (10, 0), (10, 30)):
            self.at(hh, mm)
            jobs.run_diff(self.env, "snowmeet_new")
        self.assertEqual(self.local_files(), [
            "snowmeet_new_diff_20261116_1000_base_20261116_0230.bak",
            "snowmeet_new_diff_20261116_1030_base_20261116_0230.bak",
            "snowmeet_new_full_20261116_0230.bak"])

    def test_midnight_diff_belongs_to_previous_full(self):
        jobs.run_full(self.env, "snowmeet_new")
        self.at(0, 10, day=17)
        r = jobs.run_diff(self.env, "snowmeet_new")
        self.assertEqual(r["base"], "20261116_0230")
        self.assertTrue(r["key"].endswith("snowmeet_new_diff_20261117_0010_base_20261116_0230.bak.gz"), r["key"])

    def test_diff_without_full_fails(self):
        with self.assertRaises(jobs.BackupError):
            jobs.run_diff(self.env, "snowmeet_new")
        self.assertEqual(self.sql.executed, [])
        self.assertEqual(self.notes[0][0], "error")

    def test_diff_stops_when_base_was_replaced_outside_the_script(self):
        jobs.run_full(self.env, "snowmeet_new")
        self.sql.set_id = 999          # 有人手动做了没带 COPY_ONLY 的完整备份
        self.at(9, 0)
        with self.assertRaises(jobs.BackupError) as cm:
            jobs.run_diff(self.env, "snowmeet_new")
        self.assertIn("COPY_ONLY", str(cm.exception))
        self.assertFalse(any("DIFFERENTIAL" in s for s in self.sql.executed))

    def test_diff_upload_mismatch_cleans_up(self):
        jobs.run_full(self.env, "snowmeet_new")
        self.at(9, 0)
        self.s3.corrupt = True
        with self.assertRaises(jobs.BackupError):
            jobs.run_diff(self.env, "snowmeet_new")
        self.assertEqual(self.local_files(), ["snowmeet_new_full_20261116_0230.bak"])
        self.assertIn("diff:snowmeet_new", self.state()["failures"])

    def test_busy_diff_is_skipped_not_failed(self):
        jobs.run_full(self.env, "snowmeet_new")
        self.at(9, 0)
        with jobs.file_lock(self.env.lock_path):
            with self.assertRaises(jobs.Busy):
                jobs.run_diff(self.env, "snowmeet_new")
        self.assertNotIn("diff:snowmeet_new", self.state().get("failures", {}))


class CheckTests(JobTestBase):
    def test_check_alerts_once_per_cooldown_then_clears(self):
        jobs.run_full(self.env, "snowmeet_new")
        self.at(9, 0)
        jobs.run_diff(self.env, "snowmeet_new")
        self.at(9, 20)
        self.assertEqual(jobs.run_check(self.env, "snowmeet_new"), [])
        self.at(11, 0)                              # 10:00 之后的差异备份都没做
        self.notes.clear()
        self.assertEqual([k for k, _ in jobs.run_check(self.env, "snowmeet_new")], ["diff:snowmeet_new:missed"])
        self.assertEqual(len(self.notes), 1)
        self.at(11, 30)
        jobs.run_check(self.env, "snowmeet_new")
        self.assertEqual(len(self.notes), 1)        # 6 小时内不重复提醒
        self.at(11, 40)
        jobs.run_diff(self.env, "snowmeet_new")
        self.at(11, 45)
        self.assertEqual(jobs.run_check(self.env, "snowmeet_new"), [])
        self.assertEqual(self.state().get("alerts"), {})


class DrillTests(JobTestBase):
    def test_restore_statements(self):
        st = jobs.build_restore_statements("snowmeet_new_rt", "/b/full.bak", "/b/diff.bak",
                                           [("snowmeet_new", "x.mdf", "D"), ("snowmeet_new_log", "x.ldf", "L")], "/b/rt")
        self.assertEqual(len(st), 3)
        self.assertIn("NORECOVERY", st[0])
        self.assertIn("MOVE N'snowmeet_new' TO N'/b/rt/snowmeet_new_rt_0.mdf'", st[0])
        self.assertIn("MOVE N'snowmeet_new_log' TO N'/b/rt/snowmeet_new_rt_1.ldf'", st[0])
        self.assertIn("FROM DISK = N'/b/diff.bak' WITH RECOVERY", st[1])
        self.assertTrue(st[2].startswith("DBCC CHECKDB ([snowmeet_new_rt])"))
        no_diff = jobs.build_restore_statements("t_rt", "/b/full.bak", None, [("a", "x", "D")], "/b/rt")
        self.assertEqual(len(no_diff), 2)
        self.assertIn(" RECOVERY", no_diff[0])
        self.assertNotIn("NORECOVERY", no_diff[0])

    def test_drill_runs_and_always_drops_temp_db(self):
        jobs.run_full(self.env, "snowmeet_new")
        self.at(9, 0)
        jobs.run_diff(self.env, "snowmeet_new")
        self.sql.executed.clear()
        jobs.run_drill(self.env, "snowmeet_new")
        self.assertTrue(self.sql.executed[0].startswith("RESTORE DATABASE [snowmeet_new_rt]"))
        self.assertTrue(self.sql.executed[-1].startswith("IF DB_ID(N'snowmeet_new_rt') IS NOT NULL DROP DATABASE"))
        self.assertIn("drill:snowmeet_new", self.state()["jobs"])

    def test_drill_failure_still_drops_temp_db(self):
        jobs.run_full(self.env, "snowmeet_new")
        self.sql.executed.clear()
        self.sql.fail_on = "DBCC CHECKDB"
        with self.assertRaises(RuntimeError):
            jobs.run_drill(self.env, "snowmeet_new")
        self.assertTrue(self.sql.executed[-1].startswith("IF DB_ID"))
        self.assertIn("drill:snowmeet_new", self.state()["failures"])

    def test_drill_never_drops_a_pre_existing_database(self):
        jobs.run_full(self.env, "snowmeet_new")
        self.sql.executed.clear()
        self.sql.rt_exists = True
        with self.assertRaises(jobs.BackupError):
            jobs.run_drill(self.env, "snowmeet_new")
        self.assertEqual(self.sql.executed, [])


if __name__ == "__main__":
    unittest.main()
