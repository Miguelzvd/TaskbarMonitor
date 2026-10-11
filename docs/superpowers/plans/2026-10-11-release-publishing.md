# Plano de publicação de versões Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir publicar uma nova versão do TaskbarMonitor com um único comando (`release.bat X.Y.Z`), gerando uma release no GitHub com um instalador (`Setup.exe`) que atualiza por cima de forma fácil, e ter um `deploy-local.bat` para atualizar o PC de desenvolvimento com um duplo clique.

**Architecture:** O número da versão fica em `<Version>` do `TaskbarMonitor.csproj`, e o histórico fica em `CHANGELOG.md`. O `release.bat` valida branch, árvore, versão e changelog, atualiza o csproj, gera o zip local, cria o commit e a tag `vX.Y.Z` e envia ambos ao GitHub. Um workflow do GitHub Actions dispara com a tag, compila, gera o zip e o instalador (Inno Setup) e cria a release com os dois anexados e as notas tiradas do changelog. O instalador instala em `%ProgramFiles%\TaskbarMonitor` (caminho fixo), fecha o app em execução, atualiza os arquivos e reaponta a tarefa de inicialização automática, se existir. Auto-update dentro do app fica fora do escopo, por causa do repositório privado.

**Tech Stack:** Windows batch (`.bat`), PowerShell 5.1 (já presente no Windows), .NET 8 SDK, Git, GitHub Actions e Inno Setup 6 (instalado no runner via Chocolatey; localmente é opcional).

**Spec:** Nenhum spec formal. Escopo definido na conversa: publicação fácil a cada atualização, com o repositório mantido **privado** por enquanto.

## Global Constraints

- Repositório: `https://github.com/miguelzvd/TaskbarMonitor.git`, visibilidade **privada** (não alterar sem pedido explícito).
- Branch de release: `main`.
- Formato de versão: exatamente `X.Y.Z` (números). Sem prefixo `v` no argumento, sem sufixos como `-beta`.
- Tag de release: `vX.Y.Z`. Nome do zip, local e na release: `TaskbarMonitor-X.Y.Z-win-x64.zip` (sem `v`).
- Build framework-dependent (`--self-contained false`), igual ao `build.bat` atual: quem usa o zip precisa do .NET 8 Desktop Runtime.
- `.bat` e `.cs` usam **CRLF**. Ferramentas de escrita podem gravar LF; batch com LF quebra `goto` e rótulos. Depois de criar ou editar qualquer `.bat`, normalizar com `sed -i 's/\r*$/\r/' <arquivo>` (Git Bash) e conferir com `grep -c $'\r' <arquivo>` igual a `wc -l < <arquivo>`.
- O `TaskbarMonitor.csproj` hoje é UTF-8 **sem BOM** e CRLF. Qualquer alteração automática deve preservar os dois.
- Dentro de blocos `( ... )` de batch, textos de `echo` não podem conter `)`, porque isso fecha o bloco.
- Mensagens dos scripts em português, sem acentos (o console do cmd pode não usar UTF-8), no mesmo tom do `build.bat`.
- `release/` e `publish/` ficam fora do Git (`.gitignore`).
- Pasta de instalação padrão, usada pelo instalador e pelo `deploy-local.bat`: `%ProgramFiles%\TaskbarMonitor`. Nome da tarefa agendada de inicialização: `TaskbarMonitor` (definido em `Autostart.cs`; o instalador não recria a tarefa, só troca o caminho do exe com `schtasks /Change /TR`).
- Nome do instalador: `TaskbarMonitor-Setup-X.Y.Z.exe`, gerado em `release\`. O `AppId` do Inno Setup é fixo e nunca muda, senão cada versão vira uma instalação separada.
- O `.iss` usa só texto ASCII (sem acentos), para evitar problemas de codificação do Inno Setup.
- `deploy-local.bat` copia com `robocopy /E` (nunca `/MIR`), para não apagar o `unins000.exe` do instalador.
- Detecção do .NET 8 pelo sistema de arquivos (`dotnet\shared\Microsoft.WindowsDesktop.App\8.*`), porque a chave de registro equivalente não existe nesta máquina mesmo com o runtime instalado.
- Não usar o `gh` CLI localmente. Ele não está no PATH nesta máquina, e a release é criada no GitHub Actions.
- Repositório privado consome minutos do GitHub Actions. Runners Windows contam em dobro; cada release leva poucos minutos e cabe com folga na cota gratuita.

## Review Focus

1. **Release com alterações não commitadas:** a tag apontaria para código diferente do que foi testado. Esperado: o script recusa, antes de mexer em qualquer arquivo. (Task 2, Step 3)
2. **Versão mal formatada** (`1.2`, `v1.2.0`, `1.2.0-beta`): geraria nome de zip e tag quebrados. Esperado: o script recusa com mensagem clara. (Task 2, Step 2)
3. **Tag já existente ou branch errada:** o push falharia no meio, ou a tag sairia de outra branch. Esperado: o script recusa antes de alterar o csproj. (Task 2, Step 4)
4. **App já aberto** (instância instalada rodando pela inicialização automática): o mutex de instância única faz o exe de Debug fechar em silêncio, e a atualização falha com o exe travado. Esperado: o `run.bat` avisa e para; o README manda fechar o app antes de atualizar. (Task 1, Step 3; Task 4)
5. **Versão publicada sem entrada no changelog:** a release ficaria sem notas. Esperado: o script recusa se não houver a seção `## [X.Y.Z]` no `CHANGELOG.md`, e o workflow falha se as notas vierem vazias. (Task 2, Step 4; Task 3)
6. **Inicialização automática apontando para um caminho que não existe mais:** já acontece hoje. A tarefa `TaskbarMonitor` aponta para `C:\Users\dilso\Downloads\TaskbarMonitor\publish\TaskbarMonitor.exe`, pasta que não existe mais, então o app não abriria no próximo logon. Esperado: o instalador troca o caminho da tarefa para a pasta de instalação. (Task 5, Step 4; Task 7, Step 6)
7. **Instalar por cima com o app aberto:** o exe fica travado e a cópia falha. Esperado: o instalador e o `deploy-local.bat` encerram o processo antes de copiar. (Task 5; Task 6)
8. **Máquina sem o .NET 8 Desktop Runtime:** o app não abre e o usuário não sabe por quê. Esperado: o instalador recusa e mostra o comando `winget` para instalar. (Task 5)

