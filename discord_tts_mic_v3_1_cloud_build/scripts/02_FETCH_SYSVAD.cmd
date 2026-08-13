@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0.."
set "DEST=%CD%\third_party\Windows-driver-samples"
set "ZIP=%TEMP%\windows-driver-samples-main.zip"
set "TMP=%TEMP%\dtm-windows-driver-samples"
cls
echo ============================================================
echo  Discord TTS Microphone v3.1 - Fetch Microsoft SysVAD
echo ============================================================
echo.
if exist "%DEST%\audio\sysvad\sysvad.sln" (
  echo SysVAD is already available at:
  echo   %DEST%\audio\sysvad
  pause
  exit /b 0
)
where git.exe >nul 2>nul
if not errorlevel 1 (
  echo Fetching only audio/sysvad with Git sparse checkout...
  if not exist "%CD%\third_party" mkdir "%CD%\third_party"
  git clone --depth 1 --filter=blob:none --sparse https://github.com/microsoft/Windows-driver-samples.git "%DEST%"
  if errorlevel 1 goto :fail
  pushd "%DEST%"
  git sparse-checkout set audio/sysvad
  popd
  goto :verify
)

echo Git not found. Falling back to curl + tar; this downloads the Microsoft sample archive.
if exist "%TMP%" rmdir /s /q "%TMP%"
mkdir "%TMP%"
curl.exe -fL --retry 3 --connect-timeout 20 "https://github.com/microsoft/Windows-driver-samples/archive/refs/heads/main.zip" -o "%ZIP%"
if errorlevel 1 goto :fail
tar.exe -xf "%ZIP%" -C "%TMP%"
if errorlevel 1 goto :fail
if not exist "%CD%\third_party" mkdir "%CD%\third_party"
mkdir "%DEST%" >nul 2>nul
xcopy /e /i /y "%TMP%\Windows-driver-samples-main\audio\sysvad" "%DEST%\audio\sysvad" >nul

:verify
if not exist "%DEST%\audio\sysvad\sysvad.sln" goto :fail
echo.
echo [OK] SysVAD ready:
echo   %DEST%\audio\sysvad
pause
exit /b 0
:fail
echo.
echo [FAIL] Could not fetch/prepare SysVAD.
echo Check network access to github.com and your organization policy.
pause
exit /b 1
