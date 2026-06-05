@echo off
chcp 65001 > nul
echo === VLESS Monitor — установка ===
echo.

where python >nul 2>&1
if %errorlevel% neq 0 (
    echo [ОШИБКА] Python не найден. Скачайте с https://python.org ^(добавьте в PATH^)
    pause & exit /b 1
)

echo Установка зависимостей...
pip install PyQt6 requests PySocks --quiet

echo.
echo Готово! Запуск: start.bat
pause
