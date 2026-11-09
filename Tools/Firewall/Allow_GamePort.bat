@echo off
setlocal
cd /d "%~dp0"

echo ======================================================
echo  MS2027 Game Port Firewall Setup (UDP 7777)
echo ======================================================
echo.

set RULE_NAME="MS2027_GamePort_7777"
set PORT=7777

echo Registering firewall rule...
netsh advfirewall firewall add rule name=%RULE_NAME% dir=in action=allow protocol=UDP localport=%PORT% profile=any

echo.
if errorlevel 1 goto :FAIL

:SUCCESS
echo ======================================================
echo [SUCCESS] Firewall rule registered successfully.
echo Target Port: UDP %PORT% (Domain / Private / Public)
echo Unity Editor and built game are now allowed.
echo ======================================================
goto :END

:FAIL
echo ======================================================
echo [FAILED] Administrator privilege is required.
echo Please RIGHT-CLICK this file and select:
echo "Run as administrator"
echo ======================================================
goto :END

:END
echo.
pause
