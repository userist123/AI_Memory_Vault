@echo off
title Trading Bot — Instalare
echo.
echo  ============================================
echo    TRADING BOT v2.0 — INSTALARE
echo  ============================================
echo.

REM Verifica Python
python --version >nul 2>&1
if errorlevel 1 (
    echo  [EROARE] Python nu este instalat!
    echo  Descarca de la: https://www.python.org/downloads/
    echo  IMPORTANT: Bifeaza "Add Python to PATH" la instalare!
    pause
    exit /b 1
)

echo  [1/2] Se instaleaza dependentele...
pip install --upgrade pip
pip install -r requirements.txt

echo.
echo  [2/2] Se verifica instalarea...
python -c "import PyQt6; import yfinance; import ta; import ccxt; print('  OK — Toate dependentele sunt instalate!')"

if errorlevel 1 (
    echo.
    echo  [AVERTIZARE] Unele pachete nu s-au instalat corect.
    echo  Incearca manual: pip install PyQt6 yfinance ta ccxt pyqtgraph cryptography
)

echo.
echo  ============================================
echo    INSTALARE COMPLETA!
echo    Ruleaza: python main.py  sau  START_BOT.bat
echo  ============================================
pause
