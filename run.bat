@echo off
setlocal
title Fortnite Edit Input
cd /d "%~dp0"

set "EXE=%~dp0publish\FortniteEditInput.exe"

rem Already built? Just start it. (Run "run.bat rebuild" to force a fresh build.)
if /i not "%~1"=="rebuild" if exist "%EXE%" goto :start

call :check_sdk
if not defined HAVE_SDK (
    echo.
    echo  The .NET 8 SDK is needed to build the app the first time.
    echo  ^(The .NET *Runtime* alone is not enough - it must be the SDK.^)
    echo.
    where winget >nul 2>nul
    if not errorlevel 1 (
        choice /c YN /m " Install the .NET 8 SDK now with winget"
        if not errorlevel 2 (
            winget install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements --accept-source-agreements
            echo.
            echo  Done. Close this window and double-click run.bat again.
            pause
            exit /b 0
        )
    )
    echo  Download "SDK 8.0 - Windows x64 Installer" from the page that is opening,
    echo  install it, then double-click run.bat again.
    start "" "https://dotnet.microsoft.com/download/dotnet/8.0"
    pause
    exit /b 1
)

echo Building Fortnite Edit Input (first run takes a minute)...
if exist "%EXE%" del /q "%EXE%"
dotnet publish "src\EditInput.App\EditInput.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "publish"
if not exist "%EXE%" (
    echo.
    echo  Build failed. See the messages above.
    pause
    exit /b 1
)

:start
echo Starting Fortnite Edit Input...
start "" "%EXE%"
exit /b 0

:check_sdk
set "HAVE_SDK="
where dotnet >nul 2>nul || exit /b 0
for /f "tokens=1 delims=." %%v in ('dotnet --list-sdks 2^>nul') do (
    if %%v GEQ 8 set "HAVE_SDK=1"
)
exit /b 0
