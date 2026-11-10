@echo off
setlocal
rem Compila e publica o Display Dimmer em .\dist
where dotnet >nul 2>nul
if errorlevel 1 (
    echo .NET SDK nao encontrado no PATH. Instale o .NET 8 SDK: https://dot.net
    exit /b 1
)
dotnet publish "%~dp0src\DisplayDimmer\DisplayDimmer.csproj" -c Release -r win-x64 --self-contained false -o "%~dp0dist"
if errorlevel 1 (
    echo Falha na publicacao.
    exit /b 1
)
echo.
echo Pronto: %~dp0dist\DisplayDimmer.exe
endlocal
