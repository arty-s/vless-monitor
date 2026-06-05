"""
Settings window.
"""
import json
import os
import subprocess
import winreg
from pathlib import Path

from PyQt6.QtCore import Qt, pyqtSignal
from PyQt6.QtGui import QFont
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QFormLayout,
    QLabel, QLineEdit, QSpinBox, QCheckBox, QPushButton,
    QGroupBox, QMessageBox, QFrame, QSizePolicy,
)

CONFIG_PATH = Path(__file__).parent.parent / "config.json"

BG = "#1e1e2e"; BG2 = "#2a2a3e"; BG3 = "#313244"
FG = "#cdd6f4"; FG_DIM = "#6c7086"
GREEN = "#a6e3a1"; BLUE = "#89b4fa"; LAVENDER = "#b4befe"
RED = "#f38ba8"

AUTORUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
AUTORUN_NAME = "VlessMonitor"


class SettingsWindow(QWidget):
    config_changed = pyqtSignal(dict)

    def __init__(self, config: dict):
        super().__init__()
        self._cfg = dict(config)
        self.setWindowTitle("Настройки — VLESS Monitor")
        self.setFixedSize(560, 520)
        self._setup_ui()
        self._load_values()

    def _setup_ui(self):
        self.setStyleSheet(f"QWidget {{ background:{BG}; color:{FG}; font-family:'Segoe UI'; font-size:10pt; }}")

        root = QVBoxLayout(self)
        root.setContentsMargins(16, 16, 16, 16)
        root.setSpacing(12)

        # ── VLESS ──────────────────────────────────────────
        grp_vless = self._group("VLESS подключение")
        fl = QFormLayout(grp_vless)
        fl.setSpacing(8)

        self._uri_edit = QLineEdit()
        self._uri_edit.setPlaceholderText("vless://UUID@host:port?...")
        self._uri_edit.setStyleSheet(self._input_style())
        fl.addRow("VLESS URI:", self._uri_edit)

        self._port_spin = QSpinBox()
        self._port_spin.setRange(1024, 65535)
        self._port_spin.setStyleSheet(self._input_style())
        fl.addRow("Локальный SOCKS5 порт:", self._port_spin)

        root.addWidget(grp_vless)

        # ── Checks ─────────────────────────────────────────
        grp_chk = self._group("Параметры проверок")
        fl2 = QFormLayout(grp_chk)
        fl2.setSpacing(8)

        self._interval_spin = QSpinBox()
        self._interval_spin.setRange(10, 3600)
        self._interval_spin.setSuffix(" сек")
        self._interval_spin.setStyleSheet(self._input_style())
        fl2.addRow("Интервал проверки:", self._interval_spin)

        self._dpi_kb_spin = QSpinBox()
        self._dpi_kb_spin.setRange(8, 256)
        self._dpi_kb_spin.setSuffix(" КБ")
        self._dpi_kb_spin.setStyleSheet(self._input_style())
        fl2.addRow("DPI probe размер:", self._dpi_kb_spin)

        self._ratio_spin = QSpinBox()
        self._ratio_spin.setRange(2, 20)
        self._ratio_spin.setSuffix("×")
        self._ratio_spin.setStyleSheet(self._input_style())
        fl2.addRow("Порог задержки (ratio):", self._ratio_spin)

        root.addWidget(grp_chk)

        # ── Telegram ───────────────────────────────────────
        grp_tg = self._group("Уведомления Telegram (необязательно)")
        fl3 = QFormLayout(grp_tg)
        fl3.setSpacing(8)

        self._tg_enable = QCheckBox("Включить уведомления")
        self._tg_enable.setStyleSheet(f"color:{FG};")
        fl3.addRow(self._tg_enable)

        self._tg_token = QLineEdit()
        self._tg_token.setPlaceholderText("123456:ABC...")
        self._tg_token.setStyleSheet(self._input_style())
        fl3.addRow("Bot token:", self._tg_token)

        self._tg_chat = QLineEdit()
        self._tg_chat.setPlaceholderText("-100...")
        self._tg_chat.setStyleSheet(self._input_style())
        fl3.addRow("Chat ID:", self._tg_chat)

        root.addWidget(grp_tg)

        # ── System ─────────────────────────────────────────
        grp_sys = self._group("Система")
        hl = QHBoxLayout(grp_sys)
        hl.setSpacing(12)

        self._autostart_cb = QCheckBox("Запускать вместе с Windows")
        self._autostart_cb.setStyleSheet(f"color:{FG};")
        hl.addWidget(self._autostart_cb)
        hl.addStretch()

        root.addWidget(grp_sys)
        root.addStretch()

        # ── Buttons ────────────────────────────────────────
        line = QFrame()
        line.setFrameShape(QFrame.Shape.HLine)
        line.setStyleSheet(f"color:{BG3};")
        root.addWidget(line)

        btn_row = QHBoxLayout()
        btn_row.addStretch()

        btn_cancel = QPushButton("Отмена")
        btn_cancel.setFixedSize(100, 32)
        btn_cancel.setStyleSheet(self._btn_style(FG_DIM))
        btn_cancel.clicked.connect(self.close)
        btn_row.addWidget(btn_cancel)

        btn_save = QPushButton("Сохранить")
        btn_save.setFixedSize(110, 32)
        btn_save.setStyleSheet(self._btn_style(GREEN))
        btn_save.clicked.connect(self._save)
        btn_row.addWidget(btn_save)

        root.addLayout(btn_row)

    def _load_values(self):
        self._uri_edit.setText(self._cfg.get("vless_uri", ""))
        self._port_spin.setValue(self._cfg.get("local_socks5_port", 10808))
        self._interval_spin.setValue(self._cfg.get("check_interval_sec", 30))
        self._dpi_kb_spin.setValue(self._cfg.get("dpi_probe_size_kb", 24))
        self._ratio_spin.setValue(int(self._cfg.get("latency_ratio_threshold", 4)))
        self._tg_enable.setChecked(self._cfg.get("notify_telegram", False))
        self._tg_token.setText(self._cfg.get("telegram_bot_token", ""))
        self._tg_chat.setText(self._cfg.get("telegram_chat_id", ""))
        self._autostart_cb.setChecked(self._is_autostart_set())

    def _save(self):
        uri = self._uri_edit.text().strip()
        if uri and not uri.startswith("vless://"):
            QMessageBox.warning(self, "Ошибка", "VLESS URI должен начинаться с vless://")
            return

        self._cfg["vless_uri"] = uri
        self._cfg["local_socks5_port"] = self._port_spin.value()
        self._cfg["check_interval_sec"] = self._interval_spin.value()
        self._cfg["dpi_probe_size_kb"] = self._dpi_kb_spin.value()
        self._cfg["latency_ratio_threshold"] = float(self._ratio_spin.value())
        self._cfg["notify_telegram"] = self._tg_enable.isChecked()
        self._cfg["telegram_bot_token"] = self._tg_token.text().strip()
        self._cfg["telegram_chat_id"] = self._tg_chat.text().strip()

        # Save to file
        with open(CONFIG_PATH, "w", encoding="utf-8") as f:
            json.dump(self._cfg, f, ensure_ascii=False, indent=4)

        # Autostart
        if self._autostart_cb.isChecked():
            self._set_autostart(True)
        else:
            self._set_autostart(False)

        self.config_changed.emit(dict(self._cfg))
        QMessageBox.information(self, "Сохранено",
                                "Настройки сохранены.\nИзменения применятся на следующем цикле проверок.")
        self.close()

    def _is_autostart_set(self) -> bool:
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, AUTORUN_KEY) as k:
                winreg.QueryValueEx(k, AUTORUN_NAME)
                return True
        except Exception:
            return False

    def _set_autostart(self, enable: bool):
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, AUTORUN_KEY,
                                access=winreg.KEY_SET_VALUE) as k:
                if enable:
                    exe = Path(__file__).parent.parent / "start.vbs"
                    winreg.SetValueEx(k, AUTORUN_NAME, 0, winreg.REG_SZ, str(exe))
                else:
                    try:
                        winreg.DeleteValue(k, AUTORUN_NAME)
                    except FileNotFoundError:
                        pass
        except Exception as e:
            pass  # non-critical

    def _group(self, title: str) -> QGroupBox:
        g = QGroupBox(title)
        g.setStyleSheet(f"""
            QGroupBox {{
                color: {LAVENDER}; font-weight: bold; font-size: 9pt;
                border: 1px solid {BG3}; border-radius: 6px;
                margin-top: 8px; padding-top: 8px;
            }}
            QGroupBox::title {{ subcontrol-origin: margin; left: 8px; }}
        """)
        return g

    def _input_style(self) -> str:
        return f"""
            QLineEdit, QSpinBox {{
                background:{BG2}; color:{FG}; border:1px solid {BG3};
                border-radius:4px; padding:4px 8px;
            }}
            QLineEdit:focus, QSpinBox:focus {{ border-color:{BLUE}; }}
            QSpinBox::up-button, QSpinBox::down-button {{
                background:{BG3}; border:none; width:16px;
            }}
        """

    def _btn_style(self, color: str) -> str:
        return f"""
            QPushButton {{
                background:{BG3}; color:{color};
                border:1px solid {color}44; border-radius:5px; font-size:10pt;
            }}
            QPushButton:hover {{ background:{color}22; }}
        """
