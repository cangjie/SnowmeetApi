import json
import unittest

from snowmeet_backup import real


class FakeResp:
    def __init__(self, payload):
        self._payload = payload

    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False

    def read(self):
        return json.dumps(self._payload).encode("utf-8")


class PostAlertTests(unittest.TestCase):
    def test_sends_json_with_token_header(self):
        seen = {}

        def opener(req, timeout):
            seen["url"], seen["method"] = req.full_url, req.get_method()
            seen["token"], seen["body"], seen["timeout"] = req.get_header("X-backup-token"), req.data, timeout
            return FakeResp({"code": 0, "message": "sent"})

        self.assertTrue(real.post_alert("http://x/api/BackupAlert/Notify", "tok", "error", "完整备份失败", opener=opener))
        self.assertEqual((seen["url"], seen["method"], seen["token"]), ("http://x/api/BackupAlert/Notify", "POST", "tok"))
        self.assertEqual(json.loads(seen["body"].decode("utf-8")), {"level": "error", "message": "完整备份失败"})
        self.assertEqual(seen["timeout"], 15)

    def test_backend_refusal_is_reported_as_failure(self):
        self.assertFalse(real.post_alert("http://x", "tok", "error", "m",
                                         opener=lambda req, timeout: FakeResp({"code": 1, "message": "no_receivers"})))

    def test_network_errors_never_raise_and_never_log_the_token(self):
        def boom(req, timeout):
            raise OSError("connection refused")

        with self.assertLogs("snowmeet_backup", level="WARNING") as cm:
            self.assertFalse(real.post_alert("http://x", "SECRET-TOKEN", "error", "m", opener=boom))
        self.assertNotIn("SECRET-TOKEN", "\n".join(cm.output))


class NotifierTests(unittest.TestCase):
    def test_only_errors_and_warnings_go_to_wecom(self):
        sent = []
        n = real.make_notifier(hook="/nonexistent", poster=lambda lvl, msg: sent.append((lvl, msg)) or True)
        n("info", "i")
        n("warning", "w")
        n("error", "e")
        self.assertEqual(sent, [("warning", "w"), ("error", "e")])

    def test_poster_failure_does_not_break_the_backup(self):
        def poster(lvl, msg):
            raise RuntimeError("boom")

        real.make_notifier(hook="/nonexistent", poster=poster)("error", "e")   # 不抛异常即通过


if __name__ == "__main__":
    unittest.main()
