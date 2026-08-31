@echo off
echo Launching XAU_Kinetic Python Quant Engine Loop...
cd /d "%~dp0"
python -m xau_kinetic.main --mock
pause
