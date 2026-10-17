@echo off
setlocal
cd /d "%~dp0.."

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [erro] .NET SDK nao encontrado. Instale com: winget install Microsoft.DotNet.SDK.8
  pause
  exit /b 1
)

set "ISCC="
for %%P in (
  "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
  "%ProgramFiles%\Inno Setup 6\ISCC.exe"
  "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
) do if exist %%P set "ISCC=%%~P"
if not defined ISCC (
  echo [erro] Inno Setup 6 nao encontrado. Instale com: winget install JRSoftware.InnoSetup
  pause
  exit /b 1
)

rem A versao vem do TaskbarMonitor.csproj, a mesma usada pelo release.bat.
set "VER="
for /f "delims=" %%v in ('powershell -NoProfile -Command "([xml](Get-Content TaskbarMonitor.csproj)).Project.PropertyGroup.Version | Where-Object { $_ }"') do set "VER=%%v"
if not defined VER (
  echo [erro] Nao consegui ler a versao do TaskbarMonitor.csproj.
  pause
  exit /b 1
)

echo Compilando v%VER%...
dotnet publish -c Release -o publish
if errorlevel 1 (
  echo [falhou] Publish com erro.
  pause
  exit /b 1
)

echo Gerando instalador...
"%ISCC%" /DAppVersion=%VER% installer\TaskbarMonitor.iss
if errorlevel 1 (
  echo [falhou] Erro ao gerar o instalador.
  pause
  exit /b 1
)

rem Mantem so o instalador da versao atual: apaga os setups antigos (so depois de gerar o novo com sucesso).
for %%F in (release\TaskbarMonitor-Setup-*.exe) do (
  if /i not "%%~nxF"=="TaskbarMonitor-Setup-%VER%.exe" (
    del "%%F" >nul 2>nul
    if exist "%%F" (
      echo [aviso] Nao consegui apagar %%~nxF - feche o instalador aberto e apague manualmente.
    ) else (
      echo Removido: %%~nxF
    )
  )
)

echo.
echo Pronto: %CD%\release\TaskbarMonitor-Setup-%VER%.exe
pause
endlocal
