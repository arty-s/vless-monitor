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
    name = "Probe-сервер (прямое соединение)"
    url = f"http://{vps_ip}:{port}/health"
    t0 = time.perf_counter()
    try:
        r = requests.get(url, headers={"X-Token": secret}, timeout=timeout)
        ms = (time.perf_counter() - t0) * 1000
        if r.status_code == 200:
            return CheckResult(
                name=name, ok=True, value=round(ms, 1),
                message=f"{ms:.0f} мс",
                comment="Сервер отвечает напрямую — VPS живой и доступен",
                category="tunnel",
            )
        return CheckResult(
            name=name, ok=False, message=f"HTTP {r.status_code}",
            comment="Сервер ответил с ошибкой — что-то не так на стороне VPS",
            category="tunnel",
        )
    except requests.exceptions.ConnectionError:
        return CheckResult(
            name=name, ok=False, message="нет соединения",
            comment="Probe-сервер не запущен или порт 8765 не открыт на VPS",
            category="tunnel",
        )
    except requests.exceptions.Timeout:
        return CheckResult(
            name=name, ok=False, message="таймаут",
            comment="VPS не отвечает — возможно перегружен или недоступен",
            category="tunnel",
        )
    except Exception as e:
        return CheckResult(
            name=name, ok=False, message=str(e)[:50],
            comment="Не удалось подключиться к серверу",
            category="tunnel",
        )


def check_tunnel_e2e(vps_ip: str, port: int, secret: str,
                     proxy_url: str, timeout: float = 10.0) -> CheckResult:
    """
    Send beacon THROUGH VLESS, verify arrival DIRECTLY.
    This is the gold-standard check: confirms the tunnel carries real data.
    """
    name = "Туннель — сквозная проверка"
    bid = uuid.uuid4().hex
    base = f"http://{vps_ip}:{port}"

    t0 = time.perf_counter()
    try:
        sess = _session(proxy_url)
        r = sess.post(f"{base}/beacon/{bid}",
                      headers={"X-Token": secret}, timeout=timeout)
        ms = (time.perf_counter() - t0) * 1000
        if r.status_code != 200:
            return CheckResult(
                name=name, ok=False, message=f"HTTP {r.status_code}",
                comment="Туннель ответил ошибкой — xray запущен, но что-то не так",
                category="tunnel",
            )
    except requests.exceptions.ProxyError:
        return CheckResult(
            name=name, ok=False, message="ошибка прокси",
            comment="xray не запущен или SOCKS5 порт не отвечает — туннель сломан",
            category="tunnel",
        )
    except requests.exceptions.Timeout:
        return CheckResult(
            name=name, ok=False, message="таймаут",
            comment="Данные через туннель не дошли — возможно DPI блокирует VLESS",
            category="tunnel",
        )
    except Exception as e:
        return CheckResult(
            name=name, ok=False, message=str(e)[:50],
            comment="Туннель не работает — проверьте xray и VLESS-настройки",
            category="tunnel",
        )

    # Verify arrival directly
    try:
        r2 = requests.get(f"{base}/beacon/{bid}",
                          headers={"X-Token": secret}, timeout=5.0)
        arrived = r2.status_code == 200 and r2.json().get("received")
    except Exception:
        arrived = False

    if arrived:
        speed = "быстро" if ms < 200 else "нормально" if ms < 600 else "медленно"
        return CheckResult(
            name=name, ok=True, value=round(ms, 1),
            message=f"{ms:.0f} мс",
            comment=f"Туннель работает — данные проходят насквозь ({speed})",
            category="tunnel",
        )
    return CheckResult(
        name=name, ok=False, message="маяк не дошёл",
        comment="Запрос ушёл, но до сервера не добрался — DPI режет трафик на полпути",
        category="tunnel",
    )


def check_external_http(proxy_url: str, timeout: float = 8.0) -> CheckResult:
    """HTTP GET through tunnel to external URL — confirms internet works via VLESS."""
    name = "Интернет через туннель"
    t0 = time.perf_counter()
    try:
        sess = _session(proxy_url)
        r = sess.get("http://cp.cloudflare.com/", timeout=timeout)
        ms = (time.perf_counter() - t0) * 1000
        if r.status_code == 200:
            speed = "быстро" if ms < 300 else "нормально" if ms < 800 else "медленно"
            return CheckResult(
                name=name, ok=True, value=round(ms, 1),
                message=f"{ms:.0f} мс",
                comment=f"Интернет через туннель работает ({speed})",
                category="tunnel",
            )
        return CheckResult(
            name=name, ok=False, message=f"HTTP {r.status_code}",
            comment="Туннель работает, но сайт ответил ошибкой — временная проблема",
            category="tunnel",
        )
    except requests.exceptions.ProxyError:
        return CheckResult(
            name=name, ok=False, message="ошибка прокси",
            comment="xray не запущен — локальный SOCKS5 прокси не отвечает",
            category="tunnel",
        )
    except requests.exceptions.Timeout:
        return CheckResult(
            name=name, ok=False, message="таймаут",
            comment="Интернет через туннель не работает — возможно DPI блокирует VLESS",
            category="tunnel",
        )
    except Exception as e:
        return CheckResult(
            name=name, ok=False, message=str(e)[:50],
            comment="Не удалось выйти в интернет через туннель",
            category="tunnel",
        )


