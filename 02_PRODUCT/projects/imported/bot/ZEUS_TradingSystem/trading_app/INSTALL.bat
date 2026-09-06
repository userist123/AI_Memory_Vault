@echo off
title ZEUS Trading System - Installer
echo.
echo  ================================================
echo  ⚡ ZEUS TRADING SYSTEM - Instalare dependinte
echo  ================================================
echo.

python --version >nul 2>&1
if errorlevel 1 (
    echo [EROARE] Python nu este instalat!
    echo Descarca Python 3.11+ de la: https://www.python.org/downloads/
    echo Asigura-te sa bifezi "Add Python to PATH" la instalare.
    pause
    exit /b 1
)

echo [1/3] Actualizare pip...
python -m pip install --upgrade pip --quiet

echo [2/3] Instalare dependinte Zeus Trading...
python -m pip install PyQt6 pyqtgraph yfinance pandas numpy ta --quiet

echo [3/3] Verificare instalare...
python -c "import PyQt6; import pyqtgraph; import yfinance; import ta; print('OK - Toate dependintele instalate!')"

echo.
echo  ================================================
echo  ✅ Instalare completa! 
echo  Ruleaza: START_ZEUS.bat
echo  ================================================
pause
