@echo off
echo.
echo  =======================================
echo   Cerebras Coding Agent - Setup Check
echo  =======================================
echo.

python --version >nul 2>&1
if errorlevel 1 (
    echo [EROARE] Python nu e instalat! Download: https://www.python.org
    pause
    exit /b 1
)

python -c "import cerebras" >nul 2>&1
if errorlevel 1 (
    echo [INFO] Instalez cerebras-cloud-sdk...
    pip install cerebras-cloud-sdk
)

if "%CEREBRAS_API_KEY%"=="" (
    echo.
    echo [ATENTIE] CEREBRAS_API_KEY nu e setat!
    echo Obtine o cheie gratuita de la: https://cloud.cerebras.ai
    echo.
    set /p CEREBRAS_API_KEY="Introdu API key-ul: "
    if "%CEREBRAS_API_KEY%"=="" (
        echo [EROARE] API key necesar!
        pause
        exit /b 1
    )
)

echo.
echo [OK] Totul e configurat! Pornesc agentul...
echo.
python agent.py %*
