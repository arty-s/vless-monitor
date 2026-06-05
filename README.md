# VLESS Monitor

Windows tray application for monitoring VLESS proxy connectivity and detecting ISP throttling / DPI blocking.

**[🇷🇺 Документация на русском →](README_RU.md)**

---

## Features

- **Traffic light tray icon** — green / yellow / red at a glance
- **Right-click menu** — status summary without opening any window
- **Detailed status window** — all checks grouped by category
- **Built-in xray-core** — uses the real VLESS+Reality protocol (downloaded automatically)
- **DPI detection** — detects RKN/ISP throttling heuristics:
  - 16 KB packet-count freeze (RKN's "25 packet rule")
  - Latency ratio spike (tunnel vs direct)
  - End-to-end tunnel beacon verification
- **ISP diagnosis** — distinguishes: VPS down / port blocked / DPI throttle / ISP upstream failure
- **Settings UI** — change VLESS URI, check interval, Telegram notifications, autostart
- **No Python required** — standalone `.exe`

## Quick Start (binary)

1. Download `VlessMonitor-windows-x64.zip` from [Releases](../../releases/latest)
2. Extract anywhere (e.g. `C:\Tools\VlessMonitor\`)
3. Run `VlessMonitor.exe`
4. On first launch: paste your VLESS link in Settings
5. On first launch: xray-core (~15 MB) is downloaded from GitHub automatically

## Quick Start (from source)

**Requirements:** Python 3.11+, Windows 10/11

```bat
git clone https://github.com/YOUR_USERNAME/vless-monitor
cd vless-monitor
pip install PyQt6 requests PySocks
python monitor.py
```

## Server-side probe (optional but recommended)

The optional probe server on your VPS enables **end-to-end tunnel verification** and **DPI stream tests**.

```bash
# On your VPS
scp server/probe_server.py root@YOUR_VPS:/opt/vless-monitor/
ssh root@YOUR_VPS

# Install as systemd service
cat > /etc/systemd/system/vless-probe.service << EOF
[Unit]
Description=VLESS Monitor Probe Server
After=network.target

[Service]
ExecStart=/usr/bin/python3 /opt/vless-monitor/probe_server.py --port 8765 --secret YOUR_SECRET
Restart=always

[Install]
WantedBy=multi-user.target
EOF

systemctl enable --now vless-probe
ufw allow 8765/tcp
```

Then set `probe_server_port` and `probe_secret` in the app's Settings.

## Check categories

| Category | Checks |
|---|---|
| **Pings** | VPS, Google DNS, Cloudflare, Yandex RU, Rostelecom RU |
| **Ports** | TCP to VLESS port, probe server port |
| **Tunnel** | Direct HTTP to VPS, E2E beacon via VLESS, HTTP via tunnel |
| **DPI** | 16 KB freeze test, latency ratio (direct vs tunnel) |
| **Stats** | xray traffic (↑↓ MB) |

## Status logic

| Tray color | Meaning |
|---|---|
| 🟢 Green | Everything OK |
| 🟡 Yellow | Tunnel slow / DPI throttling detected |
| 🔴 Red | VPS unreachable / VLESS port blocked / no internet |

## Build from source

```bat
build.bat
# Output: dist\VlessMonitor\VlessMonitor.exe
```

## License

MIT
