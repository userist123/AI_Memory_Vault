@echo off
setlocal
set "VAULT_ROOT=C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY"
set "COGNITIVE_ROOT=%VAULT_ROOT%\projects\jarvis_cognitive_brain"
set "PYTHONPATH=%VAULT_ROOT%;%COGNITIVE_ROOT%"
set "JARVIS_UNIFIED_PORT=3000"
if "%JARVIS_BACKEND_AUDIO%"=="" set "JARVIS_BACKEND_AUDIO=0"

echo.
echo ============================================================
echo   JARVIS UNIFIED AI COMMAND CENTER
echo   Single process: web + memory + cognition + TTS + agents
echo ============================================================
echo.
echo   URL: http://127.0.0.1:%JARVIS_UNIFIED_PORT%
echo   Backend audio: %JARVIS_BACKEND_AUDIO%
echo.
echo   Stop with CTRL+C.
echo.

cd /d "%COGNITIVE_ROOT%"
python unified_server.py --host 127.0.0.1 --port %JARVIS_UNIFIED_PORT%
set "EXIT_CODE=%ERRORLEVEL%"
echo.
echo JARVIS stopped with exit code %EXIT_CODE%.
exit /b %EXIT_CODE%