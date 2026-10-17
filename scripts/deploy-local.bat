@echo off
setlocal
cd /d "%~dp0.."

net session >nul 2>&1
if errorlevel 1 (
  echo [erro] Execute como administrador: botao direito no arquivo, Executar como administrador.
  pause
  exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [erro] .NET SDK nao encontrado. Instale com: winget install Microsoft.DotNet.SDK.8
  pause
  exit /b 1
)

set "DEST=%ProgramFiles%\TaskbarMonitor"
set "STAGE=%TEMP%\TaskbarMonitor-deploy"
if exist "%STAGE%" rmdir /s /q "%STAGE%"

echo Compilando...
dotnet publish -c Release -o "%STAGE%"
if errorlevel 1 (
  echo [falhou] Publish com erro. Nada foi alterado na instalacao.
  pause
  exit /b 1
)

echo Fechando o app...
taskkill /f /im TaskbarMonitor.exe >nul 2>&1

echo Copiando para %DEST%...
robocopy "%STAGE%" "%DEST%" /E /NFL /NDL /NJH /NJS /NP
if errorlevel 8 (
  echo [falhou] Erro ao copiar os arquivos.
  pause
  exit /b 1
)

rem Limpa a pasta temporaria da publicacao.
rmdir /s /q "%STAGE%"

schtasks /Query /TN TaskbarMonitor >nul 2>&1
if not errorlevel 1 (
  schtasks /Change /TN TaskbarMonitor /TR "\"%DEST%\TaskbarMonitor.exe\"" >nul
)

start "" "%DEST%\TaskbarMonitor.exe"
echo Pronto: %DEST%
endlocal
