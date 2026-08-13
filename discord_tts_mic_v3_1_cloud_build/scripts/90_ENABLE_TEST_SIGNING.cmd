@echo off
cls
echo ============================================================
echo  Enable Windows TESTSIGNING for local driver development
echo ============================================================
echo.
echo This changes a Windows boot setting and requires Administrator rights.
echo A reboot is required. It is intended only for a development/test PC.
echo.
choice /c YN /n /m "Enable TESTSIGNING now? [Y/N]: "
if errorlevel 2 exit /b 0
net session >nul 2>nul
if errorlevel 1 (
  echo.
  echo [FAIL] Run this CMD as Administrator.
  pause
  exit /b 1
)
bcdedit /set testsigning on
if errorlevel 1 (
  echo.
  echo [FAIL] Windows refused the change. Secure Boot or organization policy may prevent test mode.
  pause
  exit /b 1
)
echo.
echo [OK] TESTSIGNING was enabled. Reboot Windows before loading a test-signed kernel driver.
pause
