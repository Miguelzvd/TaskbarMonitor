@echo off
setlocal
cd /d "%~dp0"

set "VER=%~1"
if "%VER%"=="" (
  echo Uso: release.bat X.Y.Z   ex.: release.bat 1.1.0
  exit /b 1
)

echo %VER%| findstr /r "^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$" >nul
if errorlevel 1 (
  echo [erro] Versao invalida "%VER%". Use o formato X.Y.Z, ex.: 1.1.0
  exit /b 1
)

set "BR="
for /f "delims=" %%b in ('git branch --show-current') do set "BR=%%b"
if /i not "%BR%"=="main" (
  echo [erro] Publique a partir da branch main. Branch atual: %BR%
  exit /b 1
)

for /f "delims=" %%i in ('git status --porcelain') do (
  echo [erro] Ha alteracoes nao commitadas. Commit ou descarte antes de publicar.
  exit /b 1
)

git rev-parse -q --verify "refs/tags/v%VER%" >nul 2>nul
if not errorlevel 1 (
  echo [erro] A tag v%VER% ja existe.
  exit /b 1
)

rem /l deixa explicito que a busca e literal, sem regex.
findstr /l /c:"## [%VER%]" CHANGELOG.md >nul 2>nul
if errorlevel 1 (
  echo [erro] CHANGELOG.md nao tem a secao "## [%VER%]". Escreva-a, faca commit e rode de novo.
  exit /b 1
)

echo Atualizando versao para %VER%...
rem WriteAllText grava UTF-8 sem BOM e ReadAllText preserva o CRLF do arquivo.
powershell -NoProfile -Command "$p='TaskbarMonitor.csproj'; $t=[IO.File]::ReadAllText($p) -replace '<Version>[^<]*</Version>', '<Version>%VER%</Version>'; [IO.File]::WriteAllText($p, $t)"
if errorlevel 1 goto fail

set "OUT=release\TaskbarMonitor-%VER%"
if exist "%OUT%" rmdir /s /q "%OUT%"
echo Gerando publish em %OUT%...
dotnet publish -c Release -r win-x64 --self-contained false -o "%OUT%"
if errorlevel 1 goto fail

echo Gerando zip...
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%OUT%-win-x64.zip' -Force"
if errorlevel 1 goto fail

git diff --quiet -- TaskbarMonitor.csproj
if errorlevel 1 (
  git add TaskbarMonitor.csproj
  git commit -m "chore: release v%VER%"
  if errorlevel 1 goto fail
)

git tag -a "v%VER%" -m "v%VER%"
if errorlevel 1 goto fail

echo Enviando branch e tag ao GitHub...
git push origin main
if errorlevel 1 (
  echo [erro] Falha no push da branch. A tag local v%VER% foi criada. Depois de resolver, rode: git push origin main e git push origin v%VER%
  exit /b 1
)
git push origin "v%VER%"
if errorlevel 1 (
  echo [erro] Falha no push da tag. Rode: git push origin v%VER%
  exit /b 1
)

echo.
echo Pronto: release\TaskbarMonitor-%VER%-win-x64.zip
echo O GitHub Actions cria a release v%VER% automaticamente.
endlocal
exit /b 0

:fail
echo [falhou] Publicacao cancelada. Restaurando TaskbarMonitor.csproj.
git checkout -- TaskbarMonitor.csproj
endlocal
exit /b 1
