@echo off
title Trading Bot v2.0
cd /d "%~dp0"
python main.py
if errorlevel 1 (
    echo.
    echo  Eroare la pornire. Ruleaza INSTALL.bat mai intai.
    pause
)
