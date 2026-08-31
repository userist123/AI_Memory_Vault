@echo off
echo Running XAU_Kinetic System Build and Unit Test Verification...
cd /d "%~dp0"
echo.
echo [1/2] Compiling C# .NET 8 WPF Desktop Control Center...
dotnet build XAU_Kinetic.Desktop/XAU_Kinetic.Desktop.csproj
if %errorlevel% neq 0 (
    echo [ERROR] C# WPF compilation failed.
    pause
    exit /b %errorlevel%
)
echo [PASS] C# WPF Desktop Build Succeeded.
echo.
echo [2/2] Running Python Unit Test Suite (20 Tests)...
python -m unittest discover -s xau_kinetic/tests
if %errorlevel% neq 0 (
    echo [ERROR] Python unit tests failed.
    pause
    exit /b %errorlevel%
)
echo [PASS] Python Unit Tests Succeeded.
echo.
echo === ALL SYSTEM TESTS PASSED SUCCESSFULLY ===
pause
