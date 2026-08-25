@echo off
title JARVIS AI Memory Vault Command Center
color 0B
setlocal

set "JARVIS_DIR=%~dp0"
for %%I in ("%JARVIS_DIR%..\..") do set "VAULT_ROOT=%%~fI"

echo.
echo  ==================================================
echo   JARVIS AI MEMORY VAULT COMMAND CENTER
echo  ==================================================
echo   Vault: %VAULT_ROOT%
echo.

if not exist "%VAULT_ROOT%\memory_controller\api_server.py" (
  echo  ERROR: AI Memory Vault root not found.
  echo  Expected: %VAULT_ROOT%
  pause
  exit /b 1
)

where python >nul 2>&1 || (echo  ERROR: Python is not installed/in PATH.& pause & exit /b 1)
where node >nul 2>&1 || (echo  ERROR: Node.js is not installed/in PATH.& pause & exit /b 1)

echo  [1/3] Starting Memory Vault API on port 8000...
start "JARVIS Memory Vault API" /D "%VAULT_ROOT%" cmd /k "python -m memory_controller.api_server 8000"

timeout /t 2 /nobreak >nul

echo  [2/3] Starting JARVIS Command Center on port 3000...
start "JARVIS Command Center" /D "%JARVIS_DIR%" cmd /k "node server.cjs"

timeout /t 2 /nobreak >nul

echo  [3/3] Opening JARVIS...
start "" "http://127.0.0.1:3000"

echo.
echo  ==================================================
echo   JARVIS: http://127.0.0.1:3000
echo   Memory API: http://127.0.0.1:8000
echo  ==================================================
echo.
echo  Close the two server windows to stop JARVIS.
echo.
pause
endlocal
