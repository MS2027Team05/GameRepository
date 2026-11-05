@echo off
setlocal
cd /d "%~dp0"

echo ======================================================
echo  MS2027 Game Port Firewall Delete (UDP 7777)
echo ======================================================
echo.

set RULE_NAME="MS2027_GamePort_7777"

echo Deleting firewall rule...
netsh advfirewall firewall delete rule name=%RULE_NAME%

echo.
if errorlevel 1 goto :FAIL

:SUCCESS
echo ======================================================
echo [SUCCESS] Firewall rule removed successfully.
echo ======================================================
goto :END

:FAIL
echo ======================================================
echo [INFO/FAILED] Rule not found or permission denied.
echo If permission denied, please RIGHT-CLICK this file
echo and select "Run as administrator".
echo ======================================================
goto :END

:END
echo.
pause
