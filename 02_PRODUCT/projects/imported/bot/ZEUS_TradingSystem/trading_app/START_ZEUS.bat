@echo off
title ZEUS Trading System
echo.
echo  ⚡ Se porneste ZEUS Trading System...
echo.
cd /d "%~dp0"
python main.py
if errorlevel 1 (
    echo.
    echo [EROARE] A aparut o problema la pornire.
    echo Ruleaza INSTALL.bat mai intai.
    pause
)
