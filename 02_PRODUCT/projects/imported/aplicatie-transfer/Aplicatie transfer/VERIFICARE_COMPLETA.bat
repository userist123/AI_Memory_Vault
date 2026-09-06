@echo off
chcp 65001 >nul
echo ╔════════════════════════════════════════════════════════╗
echo ║   VERIFICARE și FIX COMPLET - Registru Transferuri    ║
echo ╚════════════════════════════════════════════════════════╝
echo.

set ERRORS=0

echo [STEP 1/6] Verificare directoarele...
echo ────────────────────────────────────────────────────────

if not exist "database" (
    echo [X] LIPSEȘTE: database\
    mkdir database
    echo [√] Creat: database\
)
if not exist "ui" (
    echo [X] LIPSEȘTE: ui\
    mkdir ui
    echo [√] Creat: ui\
)
if not exist "ui\widgets" (
    echo [X] LIPSEȘTE: ui\widgets\
    mkdir ui\widgets
    echo [√] Creat: ui\widgets\
)
if not exist "utils" (
    echo [X] LIPSEȘTE: utils\
    mkdir utils
    echo [√] Creat: utils\
)
echo [√] Directoare verificate
echo.

echo [STEP 2/6] Verificare fișiere __init__.py...
echo ────────────────────────────────────────────────────────

if not exist "database\__init__.py" (
    echo [X] LIPSEȘTE: database\__init__.py
    type nul > "database\__init__.py"
    echo [√] Creat: database\__init__.py
)
if not exist "ui\__init__.py" (
    echo [X] LIPSEȘTE: ui\__init__.py
    type nul > "ui\__init__.py"
    echo [√] Creat: ui\__init__.py
)
if not exist "ui\widgets\__init__.py" (
    echo [X] LIPSEȘTE: ui\widgets\__init__.py
    type nul > "ui\widgets\__init__.py"
    echo [√] Creat: ui\widgets\__init__.py
)
if not exist "utils\__init__.py" (
    echo [X] LIPSEȘTE: utils\__init__.py
    type nul > "utils\__init__.py"
    echo [√] Creat: utils\__init__.py
)
echo [√] Fișiere __init__.py verificate
echo.

echo [STEP 3/6] Verificare fișiere Python principale...
echo ────────────────────────────────────────────────────────

set MISSING_FILES=0

if not exist "main.py" (
    echo [X] LIPSEȘTE: main.py
    set /a MISSING_FILES+=1
)
if not exist "database\db.py" (
    echo [X] LIPSEȘTE: database\db.py
    set /a MISSING_FILES+=1
)
if not exist "database\schema.sql" (
    echo [X] LIPSEȘTE: database\schema.sql
    set /a MISSING_FILES+=1
)
if not exist "ui\main_window.py" (
    echo [X] LIPSEȘTE: ui\main_window.py
    set /a MISSING_FILES+=1
)
if not exist "ui\widgets\form_widget.py" (
    echo [X] LIPSEȘTE: ui\widgets\form_widget.py
    set /a MISSING_FILES+=1
)
if not exist "ui\widgets\table_widget.py" (
    echo [X] LIPSEȘTE: ui\widgets\table_widget.py
    set /a MISSING_FILES+=1
)
if not exist "ui\widgets\settings_widget.py" (
    echo [X] LIPSEȘTE: ui\widgets\settings_widget.py
    set /a MISSING_FILES+=1
)

if %MISSING_FILES% gtr 0 (
    echo.
    echo [!] ATENȚIE: %MISSING_FILES% fișiere principale LIPSESC!
    echo [!] Descarcă TOATE fișierele din proiect și repune-le aici.
    echo.
    set /a ERRORS+=1
) else (
    echo [√] Toate fișierele principale există
)
echo.

echo [STEP 4/6] Fix stats_widget.py (eroare RenderHint)...
echo ────────────────────────────────────────────────────────

if exist "ui\widgets\stats_widget_FIXED.py" (
    echo [√] Găsit stats_widget_FIXED.py
    echo [~] Backup stats_widget.py original...
    if exist "ui\widgets\stats_widget.py" (
        copy /Y "ui\widgets\stats_widget.py" "ui\widgets\stats_widget_OLD.py" >nul
    )
    echo [~] Înlocuire cu versiunea FIXED...
    copy /Y "ui\widgets\stats_widget_FIXED.py" "ui\widgets\stats_widget.py" >nul
    echo [√] stats_widget.py actualizat
) else (
    if not exist "ui\widgets\stats_widget.py" (
        echo [X] LIPSEȘTE: ui\widgets\stats_widget.py
        echo [!] Descarcă stats_widget_FIXED.py și pune-l în ui\widgets\
        set /a ERRORS+=1
    ) else (
        echo [√] stats_widget.py există (verifică manual pentru erori)
    )
)
echo.

echo [STEP 5/6] Verificare și fix PyQt6 versiuni...
echo ────────────────────────────────────────────────────────

python --version >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [X] Python NU este instalat!
    set /a ERRORS+=1
    goto skip_pyqt
)

echo [~] Verificare versiuni PyQt6...
python -c "import PyQt6; print('[√] PyQt6:', PyQt6.QtCore.PYQT_VERSION_STR)" 2>nul

python -c "from PyQt6.QtCharts import QChart; print('[√] PyQt6-Charts OK')" 2>nul
if %ERRORLEVEL% neq 0 (
    echo [X] PyQt6-Charts NU funcționează (DLL error sau lipsă)
    echo [~] Reinstalare PyQt6 + PyQt6-Charts cu versiuni sincronizate...
    pip uninstall -y PyQt6 PyQt6-Charts PyQt6-Qt6 PyQt6-Charts-Qt6 PyQt6-sip 2>nul
    pip cache purge 2>nul
    echo [~] Instalare PyQt6==6.7.1 și PyQt6-Charts==6.7.0...
    pip install PyQt6==6.7.1 PyQt6-Charts==6.7.0
    
    python -c "from PyQt6.QtCharts import QChart; print('[√] PyQt6-Charts REPARAT')" 2>nul
    if %ERRORLEVEL% neq 0 (
        echo [!] PyQt6-Charts încă nu funcționează
        echo [!] Folosește versiunea FALLBACK (fără grafice):
        echo [!]   Rulează: python main.py (stats fără charts)
    )
)

:skip_pyqt
echo.

echo [STEP 6/6] Test final import...
echo ────────────────────────────────────────────────────────

if %ERRORS% equ 0 (
    python -c "from ui.widgets import form_widget, table_widget, stats_widget, settings_widget; print('[√] Toate module importate corect')" 2>nul
    if %ERRORLEVEL% neq 0 (
        echo [X] Eroare la import module
        set /a ERRORS+=1
    )
)
echo.

echo ════════════════════════════════════════════════════════
if %ERRORS% equ 0 (
    echo [√] SUCCESS! Toate verificările au trecut!
    echo ════════════════════════════════════════════════════════
    echo.
    echo Pornesc aplicația...
    echo.
    python main.py
) else (
    echo [X] ERORI GĂSITE: %ERRORS%
    echo ════════════════════════════════════════════════════════
    echo.
    echo ACȚIUNI NECESARE:
    echo 1. Descarcă TOATE fișierele .py din proiect
    echo 2. Pune-le în directoarele corecte:
    echo    - main.py în rădăcină
    echo    - database\*.py în database\
    echo    - ui\*.py în ui\
    echo    - ui\widgets\*.py în ui\widgets\
    echo    - utils\*.py în utils\
    echo 3. Rulează din nou: VERIFICARE_COMPLETA.bat
    echo.
    pause
)
