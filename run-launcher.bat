@echo off
setlocal
set ROOT=%~dp0
set DLL=

for %%D in (
    "%ROOT%launcher\publish\ComfyLauncher.dll"
    "%ROOT%launcher\ComfyLauncher\bin\Release\net8.0\ComfyLauncher.dll"
    "%ROOT%launcher\ComfyLauncher\bin\Debug\net8.0\ComfyLauncher.dll"
) do (
    if not defined DLL if exist %%D set DLL=%%~fD
)

if defined DLL (
    where dotnet >nul 2>&1
    if not errorlevel 1 (
        powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%ROOT%scripts\launch-ui.ps1" -Dll "%DLL%"
        exit /b 0
    )
)

set EXE=
for %%E in (
    "%ROOT%launcher\publish\ComfyLauncher.exe"
    "%ROOT%launcher\ComfyLauncher\bin\Release\net8.0\ComfyLauncher.exe"
    "%ROOT%launcher\ComfyLauncher\bin\Debug\net8.0\ComfyLauncher.exe"
) do (
    if not defined EXE if exist %%E set EXE=%%~fE
)

if defined EXE (
    powershell -NoProfile -WindowStyle Hidden -Command "Unblock-File -LiteralPath '%EXE%'" >nul 2>&1
    start "" "%EXE%"
    exit /b 0
)

echo Launcher build not found, running dotnet run...
dotnet run --project "%ROOT%launcher\ComfyLauncher\ComfyLauncher.csproj" -c Release
