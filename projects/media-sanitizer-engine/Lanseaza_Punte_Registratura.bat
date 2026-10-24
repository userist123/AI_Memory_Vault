@echo off
title Punte INFOSEC - Registratura Electronica & Semnatura Calificata
cd /d "%~dp0"
echo ===============================================================================
echo   PORNIRE APLICATIE PUNTE INFOSEC (HG 585/2002 / REGISTRATURA ZERO-HARTIE)
echo ===============================================================================
echo.
python -m src.windows_bridge_gui
pause
