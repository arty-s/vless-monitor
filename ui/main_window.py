"""
Main status window - detailed view of all checks.
"""
from PyQt6.QtCore import Qt, QTimer, pyqtSignal, QObject
from PyQt6.QtGui import QColor, QFont, QIcon, QPalette
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel,
    QPushButton, QTableWidget, QTableWidgetItem,
    QHeaderView, QFrame, QSizePolicy,
)
import time
from checker import MonitorState, Status

# Dark theme palette
BG       = "#1e1e2e"
BG2      = "#2a2a3e"
BG3      = "#313244"
FG       = "#cdd6f4"
FG_DIM   = "#6c7086"
GREEN    = "#a6e3a1"
YELLOW   = "#f9e2af"
RED      = "#f38ba8"
BLUE     = "#89b4fa"
LAVENDER = "#b4befe"

STATUS_COLOR = {
    Status.GREEN:   GREEN,
    Status.YELLOW:  YELLOW,
    Status.RED:     RED,
    Status.UNKNOWN: FG_DIM,
}
STATUS_TEXT = {
    Status.GREEN:   "● ХОРОШО",
    Status.YELLOW:  "◑ ЕСТЬ ПРОБЛЕМЫ",
    Status.RED:     "● БЛОК / ОБРЫВ",
    Status.UNKNOWN: "○ Проверка...",
}

CATEGORY_ORDER = ["ping", "port", "tunnel", "dpi", "stats", "general"]
CATEGORY_LABEL = {
    "ping": "Пинги",
    "port": "Порты",
    "tunnel": "Туннель",
    "dpi": "DPI / Замедление",
    "stats": "Статистика",
    "general": "Прочее",
}


class _Signaller(QObject):
    update = pyqtSignal(object)


