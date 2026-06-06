# VLESS Monitor

Нативное приложение в трее Windows для мониторинга VLESS-прокси и детектирования замедлений / блокировок DPI.

**Стек:** C# / .NET 8 + WinForms. Один самодостаточный `.exe` со встроенным xray-core — не требует установки Python или .NET и ничего не качает при запуске.

---

## Возможности

- **Иконка-светофор в трее** — зелёный / жёлтый / красный
- **Правый клик** — краткий статус прямо в меню
- **Окно детальных статусов** — каждая проверка с понятным комментарием («Всё работает, замедлений нет»)
- **Встроенный xray-core** — настоящий протокол VLESS+Reality (скачивается автоматически)
- **Детектирование DPI** — обнаружение блокировок РКН / замедлений провайдера:
  - Freeze на 16 КБ (правило 25 пакетов ТСПУ)
  - Скачок latency (соотношение прямой vs туннель)
  - Верификация маяка сквозь туннель
- **Диагноз провайдера** — различает: VPS недоступен / порт заблокирован / DPI throttle / упал аплинк
- **Настройки** — VLESS-ссылка, интервал, Telegram, автозапуск с Windows
- **Один exe** — ничего ставить не нужно

---

## Установка (готовый бинарник)

1. Скачайте `VlessMonitor-windows-x64.zip` из [Releases](../../releases/latest)
2. Распакуйте в любую папку, например `C:\Tools\VlessMonitor\`
3. Запустите `VlessMonitor.exe`
4. Вставьте вашу VLESS-ссылку в окне настроек

xray-core встроен в exe и распаковывается рядом при первом запуске — доступ к GitHub не нужен (важно, если он сам под блокировкой).

> **Антивирус**: некоторые AV ругаются на свежесобранный exe + встроенный xray. Добавьте папку в исключения.

---

## Сборка из исходников

**Требования:** [.NET 8 SDK](https://dotnet.microsoft.com/download), Windows 10/11

```bat
git clone https://github.com/arty-s/vless-monitor
cd vless-monitor\client
dotnet publish -c Release -o publish
:: Готовый exe: client\publish\VlessMonitor.exe
```

Чтобы xray-core был **встроен в exe** (а не качался при первом запуске), положите `xray.exe` в `client\runtime\` перед сборкой:

```bat
:: скачайте Xray-windows-64.zip с github.com/XTLS/Xray-core/releases,
:: распакуйте xray.exe в client\runtime\xray.exe, затем dotnet publish
```

Если файла там нет — сборка всё равно пройдёт, но приложение будет скачивать xray при первом запуске (fallback). Именно так это делает CI: качает xray и встраивает автоматически.

Структура проекта:

```
client/            ← Windows-клиент (C# / .NET 8)
  Core/            ← конфиг, парсер VLESS, менеджер xray, оркестратор проверок
  Checks/          ← проверки (ping, TCP, туннель, DPI)
  UI/              ← трей, окна статусов и настроек
server/            ← probe-сервер для VPS (Python)
```

---

## Серверная часть (probe server, опционально)

Probe server на VPS добавляет **сквозную верификацию туннеля** и **тест DPI stream**.

### Установка на VPS

```bash
# Скопируйте файл на VPS
scp server/probe_server.py root@ВАШ_VPS:/opt/vless-monitor/

# Подключитесь и создайте сервис
ssh root@ВАШ_VPS

cat > /etc/systemd/system/vless-probe.service << EOF
[Unit]
Description=VLESS Monitor Probe Server
After=network.target

[Service]
ExecStart=/usr/bin/python3 /opt/vless-monitor/probe_server.py --port 8765 --secret ВАШ_СЕКРЕТ
Restart=always

[Install]
WantedBy=multi-user.target
EOF

systemctl enable --now vless-probe
ufw allow 8765/tcp
```

### Настройка в приложении

В окне настроек укажите тот же `probe_secret`, что и в `--secret` на сервере.

---

## Категории проверок

| Категория | Проверки |
|---|---|
| **Пинги** | VPS, Google DNS, Cloudflare, Яндекс (RU), Ростелеком (RU) |
| **Порты** | TCP на порт VLESS, порт probe-сервера |
| **Туннель** | Прямой HTTP на VPS, E2E маяк через VLESS, HTTP через туннель |
| **DPI** | 16 КБ freeze тест, коэффициент задержки |
| **Статистика** | Трафик xray (↑↓ МБ) |

---

## Логика светофора

| Цвет | Смысл | Типичная причина |
|---|---|---|
| 🟢 Зелёный | Всё ОК | — |
| 🟡 Жёлтый | Проблемы | DPI throttle, замедление туннеля |
| 🔴 Красный | Обрыв / блок | Порт VLESS закрыт, VPS недоступен, нет интернета |

### Диагностика по паттерну

| Пинг VPS | TCP порт | Туннель | Диагноз |
|---|---|---|---|
| ✅ | ✅ | ✅ | Всё ОК |
| ✅ | ✅ | ❌ | DPI режет VLESS трафик |
| ✅ | ❌ | ❌ | Порт заблокирован провайдером |
| ❌ | ❌ | ❌ | VPS недоступен |
| ✅ (RU) | — | ❌ | Упал аплинк провайдера (международный трафик) |

---

## Лицензия

MIT
