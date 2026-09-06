@echo off
chcp 65001 >nul
echo.
echo  ╔══════════════════════════════════════════════════════╗
echo  ║        OWNER AI AGENT TEAM - Setup Check           ║
echo  ╚══════════════════════════════════════════════════════╝
echo.

python --version >nul 2>&1
if errorlevel 1 (
    echo [EROARE] Python nu e instalat!
    pause & exit /b 1
)

python -c "import google.generativeai" >nul 2>&1
if errorlevel 1 (
    echo [INFO] Instalez google-generativeai...
    pip install google-generativeai
)

if "%GEMINI_API_KEY%"=="" (
    echo [ATENTIE] GEMINI_API_KEY nu e setat!
    echo Obtine cheie gratuita: https://aistudio.google.com/app/apikey
    echo.
    set /p GEMINI_API_KEY="Introdu API key-ul (AIza...): "
    if "%GEMINI_API_KEY%"=="" (
        echo [EROARE] API key necesar!
        pause & exit /b 1
    )
    echo.
    echo Vrei sa salvez cheia permanent? (setx)
    set /p SAVE_KEY="Da/Nu: "
    if /i "%SAVE_KEY%"=="da" (
        setx GEMINI_API_KEY "%GEMINI_API_KEY%"
        echo [OK] Cheia salvata permanent!
    )
)

echo.
echo [OK] Pornesc Orchestratorul...
echo.
python orchestrator.py
pause
