import datetime as dt
import tempfile
import unittest
from pathlib import Path

from snowmeet_backup import core


def at(day, hh, mm=0):
    return dt.datetime.combine(dt.date(2026, 11, day), dt.time(hh, mm))


class NamingTests(unittest.TestCase):
    def test_keys(self):
        t = at(16, 2, 30)
        self.assertEqual(core.full_key("snowmeet_new", t),
                         "private/backup/db/snowmeet_new/full/snowmeet_new_full_20261116_0230.bak.gz")
        self.assertEqual(core.full_key("snowmeet_new", t, adhoc=True),
                         "private/backup/db/snowmeet_new/adhoc/snowmeet_new_full_20261116_0230.bak.gz")
        self.assertEqual(core.diff_key("snowmeet_new", at(16, 9, 30), "20261116_0230"),
                         "private/backup/db/snowmeet_new/diff/snowmeet_new_diff_20261116_0930_base_20261116_0230.bak.gz")

    def test_db_name_is_validated(self):
        for bad in ("", "a b", "x';DROP", "a/b", "库"):
            with self.assertRaises(ValueError):
                core.check_db_name(bad)

    def test_local_names_round_trip(self):
        n = core.local_bak_name("snowmeet_new", "diff", at(16, 9, 30), "20261116_0230")
        self.assertEqual(core.parse_local_name(n),
                         {"db": "snowmeet_new", "kind": "diff", "stamp": "20261116_0930", "base": "20261116_0230"})
        n = core.local_bak_name("snowmeet_new", "full", at(16, 2, 30))
        self.assertEqual(core.parse_local_name(n)["kind"], "full")
        self.assertIsNone(core.parse_local_name("random.bak"))


class ExpiryAlignmentTests(unittest.TestCase):
    """差异备份要和它所属的完整备份在同一时刻过期（S3 按 UTC 零点取整）。"""

    def test_schedule_has_18_diffs(self):
        self.assertEqual(len(core.DIFF_TIMES), 18)
        self.assertIn(dt.time(9, 0), core.DIFF_TIMES)
        self.assertIn(dt.time(17, 0), core.DIFF_TIMES)
        self.assertIn(dt.time(0, 10), core.DIFF_TIMES)
        self.assertNotIn(dt.time(17, 30), core.DIFF_TIMES)

    def test_diffs_expire_with_their_full(self):
        # 完整备份：02:30 开始，04:15 前上传完（上传完成时刻 02:30～04:15 都算）
        fulls = [at(16, 2, 30) + dt.timedelta(minutes=m) for m in range(0, 106)]
        # 差异备份：当天 09:00～17:00 每半小时、次日 00:10；上传可能比整点晚 0～20 分钟
        diffs = []
        for t in core.DIFF_TIMES:
            day = 17 if t == dt.time(0, 10) else 16
            base = dt.datetime.combine(dt.date(2026, 11, day), t)
            diffs += [base + dt.timedelta(minutes=m) for m in range(0, 21)]
        expected = {core.expiry_utc(f, core.FULL_DAYS) for f in fulls}
        self.assertEqual(len(expected), 1)
        for d in diffs:
            self.assertEqual({core.expiry_utc(d, core.DIFF_DAYS)}, expected, d)

    def test_expiry_is_utc_midnight_of_day_8_beijing_0800(self):
        e = core.expiry_utc(at(16, 2, 40), core.FULL_DAYS)
        self.assertEqual(e, dt.datetime(2026, 11, 23, 0, 0))   # UTC；北京时间 11-23 08:00

    def test_late_full_is_out_of_alignment_and_detected(self):
        late = at(16, 8, 5)
        self.assertFalse(core.full_in_alignment_window(late))
        self.assertTrue(core.full_in_alignment_window(at(16, 4, 15)))
        self.assertNotEqual(core.expiry_utc(late, core.FULL_DAYS), core.expiry_utc(at(16, 9, 0), core.DIFF_DAYS))


class ConfigTests(unittest.TestCase):
    def test_parse(self):
        self.assertEqual(core.parse_sql_config("Server=x;Database=d;UID=sa;PWD=p@ss;"), ("sa", "p@ss"))
        self.assertEqual(core.parse_sql_config("Data Source=x;User Id=u;Password=pw"), ("u", "pw"))
        self.assertEqual(core.parse_sql_config("Server=x\r\nuid=a;pwd=b;\r\n"), ("a", "b"))

    def test_error_does_not_leak_content(self):
        with self.assertRaises(ValueError) as cm:
            core.parse_sql_config("Server=x;SECRETVALUE=1")
        self.assertNotIn("SECRETVALUE", str(cm.exception))