---

### Task 1: Ignorar a saída de release, versionar o plano e criar o `run.bat`

**Files:**
- Modify: `.gitignore`
- Create: `run.bat`
- Add ao Git: `docs/superpowers/plans/2026-10-11-release-publishing.md` (este plano; hoje não rastreado, e o `release.bat` recusa árvore suja)

**Interfaces:**
- Consumes: nenhum
- Produces: `run.bat` (executável de desenvolvimento em `bin\Debug\net8.0-windows\TaskbarMonitor.exe`)

- [ ] **Step 1: Adicionar `release/` ao `.gitignore`**

Acrescentar ao final de `.gitignore`:

```gitignore
release/
```

- [ ] **Step 2: Criar `run.bat`**

Conteúdo completo de `run.bat`:

```bat
@echo off
setlocal
cd /d "%~dp0"

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
powershell -NoProfile -WindowStyle Hidden -Command "Start-Process -FilePath '%~dp0bin\Debug\net8.0-windows\TaskbarMonitor.exe' -Verb RunAs"
endlocal
```

Depois normalizar para CRLF:

```bash
sed -i 's/\r*$/\r/' run.bat
```

- [ ] **Step 3: Testar o `run.bat`**

Com o app instalado aberto, executar `run.bat`.
Expected: `[aviso] O TaskbarMonitor ja esta aberto...` e nada é compilado.

Fechar o app (botão direito → Sair) e executar `run.bat` de novo.
Expected: build sem erro, pedido de UAC e, ao aceitar, o monitor aparece na barra de tarefas.

- [ ] **Step 4: Commit**

```bash
git add .gitignore run.bat docs/superpowers/plans/2026-10-11-release-publishing.md
git commit -m "chore: adiciona run.bat e plano de publicacao de versoes"
```

---

### Task 2: Criar o `release.bat`

**Files:**
- Create: `release.bat`
- Modify: `TaskbarMonitor.csproj` (apenas o valor de `<Version>`, feito pelo próprio script)