def check_dpi_freeze(vps_ip: str, port: int, secret: str,
                     proxy_url: str, probe_kb: int = 24) -> CheckResult:
    """
    RKN DPI 16 KB freeze detection.
    RKN's TSPU freezes connections after ~25 packets (~16 KB payload).
    """
    name = "DPI: тест на заморозку 16 КБ"
    t0 = time.perf_counter()
    try:
        sess = _session(proxy_url)
        resp = sess.get(
            f"http://{vps_ip}:{port}/stream/{probe_kb}",
            headers={"X-Token": secret},
            stream=True, timeout=15.0,
        )
        received = 0
        last_chunk_time = time.perf_counter()
        stall_at = None

        for chunk in resp.iter_content(chunk_size=1024):
            if chunk:
                received += len(chunk)
                now = time.perf_counter()
                if now - last_chunk_time > 4.0 and stall_at is None:
                    stall_at = received / 1024
                last_chunk_time = now

        total_sec = time.perf_counter() - t0 + 0.001
        received_kb = received / 1024

        if received_kb < 12:
            return CheckResult(
                name=name, ok=False, value=received_kb,
                message=f"стоп на {received_kb:.0f} КБ",
                comment=(
                    f"Обнаружена блокировка РКН — соединение обрывается на {received_kb:.0f} КБ. "
                    "Это признак правила «25 пакетов» систем ТСПУ"
                ),
                category="dpi",
            )
        if stall_at:
            return CheckResult(
                name=name, ok=False, value=stall_at,
                message=f"зависание на {stall_at:.0f} КБ",
                comment=(
                    f"Провайдер замедляет соединение после {stall_at:.0f} КБ. "
                    "Это throttling — не полная блокировка, но трафик искусственно тормозится"
                ),
                category="dpi",
            )

        kbps = received / total_sec / 1024
        return CheckResult(
            name=name, ok=True, value=round(kbps, 0),
            message=f"{received_kb:.0f} КБ @ {kbps:.0f} КБ/с",
            comment="Блокировки 16 КБ не обнаружено — данные проходят без обрывов",
            category="dpi",
        )

    except requests.exceptions.Timeout:
        return CheckResult(
            name=name, ok=False, message="таймаут",
            comment="Соединение зависло — возможно DPI заморозил передачу данных",
            category="dpi",
        )
    except Exception as e:
        return CheckResult(
            name=name, ok=False, message=str(e)[:50],
            comment="Не удалось выполнить тест — туннель недоступен",
            category="dpi",
        )


def check_latency_ratio(direct_ms: float | None,
                        tunnel_ms: float | None,
                        threshold: float = 4.0) -> CheckResult:
    name = "DPI: соотношение задержек"
    if direct_ms is None or tunnel_ms is None:
        return CheckResult(
            name=name, ok=False, message="нет данных",
            comment="Нет данных для сравнения — другие проверки не выполнились",
            category="dpi",
        )
    ratio = tunnel_ms / max(direct_ms, 1)
    if ratio > threshold:
        return CheckResult(
            name=name, ok=False, value=round(ratio, 1),
            message=f"{ratio:.1f}× (прямо {direct_ms:.0f} / туннель {tunnel_ms:.0f} мс)",
            comment=(
                f"Туннель в {ratio:.1f} раз медленнее прямого соединения — "
                "вероятно провайдер искусственно замедляет VLESS-трафик"
            ),
            category="dpi",
        )
    comment = "Туннель работает без замедлений — накладные расходы в норме"
    if ratio > threshold * 0.6:
        comment = "Небольшое замедление туннеля — пока в пределах нормы"
    return CheckResult(
        name=name, ok=True, value=round(ratio, 1),
        message=f"{ratio:.1f}× ({direct_ms:.0f} / {tunnel_ms:.0f} мс)",
        comment=comment,
        category="dpi",
    )


def check_xray_stats(vps_ip: str, port: int, secret: str) -> CheckResult:
    name = "Статистика xray"
    try:
        r = requests.get(f"http://{vps_ip}:{port}/xray/stats",
                         headers={"X-Token": secret}, timeout=5.0)
        if r.status_code == 200:
            d = r.json()
            up = d.get("uplink_mb", 0)
            dn = d.get("downlink_mb", 0)
            total = up + dn
            comment = "Счётчики трафика xray — суммарно с момента последнего перезапуска"
            if total == 0:
                comment = "Трафик пока не зафиксирован — возможно xray только запустился"
            return CheckResult(
                name=name, ok=True,
                message=f"↑{up:.1f}  ↓{dn:.1f} МБ",
                comment=comment,
                category="stats",
            )
        return CheckResult(
            name=name, ok=False, message=f"HTTP {r.status_code}",
            comment="Не удалось получить статистику с сервера",
            category="stats",
        )
    except Exception as e:
        return CheckResult(
            name=name, ok=False, message=str(e)[:50],
            comment="Probe-сервер недоступен — статистика недоступна",
            category="stats",
        )
