@echo off
setlocal
cd /d "%~dp0.."
if not exist third_party mkdir third_party
if exist third_party\Windows-driver-samples\audio\sysvad (
  echo SysVAD source already exists.
  goto done
)
where git >nul 2>nul
if errorlevel 1 (
  echo Git is required for this developer-only step.
  echo Clone Microsoft's Windows-driver-samples and place it under third_party\Windows-driver-samples.
  exit /b 1
)
git clone --depth 1 --filter=blob:none --sparse https://github.com/microsoft/Windows-driver-samples.git third_party\Windows-driver-samples
if errorlevel 1 exit /b 1
cd third_party\Windows-driver-samples
git sparse-checkout set audio/sysvad
:done
echo.
echo SysVAD ready under third_party\Windows-driver-samples\audio\sysvad
echo Open sysvad.sln with Visual Studio + WDK and apply src\DriverOverlay\README_DRIVER.md.
pause