**Interfaces:**
- Consumes: seção `## [X.Y.Z]` em `CHANGELOG.md` (Task 4), commitada antes de rodar o script.
- Produces: zip `release\TaskbarMonitor-X.Y.Z-win-x64.zip`, commit `chore: release vX.Y.Z` (só se a versão mudou), tag `vX.Y.Z` enviada ao `origin`.

- [ ] **Step 1: Criar `release.bat`**

Conteúdo completo:

```bat
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
findstr /l /c:"## [%VER%]" CHANGELOG.md >nul
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
```

Depois normalizar para CRLF:

```bash
sed -i 's/\r*$/\r/' release.bat
```

- [ ] **Step 2: Testar as validações de argumento (sem publicar nada)**

Run: `release.bat`
Expected: `Uso: release.bat X.Y.Z   ex.: release.bat 1.1.0` e código de saída 1.

Run: `release.bat 1.2`
Expected: `[erro] Versao invalida "1.2"...`.

Run: `release.bat v1.2.0`
Expected: mesma mensagem de versão inválida.

Run: `release.bat 1.2.0-beta`
Expected: mesma mensagem de versão inválida.

- [ ] **Step 3: Testar a recusa com alterações pendentes**

Neste ponto o próprio `release.bat` ainda não foi commitado, então a árvore está suja.
Run: `release.bat 9.9.9`
Expected: `[erro] Ha alteracoes nao commitadas...` e o `TaskbarMonitor.csproj` sem mudanças (`git diff --quiet -- TaskbarMonitor.csproj` retorna 0).

- [ ] **Step 4: Testar a busca literal no changelog e a troca de versão, isoladamente**

Criar um arquivo temporário e testar o `findstr`:

```bat
echo ## [9.9.9] - teste> %TEMP%\cl.md
findstr /l /c:"## [9.9.9]" %TEMP%\cl.md
findstr /l /c:"## [9.9.8]" %TEMP%\cl.md
del %TEMP%\cl.md
```

Expected: o primeiro `findstr` imprime a linha (código 0); o segundo não imprime nada (código 1).

Testar a troca de versão sem gravar:

```bat
powershell -NoProfile -Command "[IO.File]::ReadAllText('TaskbarMonitor.csproj') -replace '<Version>[^<]*</Version>', '<Version>9.9.9</Version>' | Select-String '<Version>'"
```

Expected: mostra `<Version>9.9.9</Version>`, e `git status` continua sem mudança no csproj.

- [ ] **Step 5: Commit**

```bash
git add release.bat
git commit -m "chore: adiciona release.bat para publicar versoes"
```

---

### Task 3: Criar o workflow de release no GitHub Actions

**Files:**
- Create: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: tag `vX.Y.Z` enviada pelo `release.bat` (Task 2); seção correspondente em `CHANGELOG.md` (Task 4).
- Produces: release `vX.Y.Z` no GitHub com `TaskbarMonitor-X.Y.Z-win-x64.zip` anexado.

- [ ] **Step 1: Criar `.github/workflows/release.yml`**

Conteúdo completo:

```yaml
name: Release

on:
  push:
    tags:
      - 'v*'

permissions:
  contents: write

jobs:
  release:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Publicar, gerar zip e notas
        shell: pwsh
        run: |
          $ver = "${{ github.ref_name }}".TrimStart('v')

          dotnet publish -c Release -r win-x64 --self-contained false -o publish
          if ($LASTEXITCODE -ne 0) { exit 1 }

          Compress-Archive -Path 'publish\*' -DestinationPath "TaskbarMonitor-$ver-win-x64.zip" -Force

          $text = Get-Content CHANGELOG.md -Raw
          $pattern = "(?s)## \[$([regex]::Escape($ver))\][^\n]*\n(.*?)(?=\n## \[|\z)"
          $notes = [regex]::Match($text, $pattern).Groups[1].Value.Trim()
          if (-not $notes) {
            Write-Error "CHANGELOG.md sem notas para a versao $ver"
            exit 1
          }
          Set-Content -Path notes.md -Value $notes -Encoding utf8
          "VER=$ver" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8

      - name: Criar release
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
        run: gh release create "${{ github.ref_name }}" "TaskbarMonitor-$env:VER-win-x64.zip" --title "${{ github.ref_name }}" --notes-file notes.md
```

- [ ] **Step 2: Conferir o arquivo**

