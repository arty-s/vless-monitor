"""
First-run dialog: download xray-core.
"""
from PyQt6.QtCore import Qt, QThread, pyqtSignal
from PyQt6.QtGui import QFont
from PyQt6.QtWidgets import (
    QDialog, QVBoxLayout, QLabel, QProgressBar, QPushButton
)
from xray_manager import download_xray

BG = "#1e1e2e"; FG = "#cdd6f4"; BG3 = "#313244"
GREEN = "#a6e3a1"; BLUE = "#89b4fa"; RED = "#f38ba8"


class _DownloadThread(QThread):
    progress = pyqtSignal(str)
    done = pyqtSignal(bool)

    def run(self):
        ok = download_xray(progress_cb=lambda msg: self.progress.emit(msg))
        self.done.emit(ok)


class DownloadDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("VLESS Monitor — первый запуск")
        self.setFixedSize(420, 200)
        self.setModal(True)
        self.setStyleSheet(f"QWidget {{ background:{BG}; color:{FG}; font-family:'Segoe UI'; }}")

        lay = QVBoxLayout(self)
        lay.setContentsMargins(20, 20, 20, 20)
        lay.setSpacing(12)

        lbl = QLabel("Для работы необходим xray-core.\nСкачать сейчас с GitHub (~15 МБ)?")
        lbl.setFont(QFont("Segoe UI", 11))
        lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
        lay.addWidget(lbl)

        self._progress = QProgressBar()
        self._progress.setRange(0, 0)
        self._progress.setVisible(False)
        self._progress.setStyleSheet(f"""
            QProgressBar {{ background:{BG3}; border:none; border-radius:4px; height:8px; }}
            QProgressBar::chunk {{ background:{BLUE}; border-radius:4px; }}
        """)
        lay.addWidget(self._progress)

        self._status_lbl = QLabel("")
        self._status_lbl.setStyleSheet(f"color:{BLUE}; font-size:9pt;")
        self._status_lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
        lay.addWidget(self._status_lbl)

        lay.addStretch()

        btn_row_lay = QVBoxLayout()
        self._btn_download = QPushButton("Скачать xray-core")
        self._btn_download.setFixedHeight(36)
        self._btn_download.setStyleSheet(f"""
            QPushButton {{ background:{BG3}; color:{GREEN}; border:1px solid {GREEN}44;
                          border-radius:5px; font-size:11pt; }}
            QPushButton:hover {{ background:{GREEN}22; }}
        """)
        self._btn_download.clicked.connect(self._start_download)
        btn_row_lay.addWidget(self._btn_download)
        lay.addLayout(btn_row_lay)

        self._thread: _DownloadThread | None = None
        self.success = False

    def _start_download(self):
        self._btn_download.setEnabled(False)
        self._progress.setVisible(True)
        self._thread = _DownloadThread()
        self._thread.progress.connect(self._on_progress)
        self._thread.done.connect(self._on_done)
        self._thread.start()

    def _on_progress(self, msg: str):
        self._status_lbl.setText(msg)

    def _on_done(self, ok: bool):
        self.success = ok
        if ok:
            self._status_lbl.setText("✓ Готово!")
            self._status_lbl.setStyleSheet(f"color:{GREEN}; font-size:9pt;")
        else:
            self._status_lbl.setText("Ошибка скачивания. Проверьте интернет.")
            self._status_lbl.setStyleSheet(f"color:{RED}; font-size:9pt;")
            self._btn_download.setEnabled(True)
        self._progress.setVisible(False)
        if ok:
            QThread.msleep(800)
            self.accept()
