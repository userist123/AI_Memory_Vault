@echo off
title Statie Bootabila Offline - Sanitizare Medii Clasificate (TOE)
cd /d "%~dp0"
echo ===============================================================================
echo   PORNIRE MEDIU BOOTABIL DE SANITIZARE HARDWARE (TOE-SSE-v1)
echo ===============================================================================
echo.
python -m src.cli
pause