Run: `git diff --stat` e abrir o arquivo para revisar a indentação (YAML usa só espaços).
Expected: nenhum caractere de tabulação (`grep -c $'\t' .github/workflows/release.yml` retorna `0`). A validação completa acontece na aba Actions do GitHub, na Task 5.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: cria release no GitHub ao enviar tag vX.Y.Z"
```

---

### Task 4: Criar o `CHANGELOG.md` e as seções de publicação no README

**Files:**
- Create: `CHANGELOG.md`
- Modify: `README.md` (novas seções "Rodar em desenvolvimento", "Publicar uma versão" e "Atualizar uma instalação", antes de "Limitações conhecidas")

**Interfaces:**
- Consumes: nenhum
- Produces: seção `## [1.0.0] - 2026-10-11` lida pelo `release.bat` (Task 2) e pelo workflow (Task 3).

- [ ] **Step 1: Criar `CHANGELOG.md`**

Conteúdo completo:

```markdown
# Changelog

Cada versão tem uma seção `## [X.Y.Z] - AAAA-MM-DD`. O `release.bat` exige essa seção antes de publicar, e o texto dela vira as notas da release no GitHub.

## [1.0.0] - 2026-10-11

- Monitor de CPU, GPU, VRAM e RAM ancorado na barra de tarefas.
- Modos fixo (direita, centro ou esquerda) e livre (arrastar e travar).
- Alerta visual de cor para uso e temperatura altos.
- Inicialização automática com o Windows via tarefa agendada elevada.
```

- [ ] **Step 2: Adicionar as seções no `README.md`**

Inserir antes de `## Limitações conhecidas`:

````markdown
## Rodar em desenvolvimento

```bat
run.bat
```

Compila em Debug e abre o app como administrador. Se a versão instalada estiver aberta, feche-a antes (botão direito → Sair), porque o app só permite uma instância.

## Publicar uma versão

1. Escreva a nova seção no `CHANGELOG.md` (ex.: `## [1.1.0] - AAAA-MM-DD`) e faça commit.
2. Na branch `main`, rode a partir da pasta do projeto:

   ```bat
   release.bat 1.1.0
   ```

O script valida a versão, a branch e o changelog, atualiza o `TaskbarMonitor.csproj`, gera `release\TaskbarMonitor-1.1.0-win-x64.zip`, cria o commit e a tag `v1.1.0` e envia ao GitHub. O GitHub Actions então cria a release com o zip anexado. Como o repositório é privado, a release fica visível só para quem tem acesso a ele.

## Atualizar uma instalação

1. Feche o app pelo menu do botão direito (Sair).
2. Extraia o zip da release por cima da pasta de instalação (ex.: `C:\Tools\TaskbarMonitor`), substituindo os arquivos.
3. Abra o `TaskbarMonitor.exe`.

As configurações ficam em `%AppData%\TaskbarMonitor\settings.json` e não são afetadas. A tarefa de inicialização automática continua válida, já que a pasta não muda. O zip exige o .NET 8 Desktop Runtime instalado.
````

- [ ] **Step 3: Verificar**

Run: `findstr /l /c:"## [1.0.0]" CHANGELOG.md`
Expected: uma linha `## [1.0.0] - 2026-10-11`.

- [ ] **Step 4: Commit**

```bash
git add CHANGELOG.md README.md
git commit -m "docs: adiciona changelog e instrucoes de desenvolvimento, publicacao e atualizacao"
```

---

### Task 5: Criar o instalador (Inno Setup)

**Files:**
- Create: `installer/TaskbarMonitor.iss`

