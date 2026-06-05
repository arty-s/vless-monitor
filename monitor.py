"""
VLESS Monitor — entry point.
Run: pythonw monitor.py   (silent) or python monitor.py (with console)
"""
import json
import logging
import sys
import os
from pathlib import Path

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s %(name)s: %(message)s",
)

BASE = Path(__file__).parent

# When frozen by PyInstaller, files are next to the exe, not in _MEIPASS
if getattr(sys, "frozen", False):
    BASE = Path(sys.executable).parent

CONFIG_PATH = BASE / "config.json"
CONFIG_EXAMPLE = BASE / "config.example.json"


def load_config() -> dict:
    if not CONFIG_PATH.exists():
        # First run: copy example
        import shutil
        src = CONFIG_EXAMPLE if CONFIG_EXAMPLE.exists() else None
        if src:
            shutil.copy(src, CONFIG_PATH)
        else:
            import json as _json
            CONFIG_PATH.write_text(_json.dumps({
                "vless_uri": "", "local_socks5_port": 10808,
                "check_interval_sec": 30, "probe_server_port": 8765,
                "probe_secret": "vless-monitor-secret-2026",
                "dpi_probe_size_kb": 24, "latency_ratio_threshold": 4.0,
                "ping_targets": {
                    "Google DNS (8.8.8.8)": "8.8.8.8",
                    "Cloudflare (1.1.1.1)": "1.1.1.1",
                    "Яндекс RU (77.88.8.8)": "77.88.8.8"
                }
            }, ensure_ascii=False, indent=4))

    with open(CONFIG_PATH, encoding="utf-8") as f:
        cfg = json.load(f)
    if "vps_ip" not in cfg and cfg.get("vless_uri"):
        import urllib.parse
        cfg["vps_ip"] = urllib.parse.urlparse(cfg["vless_uri"]).hostname or ""
    return cfg


def main():
    from PyQt6.QtWidgets import QApplication, QSystemTrayIcon
    from PyQt6.QtCore import Qt

    app = QApplication(sys.argv)
    app.setQuitOnLastWindowClosed(False)   # keep running when windows close
    app.setApplicationName("VLESS Monitor")

    if not QSystemTrayIcon.isSystemTrayAvailable():
        from PyQt6.QtWidgets import QMessageBox
        QMessageBox.critical(None, "Ошибка", "Системный трей недоступен.")
        sys.exit(1)

    cfg = load_config()

    # ── Imports ──────────────────────────────────────────────
    from vless_uri import parse_vless_uri
    from xray_manager import XrayManager, XRAY_EXE
    from checker import Checker, MonitorState
    from ui.tray import TrayIcon
    from ui.main_window import MainWindow
    from ui.settings_window import SettingsWindow

    # ── Download xray if needed ──────────────────────────────
    if not XRAY_EXE.exists():
        from ui.download_dialog import DownloadDialog
        dlg = DownloadDialog()
        if dlg.exec() != DownloadDialog.DialogCode.Accepted or not dlg.success:
            sys.exit(0)

    # ── UI (create before xray so settings can be shown) ─────
    main_win = MainWindow()
    settings_win: SettingsWindow | None = None
    tray = TrayIcon()

    # ── First-run: no VLESS URI configured ───────────────────
    if not cfg.get("vless_uri"):
        sw = SettingsWindow(cfg)
        sw.setWindowTitle("VLESS Monitor — первый запуск: введите VLESS-ссылку")
        result = sw.exec()          # blocks until accept() / reject()
        cfg = load_config()
        if result != SettingsWindow.DialogCode.Accepted or not cfg.get("vless_uri"):
            sys.exit(0)

    # ── Parse VLESS ──────────────────────────────────────────
    vless = parse_vless_uri(cfg["vless_uri"])

    # ── Start xray ───────────────────────────────────────────
    xray = XrayManager(vless, socks_port=cfg.get("local_socks5_port", 10808))
    if not xray.start():
        from PyQt6.QtWidgets import QMessageBox
        QMessageBox.critical(None, "Ошибка xray",
                             f"Не удалось запустить xray.\n{XRAY_EXE}")
        sys.exit(1)

    def open_main():
        main_win.show()
        main_win.raise_()
        main_win.activateWindow()

    def open_settings():
        nonlocal settings_win
        if settings_win and settings_win.isVisible():
            settings_win.raise_()
            return
        settings_win = SettingsWindow(cfg)
        settings_win.config_changed.connect(on_config_changed)
        settings_win.show()

    def on_refresh():
        checker.run_now()

    def on_config_changed(new_cfg: dict):
        nonlocal cfg, vless
        cfg.update(new_cfg)
        checker.cfg = cfg
        # Restart xray if VLESS URI or port changed
        vless = parse_vless_uri(cfg["vless_uri"])
        xray.vless = vless
        xray.socks_port = cfg.get("local_socks5_port", 10808)
        xray.restart()
        checker.run_now()

    def on_state_update(state: MonitorState):
        tray.update_state(state)
        main_win.update_state(state)

    def on_exit():
        checker.stop()
        xray.stop()
        app.quit()

    tray.open_requested.connect(open_main)
    tray.refresh_requested.connect(on_refresh)
    tray.settings_requested.connect(open_settings)
    tray.exit_requested.connect(on_exit)

    # ── Checker ──────────────────────────────────────────────
    checker = Checker(cfg, on_update=on_state_update)
    checker.start()

    tray.show()

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
