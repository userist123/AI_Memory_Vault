@echo off
title JARVIS AI Memory Vault Command Center
color 0B
setlocal

set "JARVIS_DIR=%~dp0"
for %%I in ("%JARVIS_DIR%..\..") do set "VAULT_ROOT=%%~fI"

if exist "%JARVIS_DIR%.jarvis-voice.env.cmd" call "%JARVIS_DIR%.jarvis-voice.env.cmd"
if not defined PIPER_DATA_DIR set "PIPER_DATA_DIR=%JARVIS_DIR%voice_models"
if not defined JARVIS_TTS_MODEL set "JARVIS_TTS_MODEL=ro_RO-mihai-medium"

where python >nul 2>&1 || (echo  ERROR: Python is not installed/in PATH.& pause & exit /b 1)
where node >nul 2>&1 || (echo  ERROR: Node.js is not installed/in PATH.& pause & exit /b 1)

if not exist "%VAULT_ROOT%\memory_controller\api_server.py" (
  echo ERROR: AI Memory Vault root not found.
  pause
  exit /b 1
)

echo.
echo  ==================================================
echo   JARVIS AI MEMORY VAULT COMMAND CENTER
echo  ==================================================
echo   Vault: %VAULT_ROOT%
echo.

echo  [1/4] Starting Memory Vault API on port 8000...
start "JARVIS Memory Vault API" /D "%VAULT_ROOT%" cmd /k "python -m memory_controller.api_server 8000"

timeout /t 2 /nobreak >nul

echo  [2/4] Starting neural Romanian voice on port 8002...
start "JARVIS Romanian Neural Voice" /D "%JARVIS_DIR%" cmd /k "python voice_server.py"

timeout /t 2 /nobreak >nul

echo  [3/4] Starting JARVIS Command Center on port 3000...
start "JARVIS Command Center" /D "%JARVIS_DIR%" cmd /k "node server.cjs"

timeout /t 2 /nobreak >nul

echo  [4/4] Opening JARVIS...
start "" "http://127.0.0.1:3000"

echo.
echo  ==================================================
echo   JARVIS:       http://127.0.0.1:3000
echo   Memory API:   http://127.0.0.1:8000
echo   Voice API:    http://127.0.0.1:8002
echo   Voice model:  %JARVIS_TTS_MODEL%
echo  ==================================================
echo.
echo  For first-time neural voice setup run:
echo  powershell -ExecutionPolicy Bypass -File setup_jarvis_voice.ps1
echo.
pause
endlocal
