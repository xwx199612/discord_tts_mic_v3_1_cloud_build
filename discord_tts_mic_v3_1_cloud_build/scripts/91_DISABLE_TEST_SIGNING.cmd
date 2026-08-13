@echo off
cls
echo ============================================================
echo  Disable Windows TESTSIGNING
echo ============================================================
echo.
choice /c YN /n /m "Disable TESTSIGNING now? [Y/N]: "
if errorlevel 2 exit /b 0
net session >nul 2>nul
if errorlevel 1 (
  echo [FAIL] Run this CMD as Administrator.
  pause
  exit /b 1
)
bcdedit /set testsigning off
if errorlevel 1 (
  echo [FAIL] Windows refused the change.
  pause
  exit /b 1
)
echo [OK] TESTSIGNING disabled. Reboot Windows to return to normal boot policy.
pause
