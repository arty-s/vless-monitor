"""Direct connectivity checks: ICMP ping and TCP port probes."""
import re
import socket
import subprocess
import time
from dataclasses import dataclass, field
from typing import Optional


@dataclass
class CheckResult:
    name: str
    ok: bool
    value: Optional[float] = None   # latency ms
    message: str = ""
    timestamp: float = field(default_factory=time.time)
    category: str = "general"      # ping | port | tunnel | dpi | stats


def tcp_probe(host: str, port: int, timeout: float = 5.0) -> CheckResult:
    name = f"TCP {host}:{port}"
    t0 = time.perf_counter()
    try:
        with socket.create_connection((host, port), timeout=timeout):
            ms = (time.perf_counter() - t0) * 1000
            return CheckResult(name=name, ok=True, value=round(ms, 1),
                               message=f"{ms:.0f} мс", category="port")
    except OSError as e:
        return CheckResult(name=name, ok=False,
                           message=str(e)[:60], category="port")


def ping_host(host: str, label: str) -> CheckResult:
    name = label
    try:
        result = subprocess.run(
            ["ping", "-n", "3", "-w", "2000", host],
            capture_output=True, text=True, timeout=15,
            creationflags=subprocess.CREATE_NO_WINDOW,
        )
        output = result.stdout
        avg_m = re.search(r"Average\s*=\s*(\d+)ms", output)
        loss_m = re.search(r"\((\d+)%\s*loss\)", output)

        loss = int(loss_m.group(1)) if loss_m else 100
        if avg_m:
            ms = int(avg_m.group(1))
            ok = loss < 80
            msg = f"{ms} мс" + (f", {loss}% потерь" if loss > 0 else "")
            return CheckResult(name=name, ok=ok, value=float(ms),
                               message=msg, category="ping")
        return CheckResult(name=name, ok=False,
                           message=f"{loss}% потерь / недоступен", category="ping")
    except Exception as e:
        return CheckResult(name=name, ok=False,
                           message=str(e)[:50], category="ping")
