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

rem Instancia unica: o exe em execucao fica travado e o publish falharia.
tasklist /fi "imagename eq TaskbarMonitor.exe" | find /i "TaskbarMonitor.exe" >nul
if not errorlevel 1 (
  echo [aviso] O TaskbarMonitor esta aberto e trava o publish. Feche-o pelo menu do botao direito, opcao Sair, e rode de novo.
  pause
  exit /b 1
)

rem Limpa a saida anterior para nao sobrar DLL de builds antigos.
if exist publish rmdir /s /q publish

dotnet publish -c Release -o publish > build.log 2>&1
set ERR=%ERRORLEVEL%
type build.log

echo.
if %ERR% neq 0 (
  echo [falhou] Detalhes em: %~dp0build.log
) else (
  echo Pronto: %~dp0publish\TaskbarMonitor.exe  ^(arquivo unico^)
)
pause
endlocal
