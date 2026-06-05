# VLESS Monitor

Native Windows tray application for monitoring VLESS proxy connectivity and detecting ISP throttling / DPI blocking.

**Stack:** C# / .NET 8 + WinForms. A single self-contained `.exe` — no Python or .NET install required.

**[🇷🇺 Документация на русском →](README_RU.md)**

---

## Features

- **Traffic-light tray icon** — green / yellow / red at a glance
- **Right-click menu** — status summary without opening any window
- **Detailed status window** — every check with a plain-language comment
- **Built-in xray-core** — real VLESS+Reality protocol (downloaded automatically)
- **DPI detection** — RKN/ISP throttling heuristics:
  - 16 KB packet-count freeze (RKN's "25 packet rule")
  - Latency ratio spike (tunnel vs direct)
  - End-to-end tunnel beacon verification
- **ISP diagnosis** — distinguishes VPS down / port blocked / DPI throttle / upstream failure
- **Settings UI** — VLESS URI, interval, Telegram notifications, autostart
- **Single exe** — nothing to install

## Quick Start (binary)

1. Download `VlessMonitor-windows-x64.zip` from [Releases](../../releases/latest)
2. Extract anywhere (e.g. `C:\Tools\VlessMonitor\`)
3. Run `VlessMonitor.exe`
4. Paste your VLESS link in Settings
5. xray-core (~15 MB) downloads automatically on first launch

## Build from source

**Requirements:** [.NET 8 SDK](https://dotnet.microsoft.com/download), Windows 10/11

```bat
git clone https://github.com/arty-s/vless-monitor
cd vless-monitor\client
dotnet publish -c Release -o publish
:: Output: client\publish\VlessMonitor.exe
```

```
client/   ← Windows client (C# / .NET 8)
server/   ← VPS probe server (Python)
```

## Server-side probe (optional)

See [README_RU.md](README_RU.md#серверная-часть-probe-server-опционально) for the systemd setup. The probe server enables end-to-end tunnel verification and DPI stream tests.

## Status logic

| Tray color | Meaning |
|---|---|
| 🟢 Green | Everything OK |
| 🟡 Yellow | Tunnel slow / DPI throttling detected |
| 🔴 Red | VPS unreachable / VLESS port blocked / no internet |

## License

MIT
