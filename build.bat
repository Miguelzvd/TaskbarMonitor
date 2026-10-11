@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul || (
  echo [erro] .NET SDK nao encontrado. Instale com:  winget install Microsoft.DotNet.SDK.8
  pause
  exit /b 1
)

echo SDKs instalados:
dotnet --list-sdks
echo.

dotnet publish -c Release -r win-x64 --self-contained false -o publish > build.log 2>&1
set ERR=%ERRORLEVEL%
type build.log

echo.
if %ERR% neq 0 (
  echo [falhou] Detalhes em: %~dp0build.log
) else (
  echo Pronto: %~dp0publish\TaskbarMonitor.exe
)
pause
endlocal
