@echo off
setlocal EnableExtensions
cd /d "%~dp0.."
set "SLN=%CD%\third_party\Windows-driver-samples\audio\sysvad\sysvad.sln"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
cls
echo ============================================================
echo  Discord TTS Microphone v3.1 - Build Clean SysVAD Baseline
echo ============================================================
echo.
if not exist "%SLN%" (
  echo [FAIL] SysVAD is missing. Run scripts\02_FETCH_SYSVAD.cmd first.
  pause
  exit /b 1
)
if not exist "%VSWHERE%" (
  echo [FAIL] Visual Studio/WDK toolchain missing. Run scripts\01_CHECK_DRIVER_TOOLCHAIN.cmd.
  pause
  exit /b 1
)
for /f "usebackq tokens=*" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -property installationPath`) do set "VSROOT=%%I"
set "MSBUILD=%VSROOT%\MSBuild\Current\Bin\MSBuild.exe"
if not exist "%MSBUILD%" (
  echo [FAIL] MSBuild was not found.
  pause
  exit /b 1
)
echo Building the unmodified Microsoft sample first.
echo This validates Visual Studio + WDK before our overlay is applied.
"%MSBUILD%" "%SLN%" /m /t:Build /p:Configuration=Debug /p:Platform=x64
if errorlevel 1 (
  echo.
  echo [FAIL] Clean SysVAD did not build. Fix the VS/WDK environment before modifying the driver.
  pause
  exit /b 1
)
echo.
echo [OK] Clean SysVAD baseline built successfully.
echo Next milestone: integrate src\DriverOverlay into one capture endpoint.
pause
exit /b 0
