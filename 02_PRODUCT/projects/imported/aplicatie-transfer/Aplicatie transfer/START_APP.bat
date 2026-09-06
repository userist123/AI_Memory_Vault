@echo off
echo ========================================
echo   Registru Transferuri Media
echo ========================================
echo.

echo [1/3] Verificare Python...
python --version >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [X] Python nu este instalat!
    pause
    exit /b 1
)
python --version
echo.

echo [2/3] Verificare baza de date...
if not exist "transferuri.db" (
    echo [!] Baza de date NU exista!
    echo [~] Initializare baza de date...
    python init_database.py
    if %ERRORLEVEL% neq 0 (
        echo [X] Eroare la initializare DB!
        pause
        exit /b 1
    )
) else (
    echo [OK] transferuri.db gasit
)
echo.

echo [3/3] Pornire aplicatie...
python main.py

if %ERRORLEVEL% neq 0 (
    echo.
    echo [X] Aplicatia s-a inchis cu eroare!
    echo.
    pause
)
