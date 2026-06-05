"""
Tunnel checks through local VLESS proxy and DPI detection heuristics.
"""
import time
import uuid
import requests
from checks.connectivity import CheckResult


def _session(proxy_url: str | None) -> requests.Session:
    s = requests.Session()
    if proxy_url:
        s.proxies = {"http": proxy_url, "https": proxy_url}
    s.headers["User-Agent"] = (
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
        "AppleWebKit/537.36 Chrome/124.0.0.0 Safari/537.36"
    )
    return s


def check_http_direct(vps_ip: str, port: int, secret: str,
                      timeout: float = 5.0) -> CheckResult:
    name = "Probe сервер (прямо)"
    url = f"http://{vps_ip}:{port}/health"
    t0 = time.perf_counter()
    try:
        r = requests.get(url, headers={"X-Token": secret}, timeout=timeout)
        ms = (time.perf_counter() - t0) * 1000
        if r.status_code == 200:
            return CheckResult(name=name, ok=True, value=round(ms, 1),
                               message=f"{ms:.0f} мс", category="tunnel")
        return CheckResult(name=name, ok=False,
                           message=f"HTTP {r.status_code}", category="tunnel")
    except requests.exceptions.ConnectionError:
        return CheckResult(name=name, ok=False,
                           message="Отказ соединения", category="tunnel")
    except requests.exceptions.Timeout:
        return CheckResult(name=name, ok=False,
                           message="Таймаут", category="tunnel")
    except Exception as e:
        return CheckResult(name=name, ok=False,
                           message=str(e)[:60], category="tunnel")


def check_tunnel_e2e(vps_ip: str, port: int, secret: str,
                     proxy_url: str, timeout: float = 10.0) -> CheckResult:
    """
    Send beacon THROUGH VLESS, verify arrival DIRECTLY.
    Confirms the tunnel is end-to-end functional.
    """
    name = "Туннель E2E"
    bid = uuid.uuid4().hex
    base = f"http://{vps_ip}:{port}"

    t0 = time.perf_counter()
    try:
        sess = _session(proxy_url)
        r = sess.post(f"{base}/beacon/{bid}",
                      headers={"X-Token": secret}, timeout=timeout)
        ms = (time.perf_counter() - t0) * 1000
        if r.status_code != 200:
            return CheckResult(name=name, ok=False,
                               message=f"Прокси ответил HTTP {r.status_code}",
                               category="tunnel")
    except requests.exceptions.ProxyError as e:
        return CheckResult(name=name, ok=False,
                           message=f"Ошибка прокси: {str(e)[:50]}",
                           category="tunnel")
    except requests.exceptions.Timeout:
        return CheckResult(name=name, ok=False,
                           message="Таймаут через туннель", category="tunnel")
    except Exception as e:
        return CheckResult(name=name, ok=False,
                           message=str(e)[:60], category="tunnel")

    # Verify arrival directly
    try:
        r2 = requests.get(f"{base}/beacon/{bid}",
                          headers={"X-Token": secret}, timeout=5.0)
        arrived = r2.status_code == 200 and r2.json().get("received")
    except Exception:
        arrived = False

    if arrived:
        return CheckResult(name=name, ok=True, value=round(ms, 1),
                           message=f"OK {ms:.0f} мс", category="tunnel")
    return CheckResult(name=name, ok=False,
                       message="Маяк не дошёл до сервера", category="tunnel")


def check_external_http(proxy_url: str, timeout: float = 8.0) -> CheckResult:
    """HTTP GET to external URL through tunnel - confirms internet access."""
    name = "HTTP через туннель (external)"
    t0 = time.perf_counter()
    try:
        sess = _session(proxy_url)
        r = sess.get("http://cp.cloudflare.com/", timeout=timeout)
        ms = (time.perf_counter() - t0) * 1000
        ok = r.status_code == 200
        return CheckResult(name=name, ok=ok, value=round(ms, 1),
                           message=f"{ms:.0f} мс" if ok else f"HTTP {r.status_code}",
                           category="tunnel")
    except requests.exceptions.ProxyError as e:
        return CheckResult(name=name, ok=False,
                           message=f"Ошибка прокси: {str(e)[:50]}", category="tunnel")
    except requests.exceptions.Timeout:
        return CheckResult(name=name, ok=False,
                           message="Таймаут", category="tunnel")
    except Exception as e:
        return CheckResult(name=name, ok=False,
                           message=str(e)[:60], category="tunnel")


