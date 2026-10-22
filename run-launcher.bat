@echo off
setlocal
set ROOT=%~dp0
set EXE=

if exist "%ROOT%launcher\ComfyLauncher\bin\Release\net8.0\ComfyLauncher.exe" set EXE=%ROOT%launcher\ComfyLauncher\bin\Release\net8.0\ComfyLauncher.exe
if not defined EXE if exist "%ROOT%launcher\publish\ComfyLauncher.exe" set EXE=%ROOT%launcher\publish\ComfyLauncher.exe
if not defined EXE if exist "%ROOT%launcher\ComfyLauncher\bin\Debug\net8.0\ComfyLauncher.exe" set EXE=%ROOT%launcher\ComfyLauncher\bin\Debug\net8.0\ComfyLauncher.exe

if defined EXE (
    start "" "%EXE%"
    exit /b 0
)

echo Launcher build not found, running dotnet run...
dotnet run --project "%ROOT%launcher\ComfyLauncher\ComfyLauncher.csproj" -c Release
