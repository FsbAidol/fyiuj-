@echo off
REM Local Windows build (same command the GitHub Action runs)
setlocal
cd /d "%~dp0.."
if not exist .venv (
    py -3 -m venv .venv || goto :err
)
call .venv\Scripts\activate
python -m pip install --upgrade pip
python -m pip install -r requirements.txt pyinstaller || goto :err
pyinstaller tools\amazingbot.spec --noconfirm || goto :err
echo.
echo Готово: dist\AmazingBot\AmazingBot.exe
pause
exit /b 0
:err
echo Ошибка сборки
pause
exit /b 1
