@echo off
setlocal EnableExtensions
cls
echo ============================================================
echo  Discord TTS Microphone v3.1 - Driver Toolchain Check
echo ============================================================
echo.
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
  echo [FAIL] Visual Studio Installer / vswhere.exe not found.
  echo Install Visual Studio 2022 with Desktop development with C++ and the Windows Driver Kit.
  goto :fail
)
for /f "usebackq tokens=*" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -property installationPath`) do set "VSROOT=%%I"
if not defined VSROOT (
  echo [FAIL] Visual Studio with MSBuild was not found.
  goto :fail
)
echo [OK] Visual Studio: %VSROOT%
if not exist "%VSROOT%\MSBuild\Current\Bin\MSBuild.exe" (
  echo [FAIL] MSBuild.exe not found under Visual Studio.
  goto :fail
)
echo [OK] MSBuild found.

set "KITS=%ProgramFiles(x86)%\Windows Kits\10"
if not exist "%KITS%\Include" (
  echo [FAIL] Windows 10/11 SDK/WDK include directory not found.
  goto :fail
)
if not exist "%KITS%\bin" (
  echo [FAIL] Windows Kits bin directory not found.
  goto :fail
)
for /d %%D in ("%KITS%\Include\10.*") do set "KITVER=%%~nxD"
if not defined KITVER (
  echo [FAIL] No Windows Kit version found.
  goto :fail
)
echo [OK] Windows Kit: %KITVER%

if exist "%KITS%\bin\%KITVER%\x64\Inf2Cat.exe" (echo [OK] Inf2Cat.exe found.) else echo [WARN] Inf2Cat.exe not found at expected path.
if exist "%KITS%\bin\%KITVER%\x64\signtool.exe" (echo [OK] signtool.exe found.) else echo [WARN] signtool.exe not found at expected path.

echo.
echo Toolchain check passed far enough to proceed with SysVAD preparation.
pause
exit /b 0
:fail
echo.
echo Driver toolchain is incomplete. This script does not modify Windows.
pause
exit /b 1
