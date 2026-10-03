"""心跳上报：状态判断、「距上一轮」的起算点、和服务器的请求格式（本机起一个假服务器，不连外网）。"""

import asyncio
import json
import threading
import unittest
from http.server import BaseHTTPRequestHandler, HTTPServer

from meituan_collector.config import Config
from meituan_collector.heartbeat import HeartbeatClient, RoundState, build_payload, request
from meituan_collector.sanitizer import MASK, Sanitizer

SANITIZE = Sanitizer().sanitize_text


class PayloadTest(unittest.TestCase):
    def test_just_started(self):
        p = build_payload(RoundState(started_at=100), False, None, SANITIZE, now=160)
        self.assertEqual(p["status"], "running")
        self.assertEqual(p["seconds_since_last_round"], 60)
        self.assertIsNone(p["message"])

    def test_waiting_login_wins(self):
        s = RoundState(started_at=0)
        s.round_done(False, "boom", 0, now=10)
        self.assertEqual(build_payload(s, True, None, SANITIZE, now=20)["status"], "need_login")

    def test_round_ok_and_error(self):
        s = RoundState(started_at=0)
        s.round_done(True, None, 3, now=500)
        p = build_payload(s, False, None, SANITIZE, now=530)
        self.assertEqual((p["status"], p["seconds_since_last_round"], p["backfill_pending"]), ("ok", 30, 3))
        s.round_done(False, "TimeoutError 联系人 13912345678", 0, now=600)
        p = build_payload(s, False, None, SANITIZE, now=600)
        self.assertEqual(p["status"], "error")
        self.assertIn(MASK, p["message"])          # 出错信息里的手机号也要打码
        self.assertNotIn("13912345678", p["message"])

    def test_login_restored_resets_stall_clock(self):
        # 等登录等了 5 小时，刚登录上：不能让服务器以为 5 小时没完成一轮
        s = RoundState(started_at=0)
        s.round_done(True, None, 0, now=100)
        p = build_payload(s, False, 18_100, SANITIZE, now=18_160)
        self.assertEqual(p["seconds_since_last_round"], 60)


class _FakeServer(BaseHTTPRequestHandler):
    seen: list = []
    reply = 200

    def _answer(self, body: bytes = b""):
        type(self).seen.append((self.command, self.path, self.headers.get("X-Collector-Token"), body))
        self.send_response(type(self).reply)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(b'{"code":0,"data":{"status":"ok"}}')

    def do_POST(self):
        self._answer(self.rfile.read(int(self.headers["Content-Length"])))

    def do_GET(self):
        self._answer()

    def log_message(self, *args):
        pass


class RequestTest(unittest.TestCase):
    def setUp(self):
        _FakeServer.seen = []
        _FakeServer.reply = 200
        self.server = HTTPServer(("127.0.0.1", 0), _FakeServer)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.config = Config(server_url=f"http://127.0.0.1:{self.server.server_port}/", server_token="t0ken")

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()

    def test_post_heartbeat(self):
        status, _ = request(self.config, "POST", "Heartbeat", {"status": "need_login", "machine": "门店"})
        self.assertEqual(status, 200)
        method, path, token, body = _FakeServer.seen[0]
        self.assertEqual((method, path, token), ("POST", "/api/MeituanCollector/Heartbeat", "t0ken"))
        self.assertEqual(json.loads(body.decode("utf-8")), {"status": "need_login", "machine": "门店"})

    def test_get_status(self):
        status, text = request(self.config, "GET", "Status")
        self.assertEqual((status, _FakeServer.seen[0][1]), (200, "/api/MeituanCollector/Status"))
        self.assertEqual(json.loads(text)["data"]["status"], "ok")

    def test_client_send(self):
        client = HeartbeatClient(self.config, lambda: {"status": "ok"})
        self.assertTrue(client.enabled)
        self.assertTrue(asyncio.run(client.send()))
        _FakeServer.reply = 401
        self.assertFalse(asyncio.run(client.send()))

    def test_unreachable_server_is_not_fatal(self):
        # 端口 1 上没人监听：连接被拒，send 返回 False、不抛异常
        client = HeartbeatClient(Config(server_url="http://127.0.0.1:1", server_token="t"), lambda: {"status": "ok"})
        self.assertFalse(asyncio.run(client.send()))

    def test_disabled_without_token(self):
        self.assertFalse(HeartbeatClient(Config(server_token=None), lambda: {}).enabled)
        self.assertFalse(HeartbeatClient(Config(server_token=""), lambda: {}).enabled)


if __name__ == "__main__":
    unittest.main()
