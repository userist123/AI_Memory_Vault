@echo off
title JARVIS Web Ecosystem — Launcher
color 0B

echo.
echo  ==========================================
echo   JARVIS Web Ecosystem — Starting Systems
echo  ==========================================
echo.

:: Step 1: Start Memory Vault REST API (port 8000) in background
echo  [1/3] Starting AI Memory Vault REST API on port 8000...
start "JARVIS Memory Vault API" cmd /k "cd /d C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY && python -m memory_controller.api_server 8000"

:: Wait for vault to come up
timeout /t 2 /nobreak > nul

:: Step 2: Start JARVIS HTTP server (port 3000) in background
echo  [2/3] Starting JARVIS HTTP Server on port 3000...
start "JARVIS HTTP Server" cmd /k "cd /d C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_web && node server.cjs"

:: Wait for HTTP server to come up
timeout /t 2 /nobreak > nul

:: Step 3: Open browser
echo  [3/3] Opening JARVIS in your browser...
start "" "http://localhost:3000"

echo.
echo  ==========================================
echo   JARVIS is ONLINE at http://localhost:3000
echo   Memory Vault API at http://localhost:8000
echo  ==========================================
echo.
echo  Close the two server windows to stop JARVIS.
echo.
pause
