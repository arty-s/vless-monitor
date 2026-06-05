"""
Manages the xray-core subprocess: start, stop, config generation.
Downloads xray-windows-64 on first run if not present.
"""
import json
import logging
import os
import subprocess
import tempfile
import threading
import time
import urllib.request
import zipfile
from pathlib import Path

from vless_uri import VlessConfig, build_xray_client_config

log = logging.getLogger(__name__)

XRAY_DIR = Path(__file__).parent / "xray"
XRAY_EXE = XRAY_DIR / "xray.exe"
XRAY_DOWNLOAD_URL = (
    "https://github.com/XTLS/Xray-core/releases/latest/download/"
    "Xray-windows-64.zip"
)


def download_xray(progress_cb=None) -> bool:
    """Download latest xray-windows-64.zip and extract xray.exe."""
    XRAY_DIR.mkdir(exist_ok=True)
    zip_path = XRAY_DIR / "xray-windows-64.zip"

    try:
        log.info("Downloading xray-core from GitHub...")
        if progress_cb:
            progress_cb("Скачивание xray-core...")

        def _report(block, block_size, total):
            if total > 0 and progress_cb:
                pct = min(100, int(block * block_size * 100 / total))
                progress_cb(f"Скачивание xray-core... {pct}%")

        urllib.request.urlretrieve(XRAY_DOWNLOAD_URL, zip_path, _report)

        log.info("Extracting xray.exe...")
        with zipfile.ZipFile(zip_path, "r") as z:
            for name in z.namelist():
                if name.lower().endswith("xray.exe"):
                    data = z.read(name)
                    XRAY_EXE.write_bytes(data)
                    break
        zip_path.unlink(missing_ok=True)

        if progress_cb:
            progress_cb("xray-core готов")
        log.info(f"xray.exe extracted to {XRAY_EXE}")
        return True

    except Exception as e:
        log.error(f"Failed to download xray: {e}")
        if progress_cb:
            progress_cb(f"Ошибка скачивания: {e}")
        return False


class XrayManager:
    def __init__(self, vless: VlessConfig, socks_port: int = 10808):
        self.vless = vless
        self.socks_port = socks_port
        self._proc: subprocess.Popen | None = None
        self._config_file: str | None = None
        self._lock = threading.Lock()
        self._running = False

    @property
    def is_running(self) -> bool:
        with self._lock:
            return self._proc is not None and self._proc.poll() is None

    def ensure_xray(self, progress_cb=None) -> bool:
        if XRAY_EXE.exists():
            return True
        return download_xray(progress_cb)

    def start(self) -> bool:
        with self._lock:
            if self._proc and self._proc.poll() is None:
                return True  # already running

            if not XRAY_EXE.exists():
                log.error("xray.exe not found")
                return False

            cfg = build_xray_client_config(self.vless, self.socks_port)

            # Write config to temp file
            fd, path = tempfile.mkstemp(suffix=".json", prefix="xray_cfg_")
            with os.fdopen(fd, "w") as f:
                json.dump(cfg, f)
            self._config_file = path

            try:
                self._proc = subprocess.Popen(
                    [str(XRAY_EXE), "run", "-c", path],
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                    creationflags=subprocess.CREATE_NO_WINDOW,
                )
                time.sleep(0.8)  # let xray bind the port
                if self._proc.poll() is not None:
                    log.error(f"xray exited immediately (code {self._proc.returncode})")
                    return False
                log.info(f"xray started (PID {self._proc.pid}), SOCKS5 on 127.0.0.1:{self.socks_port}")
                return True
            except Exception as e:
                log.error(f"Failed to start xray: {e}")
                return False

    def stop(self):
        with self._lock:
            if self._proc:
                try:
                    self._proc.terminate()
                    self._proc.wait(timeout=3)
                except Exception:
                    try:
                        self._proc.kill()
                    except Exception:
                        pass
                self._proc = None
            if self._config_file and os.path.exists(self._config_file):
                os.unlink(self._config_file)
                self._config_file = None

    def restart(self) -> bool:
        self.stop()
        time.sleep(0.5)
        return self.start()