class StateTests(unittest.TestCase):
    def test_round_trip_and_atomic(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "sub" / "state.json"
            self.assertEqual(core.load_state(p), {})
            s = {}
            core.record_ok(s, "full:x", at(16, 2, 40), key="k")
            core.record_fail(s, "diff:x", at(16, 9, 0), "boom")
            core.save_state(p, s)
            self.assertEqual(core.load_state(p), s)
            self.assertFalse(p.with_name("state.json.tmp").exists())

    def test_ok_clears_failure(self):
        s = {}
        core.record_fail(s, "full:x", at(16, 2, 40), "boom")
        core.record_ok(s, "full:x", at(16, 2, 50))
        self.assertNotIn("full:x", s["failures"])


class HealthTests(unittest.TestCase):
    def healthy(self):
        return {"enabled_from": "2026-11-10T00:00:00", "jobs": {
            "full:snowmeet_new": {"ok_at": "2026-11-16T02:41:00"},
            "diff:snowmeet_new": {"ok_at": "2026-11-16T09:31:00"}}}

    def keys(self, state, now, free=50.0):
        return [k for k, _ in core.evaluate_health(state, now, free)]

    def test_all_good(self):
        self.assertEqual(self.keys(self.healthy(), at(16, 9, 40)), [])

    def test_grace_period_for_running_job(self):
        s = self.healthy()
        s["jobs"]["diff:snowmeet_new"]["ok_at"] = "2026-11-16T09:01:00"
        self.assertEqual(self.keys(s, at(16, 9, 50)), [])                      # 10:00 的还没到点，09:30 的刚过 20 分钟
        self.assertIn("diff:snowmeet_new:missed", self.keys(s, at(16, 9, 58)))  # 09:30 的超过 25 分钟没成功

    def test_missed_full(self):
        s = self.healthy()
        s["jobs"]["full:snowmeet_new"]["ok_at"] = "2026-11-15T02:41:00"
        self.assertEqual(self.keys(s, at(16, 3, 0)), [])                       # 02:30 的 full 还在宽限期内
        self.assertIn("full:snowmeet_new:missed", self.keys(s, at(16, 4, 20)))

    def test_midnight_diff_counts(self):
        s = self.healthy()
        s["jobs"]["diff:snowmeet_new"]["ok_at"] = "2026-11-16T17:01:00"
        self.assertEqual(self.keys(s, at(16, 23, 0)), [])
        self.assertIn("diff:snowmeet_new:missed", self.keys(s, at(17, 0, 40)))

    def test_not_checked_before_enabled(self):
        s = {"enabled_from": "2026-11-16T09:20:00", "jobs": {}}
        self.assertEqual(self.keys(s, at(16, 9, 40)), [])

    def test_failure_and_recovery(self):
        s = self.healthy()
        core.record_fail(s, "diff:snowmeet_new", at(16, 9, 35), "boom")        # 在上一次成功（09:31）之后失败
        self.assertIn("diff:snowmeet_new:failed", self.keys(s, at(16, 9, 40)))
        core.record_ok(s, "diff:snowmeet_new", at(16, 9, 38))
        self.assertEqual(self.keys(s, at(16, 9, 40)), [])

    def test_old_failure_before_a_later_success_is_ignored(self):
        s = self.healthy()                                                      # 09:31 成功
        s.setdefault("failures", {})["diff:snowmeet_new"] = {"at": "2026-11-16T09:30:00", "msg": "old"}
        self.assertEqual(self.keys(s, at(16, 9, 40)), [])

    def test_disk_low(self):
        self.assertIn("disk:low", self.keys(self.healthy(), at(16, 9, 40), free=3.0))

    def test_alert_cooldown(self):
        s = {}
        self.assertTrue(core.should_alert(s, "k", at(16, 9, 0)))
        core.mark_alerted(s, "k", at(16, 9, 0))
        self.assertFalse(core.should_alert(s, "k", at(16, 12, 0)))
        self.assertTrue(core.should_alert(s, "k", at(16, 15, 1)))


if __name__ == "__main__":
    unittest.main()
