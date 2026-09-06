@echo off
chcp 65001 >nul
echo ========================================
echo   Registru Transferuri Media
echo ========================================
echo.

echo [1/4] Verificare Python...
python --version >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [X] Python nu este instalat!
    pause
    exit /b 1
)
python --version
echo.

echo [2/4] Verificare structura directoare...
if not exist "database" (
    echo [X] Lipseste directorul database\
    pause
    exit /b 1
)
if not exist "database\schema.sql" (
    echo [X] Lipseste database\schema.sql
    pause
    exit /b 1
)
echo [OK] Directoare verificate
echo.

echo [3/4] Verificare baza de date...
if not exist "transferuri.db" (
    echo [!] Baza de date NU exista!
    echo [~] Creare baza de date...
    python init_database.py
    if %ERRORLEVEL% neq 0 (
        echo [X] Eroare la creare DB!
        pause
        exit /b 1
    )
) else (
    echo [OK] transferuri.db gasit
    echo [~] Verificare tabele...
    python -c "import sqlite3; conn=sqlite3.connect('transferuri.db'); tables=[t[0] for t in conn.execute('SELECT name FROM sqlite_master WHERE type=\"table\"').fetchall()]; exit(0 if 'transferuri' in tables else 1)" >nul 2>&1
    if %ERRORLEVEL% neq 0 (
        echo [!] DB exista dar fara tabele!
        echo [~] Initializare tabele...
        python init_database.py
        if %ERRORLEVEL% neq 0 (
            echo [X] Eroare la initializare tabele!
            pause
            exit /b 1
        )
    ) else (
        echo [OK] Tabele verificate
    )
)
echo.

echo [4/4] Pornire aplicatie...
echo.
python main.py

if %ERRORLEVEL% neq 0 (
    echo.
    echo [X] Aplicatia s-a inchis cu eroare!
    echo.
    pause
)
