#!/usr/bin/env python3
"""
VLESS Monitor - probe server (runs on VPS).
Provides endpoints for tunnel verification and xray stats.

Usage: python3 probe_server.py [--port 8765] [--secret your-secret]
"""
import argparse
import json
import time
import threading
import urllib.request
from collections import deque
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler

SECRET = "vless-monitor-secret-2026"
MAX_BEACONS = 200
BEACON_TTL = 300  # seconds

_beacons: dict[str, float] = {}   # uuid -> timestamp
_beacons_lock = threading.Lock()

# ---- Cleanup old beacons periodically ----
def _cleanup():
    while True:
        time.sleep(60)
        now = time.time()
        with _beacons_lock:
            stale = [k for k, t in _beacons.items() if now - t > BEACON_TTL]
            for k in stale:
                del _beacons[k]

threading.Thread(target=_cleanup, daemon=True).start()


def _get_xray_stats() -> dict:
    """Get xray traffic stats via xray CLI (api statsquery)."""
    import subprocess, re
    xray_bin = "/usr/local/x-ui/bin/xray-linux-amd64"
    api_addr = "127.0.0.1:62789"
    uplink = downlink = 0.0
    try:
        result = subprocess.run(
            [xray_bin, "api", "statsquery", "--server", api_addr],
            capture_output=True, text=True, timeout=5
        )
        for line in result.stdout.splitlines():
            # Lines like: name:"inbound>>>inbound-53144>>>traffic>>>uplink" value:12345
            m = re.search(r'"([^"]+)"\s+value:(\d+)', line)
            if not m:
                # JSON format: "name": "...", "value": "12345"
                nm = re.search(r'"name"\s*:\s*"([^"]+)"', line)
                vm = re.search(r'"value"\s*:\s*"(\d+)"', line)
                if nm and vm:
                    name, val = nm.group(1), int(vm.group(1))
                else:
                    continue
            else:
                name, val = m.group(1), int(m.group(2))

            if "uplink" in name:
                uplink += val
            elif "downlink" in name:
                downlink += val

        return {
            "uplink_mb": round(uplink / 1024 / 1024, 2),
            "downlink_mb": round(downlink / 1024 / 1024, 2),
            "raw_ok": True,
        }
    except Exception as e:
        return {"uplink_mb": 0, "downlink_mb": 0, "raw_ok": False, "error": str(e)}


class Handler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass  # silence default access log

    def _check_auth(self) -> bool:
        return self.headers.get("X-Token", "") == SECRET

    def _send_json(self, code: int, data: dict):
        body = json.dumps(data).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if not self._check_auth():
            self._send_json(403, {"error": "forbidden"})
            return

        path = self.path.split("?")[0]

        if path == "/health":
            self._send_json(200, {"ok": True, "ts": time.time()})

        elif path.startswith("/beacon/"):
            uid = path[len("/beacon/"):]
            with _beacons_lock:
                received = uid in _beacons
                ts = _beacons.get(uid)
            self._send_json(200, {"received": received, "ts": ts})

        elif path == "/xray/stats":
            self._send_json(200, _get_xray_stats())

        elif path.startswith("/stream/"):
            # Return N kilobytes of data for DPI freeze detection
            try:
                kb = min(int(path[len("/stream/"):]), 512)
            except ValueError:
                kb = 24
            self.send_response(200)
            self.send_header("Content-Type", "application/octet-stream")
            self.send_header("Content-Length", str(kb * 1024))
            self.end_headers()
            chunk = b"x" * 4096
            remaining = kb * 1024
            while remaining > 0:
                send_size = min(len(chunk), remaining)
                self.wfile.write(chunk[:send_size])
                remaining -= send_size

        else:
            self._send_json(404, {"error": "not found"})

    def do_POST(self):
        if not self._check_auth():
            self._send_json(403, {"error": "forbidden"})
            return

        path = self.path.split("?")[0]

        if path.startswith("/beacon/"):
            uid = path[len("/beacon/"):]
            with _beacons_lock:
                _beacons[uid] = time.time()
                # Trim old entries if too many
                if len(_beacons) > MAX_BEACONS:
                    oldest = sorted(_beacons, key=lambda k: _beacons[k])
                    for k in oldest[:MAX_BEACONS // 2]:
                        del _beacons[k]
            self._send_json(200, {"ok": True})
        else:
            self._send_json(404, {"error": "not found"})


def main():
    global SECRET
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--secret", default=SECRET)
    args = parser.parse_args()

    SECRET = args.secret

    # ThreadingHTTPServer: each request in its own thread, so a slow /stream
    # request can't block health/beacon checks (the bug that caused timeouts).
    server = ThreadingHTTPServer(("0.0.0.0", args.port), Handler)
    server.daemon_threads = True
    server.request_queue_size = 64
    print(f"Probe server listening on 0.0.0.0:{args.port} (threaded)")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
