@echo off
chcp 65001 > nul
echo === VLESS Monitor — сборка exe ===
echo.

where python >nul 2>&1 || (echo [ERROR] Python не найден & pause & exit /b 1)

echo Установка зависимостей...
pip install PyQt6 requests PySocks pyinstaller --quiet

echo.
echo Сборка...
python -m PyInstaller monitor.spec --clean --noconfirm

if %errorlevel% neq 0 (
    echo [ERROR] Сборка не удалась
    pause & exit /b 1
)

echo.
echo === Готово ===
echo Exe: dist\VlessMonitor\VlessMonitor.exe
echo.

:: Copy config.example.json to dist folder
copy /y config.example.json dist\VlessMonitor\ >nul

echo Запустить сейчас? (y/n)
set /p run=
if /i "%run%"=="y" start "" "dist\VlessMonitor\VlessMonitor.exe"
