"""
System tray icon with traffic-light color and right-click context menu.
"""
from PyQt6.QtCore import QTimer, pyqtSignal, QObject
from PyQt6.QtGui import QIcon, QPixmap, QPainter, QColor, QBrush, QRadialGradient
from PyQt6.QtWidgets import QSystemTrayIcon, QMenu, QApplication

from checker import Status, MonitorState

COLORS = {
    Status.GREEN:   ("#a6e3a1", "#4ab444"),
    Status.YELLOW:  ("#f9e2af", "#d2a014"),
    Status.RED:     ("#f38ba8", "#c83250"),
    Status.UNKNOWN: ("#6c7086", "#3c4060"),
}

TOOLTIPS = {
    Status.GREEN:   "VLESS Monitor: ●  Всё OK",
    Status.YELLOW:  "VLESS Monitor: ◑  Есть проблемы",
    Status.RED:     "VLESS Monitor: ●  БЛОК / ОБРЫВ",
    Status.UNKNOWN: "VLESS Monitor: ○  Проверка...",
}


def _make_icon(status: Status, size: int = 64) -> QIcon:
    fill_hex, glow_hex = COLORS.get(status, COLORS[Status.UNKNOWN])
    pix = QPixmap(size, size)
    pix.fill(QColor(0, 0, 0, 0))
    painter = QPainter(pix)
    painter.setRenderHint(QPainter.RenderHint.Antialiasing)

    pad = size // 10
    # Outer glow
    glow = QRadialGradient(size / 2, size / 2, size / 2)
    glow.setColorAt(0.6, QColor(glow_hex + "80"))
    glow.setColorAt(1.0, QColor(glow_hex + "00"))
    painter.setBrush(QBrush(glow))
    painter.setPen(QColor(0, 0, 0, 0))
    painter.drawEllipse(0, 0, size, size)

    # Main circle
    painter.setBrush(QBrush(QColor(fill_hex)))
    painter.setPen(QColor(glow_hex))
    painter.drawEllipse(pad, pad, size - 2 * pad, size - 2 * pad)

    # Highlight (top-left white shine)
    shine = QRadialGradient(size * 0.38, size * 0.30, size * 0.20)
    shine.setColorAt(0.0, QColor(255, 255, 255, 140))
    shine.setColorAt(1.0, QColor(255, 255, 255, 0))
    painter.setBrush(QBrush(shine))
    painter.setPen(QColor(0, 0, 0, 0))
    painter.drawEllipse(pad, pad, size - 2 * pad, size - 2 * pad)

    painter.end()
    return QIcon(pix)


class TrayIcon(QSystemTrayIcon):
    open_requested = pyqtSignal()
    refresh_requested = pyqtSignal()
    settings_requested = pyqtSignal()
    exit_requested = pyqtSignal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self._status = Status.UNKNOWN
        self._last_state: MonitorState | None = None
        self._setup_menu()
        self.setIcon(_make_icon(Status.UNKNOWN))
        self.setToolTip(TOOLTIPS[Status.UNKNOWN])
        self.activated.connect(self._on_activated)

    def _setup_menu(self):
        menu = QMenu()
        menu.setStyleSheet("""
            QMenu {
                background: #1e1e2e; color: #cdd6f4;
                border: 1px solid #313244; font-family: 'Segoe UI'; font-size: 10pt;
                padding: 4px 0;
            }
            QMenu::item { padding: 6px 20px; }
            QMenu::item:selected { background: #313244; }
            QMenu::separator { background: #313244; height: 1px; margin: 4px 0; }
        """)

        self._action_status = menu.addAction("○  Инициализация...")
        self._action_status.setEnabled(False)

        menu.addSeparator()

        action_open = menu.addAction("📊  Открыть статусы")
        action_open.triggered.connect(self.open_requested)

        action_refresh = menu.addAction("⟳  Проверить сейчас")
        action_refresh.triggered.connect(self.refresh_requested)

        action_settings = menu.addAction("⚙  Настройки")
        action_settings.triggered.connect(self.settings_requested)

        menu.addSeparator()

        action_exit = menu.addAction("✕  Выход")
        action_exit.triggered.connect(self.exit_requested)

        self.setContextMenu(menu)

    def update_state(self, state: MonitorState):
        self._last_state = state
        if state.overall == self._status:
            self._refresh_menu_status(state)
            return
        self._status = state.overall
        self.setIcon(_make_icon(state.overall))
        self.setToolTip(TOOLTIPS.get(state.overall, "VLESS Monitor"))
        self._refresh_menu_status(state)

    def _refresh_menu_status(self, state: MonitorState):
        icons = {Status.GREEN: "●", Status.YELLOW: "◑",
                 Status.RED: "●", Status.UNKNOWN: "○"}
        sym = icons.get(state.overall, "○")
        short = state.diagnosis.split("\n")[0][:60]
        self._action_status.setText(f"{sym}  {short}")

    def _on_activated(self, reason):
        if reason == QSystemTrayIcon.ActivationReason.DoubleClick:
            self.open_requested.emit()
