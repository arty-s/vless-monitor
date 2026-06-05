"""
Main status window - detailed view of all checks with human-readable comments.
"""
import time

from PyQt6.QtCore import Qt, QTimer, pyqtSignal, QObject
from PyQt6.QtGui import QColor, QFont, QBrush
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel,
    QPushButton, QTableWidget, QTableWidgetItem,
    QHeaderView, QFrame, QSizePolicy, QAbstractItemView,
)
from checker import MonitorState, Status

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
    Status.GREEN:   "● Всё в порядке",
    Status.YELLOW:  "◑ Обнаружены проблемы",
    Status.RED:     "● Связь нарушена",
    Status.UNKNOWN: "○ Проверяю...",
}

CATEGORY_ORDER = ["ping", "port", "tunnel", "dpi", "stats", "general"]
CATEGORY_LABEL = {
    "ping":    "Пинги — доступность серверов",
    "port":    "Порты — открытые соединения",
    "tunnel":  "Туннель — работа VLESS",
    "dpi":     "DPI — поиск блокировок и замедлений",
    "stats":   "Статистика",
    "general": "Прочее",
}

# Two-line cell: big comment + small raw value
COMMENT_FONT = QFont("Segoe UI", 10)
VALUE_FONT   = QFont("Segoe UI", 8)


class _Signaller(QObject):
    update = pyqtSignal(object)


class _TwoLineItem(QTableWidgetItem):
    """Table item that stores comment + raw value for rendering."""
    def __init__(self, comment: str, raw: str, ok: bool):
        super().__init__(comment)
        self.comment = comment
        self.raw = raw
        self.ok = ok


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
        self.resize(820, 600)
        self.setMinimumSize(600, 420)
        self.setStyleSheet(f"QWidget {{ background-color: {BG}; color: {FG}; "
                           f"font-family: 'Segoe UI'; font-size: 10pt; }}")

        root = QVBoxLayout(self)
        root.setContentsMargins(14, 14, 14, 10)
        root.setSpacing(8)

        # ── Header ────────────────────────────────────────────
        hdr = QHBoxLayout()

        self._lbl_status = QLabel("○ Проверяю...")
        self._lbl_status.setFont(QFont("Segoe UI", 15, QFont.Weight.Bold))
        self._lbl_status.setStyleSheet(f"color: {FG_DIM};")
        hdr.addWidget(self._lbl_status)
        hdr.addStretch()

        self._lbl_time = QLabel("")
        self._lbl_time.setStyleSheet(f"color: {FG_DIM}; font-size: 9pt;")
        hdr.addWidget(self._lbl_time)

        btn_refresh = QPushButton("⟳  Обновить")
        btn_refresh.setFixedHeight(30)
        btn_refresh.setCursor(Qt.CursorShape.PointingHandCursor)
        btn_refresh.setStyleSheet(self._btn_style(BLUE))
        btn_refresh.clicked.connect(self._do_refresh)
        hdr.addWidget(btn_refresh)

        btn_settings = QPushButton("⚙  Настройки")
        btn_settings.setFixedHeight(30)
        btn_settings.setCursor(Qt.CursorShape.PointingHandCursor)
        btn_settings.setStyleSheet(self._btn_style(LAVENDER))
        btn_settings.clicked.connect(self._do_settings)
        hdr.addWidget(btn_settings)

        root.addLayout(hdr)

        line = QFrame()
        line.setFrameShape(QFrame.Shape.HLine)
        line.setStyleSheet(f"color: {BG3};")
        root.addWidget(line)

        # ── Table: 3 columns ──────────────────────────────────
        # Col 0: check name  |  Col 1: dot  |  Col 2: comment + raw value
        self._table = QTableWidget(0, 3)
        self._table.setHorizontalHeaderLabels(["Проверка", "", "Результат"])
        hh = self._table.horizontalHeader()
        hh.setSectionResizeMode(0, QHeaderView.ResizeMode.ResizeToContents)
        hh.setSectionResizeMode(1, QHeaderView.ResizeMode.Fixed)
        hh.setSectionResizeMode(2, QHeaderView.ResizeMode.Stretch)
        self._table.setColumnWidth(1, 34)
        self._table.verticalHeader().setVisible(False)
        self._table.setSelectionMode(QAbstractItemView.SelectionMode.NoSelection)
        self._table.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self._table.setShowGrid(False)
        self._table.setAlternatingRowColors(True)
        self._table.setStyleSheet(self._table_style())
        self._table.setItemDelegateForColumn(2, _CommentDelegate(self._table))
        root.addWidget(self._table, stretch=1)

        # ── Diagnosis footer ───────────────────────────────────
        line2 = QFrame()
        line2.setFrameShape(QFrame.Shape.HLine)
        line2.setStyleSheet(f"color: {BG3};")
        root.addWidget(line2)

        self._lbl_diag = QLabel("")
        self._lbl_diag.setWordWrap(True)
        self._lbl_diag.setFont(QFont("Segoe UI", 10))
        self._lbl_diag.setStyleSheet(f"color: {FG_DIM}; padding: 4px 2px;")
        root.addWidget(self._lbl_diag)

        self._refresh_timer = QTimer(self)
        self._refresh_timer.timeout.connect(self._tick_time)
        self._refresh_timer.start(1000)
        self._last_update: float = 0

    def update_state(self, state: MonitorState):
        self._sig.update.emit(state)

    def _apply_state(self, state: MonitorState):
        color = STATUS_COLOR.get(state.overall, FG_DIM)
        self._lbl_status.setText(STATUS_TEXT.get(state.overall, "○"))
        self._lbl_status.setStyleSheet(f"color: {color};")
        self._last_update = state.last_update

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
            self._table.setRowHeight(row, 42)

            # Col 0: check name
            item_name = QTableWidgetItem("  " + result.name)
            item_name.setForeground(QColor(FG))
            item_name.setFont(QFont("Segoe UI", 10))
            self._table.setItem(row, 0, item_name)

            # Col 1: status dot
            dot_color = GREEN if result.ok else RED
            item_dot = QTableWidgetItem("●")
            item_dot.setForeground(QColor(dot_color))
            item_dot.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
            item_dot.setFont(QFont("Segoe UI", 14))
            self._table.setItem(row, 1, item_dot)

            # Col 2: comment (big) + raw value (small, dim)
            comment = result.comment or result.message or "—"
            raw = result.message if result.comment else ""
            item_comment = _TwoLineItem(comment, raw, result.ok)
            self._table.setItem(row, 2, item_comment)

        diag_color = STATUS_COLOR.get(state.overall, FG_DIM)
        self._lbl_diag.setStyleSheet(f"color: {diag_color}; padding: 4px 2px;")
        self._lbl_diag.setText(state.diagnosis)

    def _add_section_row(self, label: str):
        row = self._table.rowCount()
        self._table.insertRow(row)
        self._table.setRowHeight(row, 24)
        item = QTableWidgetItem(f"  {label}")
        item.setForeground(QColor(LAVENDER))
        item.setFont(QFont("Segoe UI", 8, QFont.Weight.Bold))
        item.setBackground(QBrush(QColor(BG3)))
        self._table.setItem(row, 0, item)
        self._table.setSpan(row, 0, 1, 3)

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

    def _btn_style(self, color: str) -> str:
        return (f"QPushButton {{ background:{BG3}; color:{color}; "
                f"border:1px solid {color}44; border-radius:5px; padding:0 12px; }}"
                f"QPushButton:hover {{ background:{color}22; }}")

    def _table_style(self) -> str:
        return (f"QTableWidget {{ background:{BG}; alternate-background-color:{BG2}; "
                f"border:none; color:{FG}; font-size:10pt; }}"
                f"QHeaderView::section {{ background:{BG3}; color:{FG_DIM}; "
                f"border:none; padding:4px 8px; font-size:9pt; }}"
                f"QScrollBar:vertical {{ background:{BG2}; width:8px; border:none; }}"
                f"QScrollBar::handle:vertical {{ background:{BG3}; border-radius:4px; }}")