def check_dpi_freeze(vps_ip: str, port: int, secret: str,
                     proxy_url: str, probe_kb: int = 24) -> CheckResult:
    """
    RKN DPI 16KB freeze detection.
    Downloads probe_kb through VLESS and checks for stalls after ~16KB.
    """
    name = "DPI: 16KB freeze тест"
    t0 = time.perf_counter()
    try:
        sess = _session(proxy_url)
        resp = sess.get(
            f"http://{vps_ip}:{port}/stream/{probe_kb}",
            headers={"X-Token": secret},
            stream=True, timeout=15.0
        )
        received = 0
        last_chunk_time = time.perf_counter()
        stall_at = None

        for chunk in resp.iter_content(chunk_size=1024):
            if chunk:
                received += len(chunk)
                now = time.perf_counter()
                gap = now - last_chunk_time
                if gap > 4.0 and stall_at is None:
                    stall_at = received / 1024
                last_chunk_time = now

        total_ms = (time.perf_counter() - t0) * 1000
        received_kb = received / 1024

        if received_kb < 12:
            return CheckResult(name=name, ok=False, value=received_kb,
                               message=f"Стоп на {received_kb:.0f} КБ (DPI 16KB?)",
                               category="dpi")
        if stall_at:
            return CheckResult(name=name, ok=False, value=stall_at,
                               message=f"Зависание на {stall_at:.0f} КБ — throttling",
                               category="dpi")

        kbps = received / (total_ms / 1000 + 0.001) / 1024
        return CheckResult(name=name, ok=True, value=round(kbps, 0),
                           message=f"OK {received_kb:.0f} КБ @ {kbps:.0f} КБ/с",
                           category="dpi")

    except requests.exceptions.Timeout:
        return CheckResult(name=name, ok=False,
                           message="Таймаут — возможно freeze", category="dpi")
    except Exception as e:
        return CheckResult(name=name, ok=False,
                           message=str(e)[:60], category="dpi")


def check_latency_ratio(direct_ms: float | None,
                        tunnel_ms: float | None,
                        threshold: float = 4.0) -> CheckResult:
    name = "Задержка: коэффициент"
    if direct_ms is None or tunnel_ms is None:
        return CheckResult(name=name, ok=False,
                           message="Нет данных", category="dpi")
    ratio = tunnel_ms / max(direct_ms, 1)
    if ratio > threshold:
        return CheckResult(name=name, ok=False, value=round(ratio, 1),
                           message=f"{ratio:.1f}× (прямо {direct_ms:.0f} мс, туннель {tunnel_ms:.0f} мс) — замедление",
                           category="dpi")
    return CheckResult(name=name, ok=True, value=round(ratio, 1),
                       message=f"OK {ratio:.1f}× накладные расходы",
                       category="dpi")


def check_xray_stats(vps_ip: str, port: int, secret: str) -> CheckResult:
    name = "Xray трафик"
    try:
        r = requests.get(f"http://{vps_ip}:{port}/xray/stats",
                         headers={"X-Token": secret}, timeout=5.0)
        if r.status_code == 200:
            d = r.json()
            up = d.get("uplink_mb", 0)
            dn = d.get("downlink_mb", 0)
            return CheckResult(name=name, ok=True,
                               message=f"↑{up:.1f} МБ  ↓{dn:.1f} МБ",
                               category="stats")
        return CheckResult(name=name, ok=False,
                           message=f"HTTP {r.status_code}", category="stats")
    except Exception as e:
        return CheckResult(name=name, ok=False,
                           message=str(e)[:50], category="stats")