class MainWindow(QWidget):
    def __init__(self, on_refresh=None, on_settings=None):
        super().__init__()
        self._on_refresh = on_refresh
        self._on_settings = on_settings
        self._sig = _Signaller()
        self._sig.update.connect(self._apply_state)
        self._setup_ui()

    def _setup_ui(self):
        self.setWindowTitle("VLESS Monitor")
        self.resize(780, 580)
        self.setMinimumSize(560, 380)
        self._apply_dark_theme()

        root = QVBoxLayout(self)
        root.setContentsMargins(12, 12, 12, 12)
        root.setSpacing(8)

        # ── Header ────────────────────────────────────────
        hdr = QHBoxLayout()
        self._lbl_status = QLabel("○ Инициализация...")
        self._lbl_status.setFont(QFont("Segoe UI", 16, QFont.Weight.Bold))
        self._lbl_status.setStyleSheet(f"color: {FG_DIM};")
        hdr.addWidget(self._lbl_status)
        hdr.addStretch()

        self._lbl_time = QLabel("")
        self._lbl_time.setStyleSheet(f"color: {FG_DIM}; font-size: 10px;")
        hdr.addWidget(self._lbl_time)

        btn_refresh = QPushButton("⟳ Обновить")
        btn_refresh.setFixedHeight(30)
        btn_refresh.setCursor(Qt.CursorShape.PointingHandCursor)
        btn_refresh.setStyleSheet(self._btn_style(BLUE))
        btn_refresh.clicked.connect(self._do_refresh)
        hdr.addWidget(btn_refresh)

        btn_settings = QPushButton("⚙ Настройки")
        btn_settings.setFixedHeight(30)
        btn_settings.setCursor(Qt.CursorShape.PointingHandCursor)
        btn_settings.setStyleSheet(self._btn_style(LAVENDER))
        btn_settings.clicked.connect(self._do_settings)
        hdr.addWidget(btn_settings)

        root.addLayout(hdr)

        # ── Separator ─────────────────────────────────────
        line = QFrame()
        line.setFrameShape(QFrame.Shape.HLine)
        line.setStyleSheet(f"color: {BG3};")
        root.addWidget(line)

        # ── Table ─────────────────────────────────────────
        self._table = QTableWidget(0, 4)
        self._table.setHorizontalHeaderLabels(["Проверка", "Статус", "Значение", "Обновлено"])
        self._table.horizontalHeader().setSectionResizeMode(0, QHeaderView.ResizeMode.Stretch)
        self._table.horizontalHeader().setSectionResizeMode(1, QHeaderView.ResizeMode.Fixed)
        self._table.horizontalHeader().setSectionResizeMode(2, QHeaderView.ResizeMode.Fixed)
        self._table.horizontalHeader().setSectionResizeMode(3, QHeaderView.ResizeMode.Fixed)
        self._table.setColumnWidth(1, 80)
        self._table.setColumnWidth(2, 200)
        self._table.setColumnWidth(3, 90)
        self._table.verticalHeader().setVisible(False)
        self._table.setSelectionMode(QTableWidget.SelectionMode.NoSelection)
        self._table.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self._table.setShowGrid(False)
        self._table.setAlternatingRowColors(True)
        self._table.setStyleSheet(self._table_style())
        root.addWidget(self._table, stretch=1)

        # ── Diagnosis footer ───────────────────────────────
        line2 = QFrame()
        line2.setFrameShape(QFrame.Shape.HLine)
        line2.setStyleSheet(f"color: {BG3};")
        root.addWidget(line2)

        self._lbl_diag = QLabel("")
        self._lbl_diag.setWordWrap(True)
        self._lbl_diag.setStyleSheet(f"color: {FG_DIM}; font-size: 10px; padding: 4px 0;")
        root.addWidget(self._lbl_diag)

        # Refresh timer for "updated X seconds ago"
        self._refresh_timer = QTimer(self)
        self._refresh_timer.timeout.connect(self._tick_time)
        self._refresh_timer.start(1000)
        self._last_update: float = 0

    def update_state(self, state: MonitorState):
        """Thread-safe: called from checker thread."""
        self._sig.update.emit(state)

    def _apply_state(self, state: MonitorState):
        color = STATUS_COLOR.get(state.overall, FG_DIM)
        text = STATUS_TEXT.get(state.overall, "○")
        self._lbl_status.setText(text)
        self._lbl_status.setStyleSheet(f"color: {color};")
        self._last_update = state.last_update

        # Sort checks by category then name
        sorted_checks = sorted(
            state.checks.values(),
            key=lambda r: (CATEGORY_ORDER.index(r.category)
                           if r.category in CATEGORY_ORDER else 99, r.name)
        )

        self._table.setRowCount(0)
        current_cat = None
        for result in sorted_checks:
            if result.category != current_cat:
                current_cat = result.category
                self._add_section_row(CATEGORY_LABEL.get(current_cat, current_cat))

            row = self._table.rowCount()
            self._table.insertRow(row)

            # Name
            item_name = QTableWidgetItem("  " + result.name)
            item_name.setForeground(QColor(FG))
            self._table.setItem(row, 0, item_name)

            # Status dot
            dot = "●" if result.ok else "●"
            dot_color = GREEN if result.ok else RED
            item_dot = QTableWidgetItem(dot)
            item_dot.setForeground(QColor(dot_color))
            item_dot.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
            self._table.setItem(row, 1, item_dot)

            # Value / message
            val = result.message or (f"{result.value:.1f} мс" if result.value else "—")
            item_val = QTableWidgetItem(val)
            item_val.setForeground(QColor(GREEN if result.ok else YELLOW))
            self._table.setItem(row, 2, item_val)

            # Age
            age = int(time.time() - result.timestamp)
            age_str = f"{age}с" if age < 60 else f"{age//60}м"
            item_age = QTableWidgetItem(age_str)
            item_age.setForeground(QColor(FG_DIM))
            item_age.setTextAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
            self._table.setItem(row, 3, item_age)

        diag_color = STATUS_COLOR.get(state.overall, FG_DIM)
        self._lbl_diag.setStyleSheet(f"color: {diag_color}; font-size: 10px; padding: 4px 0;")
        self._lbl_diag.setText(state.diagnosis)

    def _add_section_row(self, label: str):
        row = self._table.rowCount()
        self._table.insertRow(row)
        item = QTableWidgetItem(f" {label}")
        item.setForeground(QColor(LAVENDER))
        item.setFont(QFont("Segoe UI", 9, QFont.Weight.Bold))
        self._table.setItem(row, 0, item)
        self._table.setSpan(row, 0, 1, 4)
        self._table.setRowHeight(row, 22)

    def _tick_time(self):
        if self._last_update:
            ago = int(time.time() - self._last_update)
            self._lbl_time.setText(f"обновлено {ago}с назад  ")

    def _do_refresh(self):
        if self._on_refresh:
            self._on_refresh()

    def _do_settings(self):
        if self._on_settings:
            self._on_settings()

    def _apply_dark_theme(self):
        self.setStyleSheet(f"""
            QWidget {{ background-color: {BG}; color: {FG}; font-family: 'Segoe UI'; font-size: 10pt; }}
        """)

    def _btn_style(self, color: str) -> str:
        return f"""
            QPushButton {{
                background-color: {BG3}; color: {color};
                border: 1px solid {color}44; border-radius: 5px;
                padding: 0 12px; font-size: 10pt;
            }}
            QPushButton:hover {{ background-color: {color}22; }}
            QPushButton:pressed {{ background-color: {color}44; }}
        """

    def _table_style(self) -> str:
        return f"""
            QTableWidget {{
                background-color: {BG}; alternate-background-color: {BG2};
                border: none; gridline-color: {BG3};
                color: {FG}; font-size: 10pt;
            }}
            QHeaderView::section {{
                background-color: {BG3}; color: {FG_DIM};
                border: none; padding: 4px 8px; font-size: 9pt;
            }}
            QScrollBar:vertical {{
                background: {BG2}; width: 8px; border: none;
            }}
            QScrollBar::handle:vertical {{
                background: {BG3}; border-radius: 4px;
            }}
        """
