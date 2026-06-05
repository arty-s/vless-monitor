"""
Orchestrates all checks periodically, computes overall status, notifies callbacks.
"""
import logging
import threading
import time
from dataclasses import dataclass, field
from enum import Enum
from typing import Callable, Optional

from checks.connectivity import CheckResult, ping_host, tcp_probe
from checks.tunnel import (
    check_http_direct, check_tunnel_e2e, check_external_http,
    check_dpi_freeze, check_latency_ratio, check_xray_stats,
)

log = logging.getLogger(__name__)


class Status(Enum):
    UNKNOWN = "unknown"
    GREEN = "green"
    YELLOW = "yellow"
    RED = "red"


@dataclass
class MonitorState:
    checks: dict[str, CheckResult] = field(default_factory=dict)
    overall: Status = Status.UNKNOWN
    diagnosis: str = "Инициализация..."
    last_update: float = 0.0


class Checker:
    def __init__(self, config: dict,
                 on_update: Optional[Callable[[MonitorState], None]] = None):
        self.cfg = config
        self.on_update = on_update
        self._state = MonitorState()
        self._lock = threading.Lock()
        self._stop = threading.Event()
        self._thread: Optional[threading.Thread] = None
        self._cycle = 0

    def start(self):
        self._stop.clear()
        self._thread = threading.Thread(target=self._loop, daemon=True,
                                        name="CheckerThread")
        self._thread.start()

    def stop(self):
        self._stop.set()

    def run_now(self):
        threading.Thread(target=self._run_all, daemon=True).start()

    def get_state(self) -> MonitorState:
        with self._lock:
            return self._state

    def _loop(self):
        self._run_all()
        interval = self.cfg.get("check_interval_sec", 30)
        while not self._stop.wait(interval):
            self._run_all()

    def _run_all(self):
        cfg = self.cfg
        vps = cfg["vps_ip"] if "vps_ip" in cfg else _extract_host(cfg.get("vless_uri", ""))
        probe_port = cfg.get("probe_server_port", 8765)
        secret = cfg.get("probe_secret", "")
        proxy = f"socks5://127.0.0.1:{cfg.get('local_socks5_port', 10808)}"
        vless_port = _extract_port(cfg.get("vless_uri", ""))
        ratio_thr = cfg.get("latency_ratio_threshold", 4.0)
        dpi_kb = cfg.get("dpi_probe_size_kb", 24)
        self._cycle += 1

        results: dict[str, CheckResult] = {}

        # --- Run pings + TCP concurrently ---
        ping_results: list[CheckResult] = []
        tcp_results: list[CheckResult] = []

        def _do_pings():
            for label, ip in cfg.get("ping_targets", {}).items():
                ping_results.append(ping_host(ip, label))

        def _do_tcp():
            tcp_results.append(tcp_probe(vps, vless_port))
            # Also probe probe-server port
            tcp_results.append(tcp_probe(vps, probe_port))

        t1 = threading.Thread(target=_do_pings, daemon=True)
        t2 = threading.Thread(target=_do_tcp, daemon=True)
        t1.start(); t2.start()
        t1.join(timeout=20); t2.join(timeout=20)

        for r in ping_results:
            results[r.name] = r
        for r in tcp_results:
            results[r.name] = r

        # --- Probe server (direct HTTP) ---
        r_direct = check_http_direct(vps, probe_port, secret)
        results[r_direct.name] = r_direct

        # --- Tunnel checks ---
        r_ext = check_external_http(proxy)
        results[r_ext.name] = r_ext

        if r_direct.ok:
            r_e2e = check_tunnel_e2e(vps, probe_port, secret, proxy)
            results[r_e2e.name] = r_e2e

            r_ratio = check_latency_ratio(
                r_direct.value,
                r_e2e.value if r_e2e.ok else None,
                ratio_thr
            )
            results[r_ratio.name] = r_ratio

            # DPI freeze test - every 3rd cycle (it's heavier)
            if self._cycle % 3 == 1:
                r_dpi = check_dpi_freeze(vps, probe_port, secret, proxy, dpi_kb)
                results[r_dpi.name] = r_dpi

            r_stats = check_xray_stats(vps, probe_port, secret)
            results[r_stats.name] = r_stats

        overall, diagnosis = self._classify(results, vps)

        with self._lock:
            self._state = MonitorState(
                checks=results,
                overall=overall,
                diagnosis=diagnosis,
                last_update=time.time(),
            )

        if self.on_update:
            try:
                self.on_update(self._state)
            except Exception:
                pass

    def _classify(self, results: dict[str, CheckResult],
                  vps: str) -> tuple[Status, str]:
        def get(name): return results.get(name)

        vps_ping = get(f"VPS ({vps})")
        vps_up = vps_ping.ok if vps_ping else False

        direct = get("Probe сервер (прямо)")
        direct_ok = direct.ok if direct else False

        tunnel = get("Туннель E2E")
        tunnel_ok = tunnel.ok if tunnel else None

        ext = get("HTTP через туннель (external)")
        ext_ok = ext.ok if ext else None

        dpi = get("DPI: 16KB freeze тест")
        dpi_ok = dpi.ok if dpi else True

        ratio = get("Задержка: коэффициент")
        ratio_ok = ratio.ok if ratio else True

        ru_ping = get("Яндекс RU (77.88.8.8)")
        intl_ping = get("Google DNS (8.8.8.8)")
        ru_ok = ru_ping.ok if ru_ping else True
        intl_ok = intl_ping.ok if intl_ping else True

        issues = []
        status = Status.GREEN

        # --- RED ---
        if not intl_ok and not ru_ok:
            return Status.RED, "Нет интернета (ни RU ни international IP не пингуются)"

        if not intl_ok and ru_ok:
            return Status.RED, ("Провайдер не видит международные IP\n"
                                "Российские IP работают — возможно upstream провайдера упал")

        if not vps_up and not direct_ok:
            return Status.RED, f"VPS недоступен: нет ни ping ни HTTP на {vps}"

        vless_port = _extract_port(self.cfg.get("vless_uri", ""))
        vless_tcp = get(f"TCP {vps}:{vless_port}")
        if vless_tcp and not vless_tcp.ok:
            status = Status.RED
            issues.append(f"Порт VLESS {vless_port} закрыт — блокировка порта")

        # --- YELLOW ---
        if status == Status.GREEN:
            if ext_ok is False and (vps_up or direct_ok):
                status = Status.YELLOW
                if tunnel_ok is False:
                    issues.append("Туннель не работает (VPS доступен напрямую) — DPI режет VLESS")
                else:
                    issues.append("Интернет через туннель недоступен")

            if not dpi_ok:
                status = Status.YELLOW
                issues.append(f"Обнаружен DPI throttling: {dpi.message if dpi else ''}")

            if not ratio_ok:
                status = Status.YELLOW
                issues.append(f"Замедление туннеля: {ratio.message if ratio else ''}")

        if status == Status.GREEN:
            return Status.GREEN, "Всё в порядке. VLESS работает нормально."

        return status, "\n".join(issues)


def _extract_host(uri: str) -> str:
    import urllib.parse
    try:
        return urllib.parse.urlparse(uri).hostname or "185.121.12.210"
    except Exception:
        return "185.121.12.210"


def _extract_port(uri: str) -> int:
    import urllib.parse
    try:
        return urllib.parse.urlparse(uri).port or 443
    except Exception:
        return 443