# ── Custom delegate: renders comment (big) + raw value (small dim) ────────────
from PyQt6.QtWidgets import QStyledItemDelegate, QStyle
from PyQt6.QtGui import QPainter, QPalette
from PyQt6.QtCore import QRect


class _CommentDelegate(QStyledItemDelegate):
    def paint(self, painter: QPainter, option, index):
        item = index.data(Qt.ItemDataRole.UserRole + 1)
        # Get our custom item via the model
        table = self.parent()
        cell = table.item(index.row(), index.column())
        if not isinstance(cell, _TwoLineItem):
            super().paint(painter, option, index)
            return

        painter.save()

        bg = QColor(BG2) if option.state & QStyle.StateFlag.State_Selected else \
             QColor(BG2 if index.row() % 2 else BG)
        painter.fillRect(option.rect, bg)

        ok_color = QColor(GREEN) if cell.ok else QColor(YELLOW)
        dim_color = QColor(FG_DIM)

        r = option.rect
        pad_x, pad_y = 8, 5

        # Big comment line
        painter.setFont(QFont("Segoe UI", 10))
        painter.setPen(ok_color)
        comment_rect = QRect(r.x() + pad_x, r.y() + pad_y,
                             r.width() - pad_x * 2, 22)
        painter.drawText(comment_rect,
                         Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter,
                         cell.comment)

        # Small raw value line
        if cell.raw:
            painter.setFont(QFont("Segoe UI", 8))
            painter.setPen(dim_color)
            raw_rect = QRect(r.x() + pad_x, r.y() + pad_y + 20,
                             r.width() - pad_x * 2, 16)
            painter.drawText(raw_rect,
                             Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter,
                             cell.raw)

        painter.restore()

    def sizeHint(self, option, index):
        return __import__('PyQt6.QtCore', fromlist=['QSize']).QSize(200, 42)
