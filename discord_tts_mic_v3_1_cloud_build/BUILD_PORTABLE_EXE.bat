@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

set "ROOT=%~dp0"
set "PROJECT=%ROOT%src\App\DiscordTtsMic.App.csproj"
set "OUT=%ROOT%dist\portable"
set "SHAREDROOT=%LOCALAPPDATA%\DiscordTtsMic\BuildTools"
set "DOTNETROOT=%SHAREDROOT%\dotnet"
set "DOTNETEXE=%DOTNETROOT%\dotnet.exe"
set "NUGET_PACKAGES=%SHAREDROOT%\nuget-packages"
set "LEGACYDOTNET=%ROOT%.buildtools\dotnet\dotnet.exe"
set "SDKZIP=%SHAREDROOT%\dotnet-sdk-win-x64.zip"
set "VERSIONFILE=%SHAREDROOT%\latest.version"

cls
echo ============================================================
echo  Discord TTS Microphone v3 - Portable Builder
echo  Shared SDK Cache / No PowerShell
echo ============================================================
echo.

if not exist "%PROJECT%" (
    echo ERROR: Project file not found:
    echo   %PROJECT%
    goto :failed
)

rem 1. Prefer an already installed system .NET SDK.
where dotnet.exe >nul 2>nul
if %errorlevel%==0 (
    for /f "delims=" %%I in ('where dotnet.exe') do (
        set "DOTNETEXE=%%I"
        set "DOTNETROOT="
        echo Using system .NET SDK:
        echo   %%I
        goto :dotnet_ready
    )
)

rem 2. Reuse the cross-version shared SDK cache.
if exist "%DOTNETEXE%" (
    echo Using cached shared .NET SDK:
    echo   %DOTNETEXE%
    goto :dotnet_ready
)

rem 3. Reuse an SDK from an older package if one is already beside this source.
if exist "%LEGACYDOTNET%" (
    echo Found legacy project-local SDK. Reusing it for this build:
    echo   %LEGACYDOTNET%
    set "DOTNETEXE=%LEGACYDOTNET%"
    set "DOTNETROOT=%ROOT%.buildtools\dotnet"
    goto :dotnet_ready
)

rem 4. Download only once into LOCALAPPDATA so future versions reuse it.
echo No system or cached .NET SDK found.
echo A private .NET 8 SDK will be downloaded ONCE to:
echo   %DOTNETROOT%
echo.
echo Future Discord TTS Microphone versions will reuse this SDK.
echo No .NET installation and no registry changes are performed.
echo.

if not exist "%SHAREDROOT%" mkdir "%SHAREDROOT%"
if not exist "%DOTNETROOT%" mkdir "%DOTNETROOT%"
if not exist "%NUGET_PACKAGES%" mkdir "%NUGET_PACKAGES%"

curl.exe -fL --retry 3 --connect-timeout 20 "https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0/latest.version" -o "%VERSIONFILE%"
if errorlevel 1 goto :download_failed

set /p SDKVER=<"%VERSIONFILE%"
if not defined SDKVER goto :download_failed

echo Latest .NET 8 SDK: %SDKVER%
echo.

curl.exe -fL --retry 3 --connect-timeout 20 "https://builds.dotnet.microsoft.com/dotnet/Sdk/%SDKVER%/dotnet-sdk-%SDKVER%-win-x64.zip" -o "%SDKZIP%"
if errorlevel 1 goto :download_failed

echo Extracting SDK to shared cache...
tar.exe -xf "%SDKZIP%" -C "%DOTNETROOT%"
if errorlevel 1 goto :extract_failed

if not exist "%DOTNETEXE%" goto :extract_failed

del /q "%SDKZIP%" >nul 2>nul

:dotnet_ready
echo.
echo Build SDK:
echo   %DOTNETEXE%
echo Shared NuGet cache:
echo   %NUGET_PACKAGES%
echo.

if defined DOTNETROOT set "DOTNET_ROOT=%DOTNETROOT%"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_NOLOGO=1"
if not exist "%NUGET_PACKAGES%" mkdir "%NUGET_PACKAGES%"

"%DOTNETEXE%" --info >nul
if errorlevel 1 goto :dotnet_blocked

if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"

echo Restoring packages...
call "%DOTNETEXE%" restore "%PROJECT%"
if errorlevel 1 goto :build_failed

echo.
echo Publishing portable Windows x64 EXE...
call "%DOTNETEXE%" publish "%PROJECT%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%OUT%" --no-restore
if errorlevel 1 goto :build_failed

if exist "%OUT%\DiscordTtsMic.pdb" del /q "%OUT%\DiscordTtsMic.pdb"
if exist "%ROOT%README.md" copy /y "%ROOT%README.md" "%OUT%\README.md" >nul

if not exist "%OUT%\DiscordTtsMic.exe" goto :build_failed

echo.
echo ============================================================
echo  BUILD COMPLETE
echo ============================================================
echo.
echo Portable EXE:
echo   %OUT%\DiscordTtsMic.exe
echo.
echo Shared SDK cache is kept at:
echo   %LOCALAPPDATA%\DiscordTtsMic\BuildTools\dotnet
echo.
echo Do NOT delete that shared cache if you want future versions to build
echo without downloading the SDK again.
echo.
pause
exit /b 0

:download_failed
echo.
echo ERROR: Could not download the .NET SDK.
echo Check network/proxy access to builds.dotnet.microsoft.com.
goto :failed

:extract_failed
echo.
echo ERROR: Could not extract the .NET SDK ZIP.
echo Windows 10/11 normally includes tar.exe.
goto :failed

:dotnet_blocked
echo.
echo ERROR: dotnet.exe could not be executed.
echo If Windows reports a Group Policy / AppLocker / WDAC block, this builder
echo will not attempt to bypass your organization's security policy.
goto :failed

:build_failed
echo.
echo ERROR: Build failed. See the messages above.
goto :failed

:failed
echo.
echo BUILD FAILED.
echo.
pause
exit /b 1
