@echo off
echo ========================================
echo Verificare Structura Aplicatie
echo ========================================
echo.

echo [1/5] Verificare director curent...
echo Directory curent: %CD%
echo.

echo [2/5] Cautare main.py...
if exist main.py (
    echo [OK] main.py gasit in directorul curent
) else (
    echo [EROARE] main.py NU este in directorul curent!
    echo.
    echo SOLUTIE: Muta-te in directorul corect:
    echo   cd "C:\Users\user\Desktop\aplicatie transfer"
    echo   SAU
    echo   cd "Aplicatie transfer"
    echo.
    pause
    exit /b 1
)
echo.

echo [3/5] Verificare fișiere __init__.py...
set MISSING=0

if exist database\__init__.py (
    echo [OK] database\__init__.py
) else (
    echo [EROARE] database\__init__.py LIPSESTE!
    set MISSING=1
)

if exist ui\__init__.py (
    echo [OK] ui\__init__.py
) else (
    echo [EROARE] ui\__init__.py LIPSESTE!
    set MISSING=1
)

if exist ui\widgets\__init__.py (
    echo [OK] ui\widgets\__init__.py
) else (
    echo [EROARE] ui\widgets\__init__.py LIPSESTE!
    set MISSING=1
)

if exist utils\__init__.py (
    echo [OK] utils\__init__.py
) else (
    echo [EROARE] utils\__init__.py LIPSESTE!
    set MISSING=1
)
echo.

if %MISSING%==1 (
    echo [EROARE] Lipsesc fisiere __init__.py!
    echo.
    echo SOLUTIE RAPIDA: Creez fisierele lipsă...
    if not exist database\__init__.py (
        type nul > database\__init__.py
        echo   Creat: database\__init__.py
    )
    if not exist ui\__init__.py (
        type nul > ui\__init__.py
        echo   Creat: ui\__init__.py
    )
    if not exist ui\widgets (
        mkdir ui\widgets
    )
    if not exist ui\widgets\__init__.py (
        type nul > ui\widgets\__init__.py
        echo   Creat: ui\widgets\__init__.py
    )
    if not exist utils\__init__.py (
        type nul > utils\__init__.py
        echo   Creat: utils\__init__.py
    )
    echo.
    echo Fisierele __init__.py au fost create!
    echo.
)

echo [4/5] Verificare Python...
python --version >nul 2>&1
if %ERRORLEVEL%==0 (
    python --version
) else (
    echo [EROARE] Python nu este instalat sau nu e in PATH!
    pause
    exit /b 1
)
echo.

echo [5/5] Verificare PyQt6...
python -c "import PyQt6; print('[OK] PyQt6 version:', PyQt6.QtCore.PYQT_VERSION_STR)" 2>nul
if %ERRORLEVEL%==0 (
    echo PyQt6 este instalat corect
) else (
    echo [ATENTIE] PyQt6 nu este instalat!
    echo Instalez acum...
    pip install PyQt6 PyQt6-Charts
)
echo.

echo ========================================
echo Toate verificarile COMPLETE!
echo ========================================
echo.
echo Pornesc aplicatia...
echo.
python main.py
