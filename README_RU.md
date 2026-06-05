# VLESS Monitor

Приложение в трее Windows для мониторинга VLESS-прокси и детектирования замедлений / блокировок DPI.

---

## Возможности

- **Иконка светофор в трее** — зелёный / жёлтый / красный
- **Правый клик** — краткий статус прямо в меню
- **Окно детальных статусов** — все проверки по категориям
- **Встроенный xray-core** — использует настоящий протокол VLESS+Reality (скачивается автоматически)
- **Детектирование DPI** — обнаружение блокировок РКН / замедлений провайдера:
  - Freeze на 16 КБ (правило 25 пакетов РКН)
  - Скачок latency (соотношение прямой vs туннель)
  - Верификация маяка сквозь туннель
- **Диагноз провайдера** — различает: VPS недоступен / порт заблокирован / DPI throttle / upstream провайдера упал
- **Настройки** — VLESS-ссылка, интервал проверок, Telegram, автозапуск с Windows
- **Не нужен Python** — готовый `.exe`

---

## Установка (бинарник, рекомендуется)

1. Скачайте `VlessMonitor-windows-x64.zip` из [Releases](../../releases/latest)
2. Распакуйте в любую папку, например `C:\Tools\VlessMonitor\`
3. Запустите `VlessMonitor.exe`
4. При первом запуске вставьте вашу VLESS-ссылку в настройках
5. При первом запуске xray-core (~15 МБ) скачается с GitHub автоматически

> **Антивирус**: некоторые AV ругаются на PyInstaller + xray. Добавьте папку в исключения.

---

## Установка из исходников

**Требования:** Python 3.11+, Windows 10/11

```bat
git clone https://github.com/YOUR_USERNAME/vless-monitor
cd vless-monitor
pip install PyQt6 requests PySocks
python monitor.py
```

---

## Серверная часть (probe server, опционально)

Probe server на VPS добавляет **сквозную верификацию туннеля** и **тест DPI stream**.

### Установка на VPS

```bash
# Скопируйте файл на VPS
scp server/probe_server.py root@ВАШ_VPS:/opt/vless-monitor/

# Подключитесь к VPS и создайте сервис
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

В окне настроек укажите:
- **Probe server port**: `8765`
- **Probe secret**: ваш секрет (должен совпадать с `--secret` на сервере)

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
| ✅ (RU) | — | ❌ | Upstream провайдера упал (международный трафик) |

---

## Сборка exe самостоятельно

```bat
build.bat
# Результат: dist\VlessMonitor\VlessMonitor.exe
```

---

## Лицензия

MIT
