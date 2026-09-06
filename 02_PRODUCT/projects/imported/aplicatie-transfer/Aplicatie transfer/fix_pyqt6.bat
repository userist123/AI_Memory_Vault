@echo off
echo ========================================
echo Fix PyQt6-Charts DLL Error
echo ========================================
echo.

echo [Pasul 1/4] Dezinstalare PyQt6 si PyQt6-Charts...
pip uninstall -y PyQt6 PyQt6-Charts PyQt6-Qt6 PyQt6-Charts-Qt6 PyQt6-sip
echo.

echo [Pasul 2/4] Curatare cache pip...
pip cache purge
echo.

echo [Pasul 3/4] Reinstalare PyQt6 6.7.1 + PyQt6-Charts 6.7.0 (versiuni SINCRONIZATE)...
pip install PyQt6==6.7.1 PyQt6-Charts==6.7.0
echo.

echo [Pasul 4/4] Verificare instalare...
python -c "from PyQt6.QtWidgets import QApplication; print('[OK] PyQt6 importat')"
python -c "from PyQt6.QtCharts import QChart; print('[OK] PyQt6-Charts importat')"
echo.

if %ERRORLEVEL%==0 (
    echo ========================================
    echo SUCCESS! PyQt6-Charts reparat!
    echo ========================================
    echo.
    echo Pornesc aplicatia...
    python main.py
) else (
    echo ========================================
    echo EROARE la instalare!
    echo ========================================
    echo.
    echo Incearca solutia FALLBACK (fara grafice):
    echo   python main_no_charts.py
    echo.
    pause
)
