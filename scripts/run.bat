@echo off
setlocal
cd /d "%~dp0.."

rem Instancia unica: se o app instalado estiver aberto, o exe de Debug fecharia sem aviso.
tasklist /fi "imagename eq TaskbarMonitor.exe" | find /i "TaskbarMonitor.exe" >nul
if not errorlevel 1 (
  echo [aviso] O TaskbarMonitor ja esta aberto. Feche-o pelo menu do botao direito, opcao Sair, e rode de novo.
  pause
  exit /b 1
)

dotnet build -c Debug
if errorlevel 1 (
  echo [falhou] Build de Debug com erro.
  pause
  exit /b 1
)

rem O app exige administrador pelo manifest, entao o exe e iniciado elevado.
powershell -NoProfile -WindowStyle Hidden -Command "Start-Process -FilePath '%CD%\bin\Debug\net8.0-windows\TaskbarMonitor.exe' -Verb RunAs"
endlocal
