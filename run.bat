@echo off
setlocal
title Fortnite Edit Input
cd /d "%~dp0"

set "EXE=%~dp0publish\FortniteEditInput.exe"

rem Already built? Just start it. (Run "run.bat rebuild" to force a fresh build.)
if /i not "%~1"=="rebuild" if exist "%EXE%" goto :start

where dotnet >nul 2>nul
if errorlevel 1 (
    echo.
    echo  The .NET 8 SDK is required to build the app the first time.
    echo  Download it from: https://dotnet.microsoft.com/download/dotnet/8.0
    echo  Install it, then run this file again.
    echo.
    start "" "https://dotnet.microsoft.com/download/dotnet/8.0"
    pause
    exit /b 1
)

echo Building Fortnite Edit Input (first run takes a minute)...
dotnet publish "src\EditInput.App\EditInput.App.csproj" -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "publish"
if errorlevel 1 (
    echo.
    echo  Build failed. See the messages above.
    pause
    exit /b 1
)

:start
echo Starting Fortnite Edit Input...
start "" "%EXE%"
exit /b 0