**Interfaces:**
- Consumes: pasta `publish\` (saída de `dotnet publish -o publish`), definição `AppVersion` passada por linha de comando (`/DAppVersion=X.Y.Z`).
- Produces: `release\TaskbarMonitor-Setup-X.Y.Z.exe`.

- [ ] **Step 1: Criar `installer/TaskbarMonitor.iss`**

Conteúdo completo (texto ASCII, sem acentos):

```iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{B3F1C7A2-5D8E-4A6B-9C21-7E4F0A9D3B58}
AppName=Taskbar Monitor
AppVersion={#AppVersion}
AppPublisher=miguelzvd
DefaultDirName={autopf}\TaskbarMonitor
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\release
OutputBaseFilename=TaskbarMonitor-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Taskbar Monitor
CloseApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Taskbar Monitor"; Filename: "{app}\TaskbarMonitor.exe"

[Run]
Filename: "{app}\TaskbarMonitor.exe"; Description: "Abrir o Taskbar Monitor"; Flags: nowait postinstall skipifsilent

; O Inno Setup executa o [UninstallRun] de baixo para cima: o taskkill roda antes de apagar a tarefa.
[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN TaskbarMonitor /F"; Flags: runhidden; RunOnceId: "DelTask"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM TaskbarMonitor.exe"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
function IsDotNet8DesktopInstalled(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNet8DesktopInstalled() then
  begin
    MsgBox('O Taskbar Monitor precisa do .NET 8 Desktop Runtime.' + #13#10 +
           'Instale com: winget install Microsoft.DotNet.DesktopRuntime.8' + #13#10 +
           'e rode este instalador de novo.', mbError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM TaskbarMonitor.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    { Se a inicializacao automatica estava ativa, aponta a tarefa para o exe instalado. }
    if Exec(ExpandConstant('{sys}\schtasks.exe'), '/Query /TN TaskbarMonitor', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
       and (ResultCode = 0) then
      Exec(ExpandConstant('{sys}\schtasks.exe'),
           ExpandConstant('/Change /TN TaskbarMonitor /TR "\"{app}\TaskbarMonitor.exe\""'),
           '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
```

- [ ] **Step 2: Instalar o Inno Setup localmente (opcional, para testar antes da CI)**

Run: `winget install JRSoftware.InnoSetup`
Expected: instalação concluída. O compilador fica em `C:\Program Files (x86)\Inno Setup 6\ISCC.exe` (ou em `%LocalAppData%\Programs\Inno Setup 6\`, conforme a instalação). Pular este passo se preferir validar só pela CI.

- [ ] **Step 3: Compilar o instalador de teste**

Run (na pasta do projeto, depois de gerar `publish\` com `build.bat`):

```bat
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DAppVersion=0.0.1 installer\TaskbarMonitor.iss
```

Expected: `Successful compile` e o arquivo `release\TaskbarMonitor-Setup-0.0.1.exe`.

- [ ] **Step 4: Testar a instalação neste PC**

Executar `release\TaskbarMonitor-Setup-0.0.1.exe`.
Expected, nesta ordem:
- o instalador pede UAC (administrador) e oferece a pasta `C:\Program Files\TaskbarMonitor`;
- se o app estava aberto, ele é encerrado sozinho;
- ao final, o app abre pela opção "Abrir o Taskbar Monitor";
- o comando abaixo mostra o caminho novo, e não mais o de `Downloads`:

```bat
schtasks /Query /TN TaskbarMonitor /XML | findstr /i "Command"
```

Expected: `<Command>C:\Program Files\TaskbarMonitor\TaskbarMonitor.exe</Command>`.

- [ ] **Step 5: Testar a atualização por cima e a desinstalação**

Compilar `/DAppVersion=0.0.2` e executar o novo instalador com o app aberto.
Expected: o app fecha, os arquivos são trocados na mesma pasta e o app reabre. Em "Aplicativos instalados" do Windows aparece uma única entrada "Taskbar Monitor".

Desinstalar pelo Windows.
Expected: o app fecha, a pasta é removida e `schtasks /Query /TN TaskbarMonitor` retorna erro (a tarefa foi apagada). Depois, reinstalar a versão que será usada de verdade (Task 7).

- [ ] **Step 6: Commit**

```bash
git add installer/TaskbarMonitor.iss
git commit -m "feat: adiciona instalador Inno Setup"
```

---

### Task 6: Gerar o instalador no workflow e criar o `deploy-local.bat`

**Files:**
- Modify: `.github/workflows/release.yml`
- Create: `deploy-local.bat`
- Modify: `README.md`

**Interfaces:**
- Consumes: `installer/TaskbarMonitor.iss` (Task 5).
- Produces: release com `TaskbarMonitor-X.Y.Z-win-x64.zip` e `TaskbarMonitor-Setup-X.Y.Z.exe`; `deploy-local.bat` para atualizar a instalação deste PC.

- [ ] **Step 1: Ajustar o workflow**

Em `.github/workflows/release.yml`, depois do `Compress-Archive` e antes de extrair as notas, o passo "Publicar, gerar zip e notas" passa a se chamar "Publicar, gerar zip, instalador e notas" e ganha estas linhas:

```yaml
          choco install innosetup -y --no-progress
          if ($LASTEXITCODE -ne 0) { exit 1 }
          & "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" "/DAppVersion=$ver" installer\TaskbarMonitor.iss
          if ($LASTEXITCODE -ne 0) { exit 1 }
```

E o último passo passa a anexar os dois arquivos:

```yaml
        run: gh release create "${{ github.ref_name }}" "TaskbarMonitor-$env:VER-win-x64.zip" "release\TaskbarMonitor-Setup-$env:VER.exe" --title "${{ github.ref_name }}" --notes-file notes.md
```

- [ ] **Step 2: Criar `deploy-local.bat`**

Conteúdo completo:

```bat
@echo off
setlocal
cd /d "%~dp0"

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
dotnet publish -c Release -r win-x64 --self-contained false -o "%STAGE%"
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

schtasks /Query /TN TaskbarMonitor >nul 2>&1
if not errorlevel 1 (
  schtasks /Change /TN TaskbarMonitor /TR "\"%DEST%\TaskbarMonitor.exe\"" >nul
)

start "" "%DEST%\TaskbarMonitor.exe"
echo Pronto: %DEST%
endlocal
```

Depois normalizar para CRLF:

```bash
sed -i 's/\r*$/\r/' deploy-local.bat
```

- [ ] **Step 3: Testar o `deploy-local.bat`**

Executar `deploy-local.bat` em um terminal comum (sem administrador).
Expected: `[erro] Execute como administrador...`.

Executar como administrador.
Expected: build, o app fecha e reabre, os arquivos aparecem em `C:\Program Files\TaskbarMonitor` e o `schtasks /Query /TN TaskbarMonitor /XML | findstr /i Command` mostra o caminho novo.

- [ ] **Step 4: Atualizar o `README.md`**

Na seção "Build", trocar o parágrafo que manda mover a pasta `publish` por:

```markdown
Para instalar de forma fixa (necessário para a inicialização automática), use o instalador da release ou rode `deploy-local.bat` como administrador. Ambos instalam em `C:\Program Files\TaskbarMonitor`.
```

Na seção "Atualizar uma instalação", substituir os 3 passos por:

```markdown
1. Baixe `TaskbarMonitor-Setup-X.Y.Z.exe` da release (repositório privado: é preciso estar logado no GitHub).
2. Execute o instalador. Ele fecha o app, atualiza os arquivos e mantém a inicialização automática funcionando.

No PC de desenvolvimento, `deploy-local.bat` (como administrador) faz o mesmo a partir do código-fonte.

As configurações ficam em `%AppData%\TaskbarMonitor\settings.json` e não são afetadas. O instalador exige o .NET 8 Desktop Runtime.
```

Também mencionar o zip como alternativa portátil: "O zip da release continua disponível para uso sem instalação, mas a inicialização automática guarda o caminho do exe, então extraia em uma pasta fixa."

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/release.yml deploy-local.bat README.md
git commit -m "feat: gera instalador na release e adiciona deploy-local.bat"
```

---

### Task 7: Primeira release (v1.0.0) e verificação ponta a ponta

**Files:** nenhum arquivo novo. Apenas execução e verificação.

**Interfaces:**
- Consumes: tudo das Tasks 1 a 6, já commitado em `main`.
- Produces: tag `v1.0.0` e release `v1.0.0` no GitHub.

- [ ] **Step 1: Confirmar branch e árvore limpa**

Run: `git status`
Expected: `On branch main` e `nothing to commit, working tree clean`.

- [ ] **Step 2: Rodar o release com a versão atual**

Run: `release.bat 1.0.0`
Expected: o csproj já está em `1.0.0`, então não há commit de release (e `git diff` do csproj continua vazio, sem BOM nem troca de quebra de linha). O script cria a tag `v1.0.0`, envia `main` e `v1.0.0` e termina com `Pronto: release\TaskbarMonitor-1.0.0-win-x64.zip`.

- [ ] **Step 3: Verificar o workflow**

Abrir `https://github.com/miguelzvd/TaskbarMonitor/actions` (conta logada).
Expected: execução "Release" concluída com sucesso.

- [ ] **Step 4: Verificar a release**

Abrir `https://github.com/miguelzvd/TaskbarMonitor/releases`.
Expected: release `v1.0.0` com `TaskbarMonitor-1.0.0-win-x64.zip` e `TaskbarMonitor-Setup-1.0.0.exe` anexados, e as notas da seção 1.0.0 do changelog.

- [ ] **Step 5: Instalar a partir da release**

Baixar `TaskbarMonitor-Setup-1.0.0.exe` da página da release (logado no GitHub) e executar.
Expected: instalação concluída em `C:\Program Files\TaskbarMonitor` e o app abre mostrando CPU, GPU e RAM. Se aparecer o aviso do SmartScreen, é esperado: o instalador não tem assinatura de código; usar "Mais informações" e "Executar assim mesmo".

- [ ] **Step 6: Confirmar a inicialização automática**

Run: `schtasks /Query /TN TaskbarMonitor /XML | findstr /i "Command"`
Expected: `<Command>C:\Program Files\TaskbarMonitor\TaskbarMonitor.exe</Command>`, ou seja, o caminho antigo de `Downloads` foi corrigido.

Opcional: fazer logoff e logon e confirmar que o monitor abre sozinho em até ~10 segundos (a tarefa tem atraso de 10 s).

- [ ] **Step 7: Limpar versões antigas**

Se existir a pasta `publish\` dentro do projeto e ela não for mais usada, ela já está no `.gitignore`; pode ser apagada sem afetar o Git.

---

## Self-Review

- **Cobertura do pedido:** rodar fácil (Task 1), publicar versão com um comando (Task 2), release no GitHub (Task 3), histórico de mudanças e instruções (Task 4), instalador que atualiza por cima (Task 5), instalador na release e atualização local por duplo clique (Task 6), verificação ponta a ponta (Task 7). Repositório privado mantido (Global Constraints). Auto-update dentro do app ficou de fora de propósito: com o repositório privado, exigiria embutir um token ou hospedar os arquivos em outro lugar.
- **Dependência entre tasks:** o `release.bat` (Task 2) gera só o zip local; o instalador é gerado pela CI (Task 6), porque o Inno Setup não está instalado nesta máquina. O `AppId` do `.iss` é fixo (Global Constraints).
- **Review Focus:** cada item aponta a task e o step que o testa.
- **Consistência de nomes:** `release.bat`, `run.bat`, tag `vX.Y.Z`, zip `TaskbarMonitor-X.Y.Z-win-x64.zip` igual no script e no workflow, seção `## [X.Y.Z]` no changelog.

### Correções da revisão (2026-10-11)

1. **`findstr` com colchetes:** suspeita de que `## [1.0.0]` virasse regex. Testado nesta máquina: `/c:` já busca literal, então não era bug. Mantido `/l` só para deixar explícito; o teste da Task 2, Step 4 cobre o caso positivo e o negativo.
2. **Plano não rastreado:** `docs/` estava fora do Git, e o `release.bat` recusaria a árvore suja na Task 5. Agora o plano entra no commit da Task 1.
3. **BOM no csproj:** `Set-Content -Encoding utf8` no PowerShell 5.1 grava BOM. Isso sujaria o csproj até sem troca de versão e geraria um commit "release" vazio de sentido. Trocado por `[IO.File]::WriteAllText`, que preserva UTF-8 sem BOM e o CRLF.
4. **Quebras de linha dos `.bat`:** batch com LF quebra `goto :fail`. Adicionado passo de normalização para CRLF.
5. **Branch errada:** a tag podia sair de outra branch enquanto o script enviava `main`. Adicionada checagem de branch.
6. **Instância única:** com o app instalado aberto, o exe de Debug fechava em silêncio pelo mutex. O `run.bat` agora avisa e para.
7. **Nome do zip:** o local saía sem `v` e o da release com `v`. Unificado como `TaskbarMonitor-X.Y.Z-win-x64.zip`.
8. **Pasta de publish antiga:** uma execução anterior podia deixar arquivos velhos no zip. A pasta é apagada antes do publish.
9. **Parênteses em `echo`:** a mensagem de branch tinha `)` dentro de um bloco, o que fecharia o bloco. Texto reescrito sem parênteses.
