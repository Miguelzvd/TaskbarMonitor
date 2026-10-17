# AGENTS.md — Taskbar Monitor

Guia para qualquer IA (ou pessoa) que for dar manutenção neste projeto. Leia antes de mexer.

## O que é

Monitor de hardware em tempo real ancorado na barra de tarefas do Windows 11: uso e temperatura da **CPU**, uso e temperatura da **GPU** com **VRAM** ao lado, e **RAM** (% e usado/total). Janela sem borda, sempre no topo, sobre a taskbar.

- Dono: **Miguel** (GitHub `Miguelzvd`), engenheiro de software. Prefere comunicação **em português**, técnica e direta.
- Repositório: `https://github.com/Miguelzvd/TaskbarMonitor` (privado). Pasta local: `C:\Users\dilso\Documents\workspace\Projects\TaskbarMonitor`.
- Cuidado: pode existir um zip antigo em `Downloads\TaskbarMonitor.zip`. É um snapshot obsoleto. **Nunca trabalhe nele.**

## Stack

C# / .NET 8 / WinForms (`net8.0-windows`), `LibreHardwareMonitorLib` 0.9.6 (NuGet) para os sensores, Inno Setup 6 para o instalador. Publica um único `TaskbarMonitor.exe` dependente do **.NET 8 Desktop Runtime** (`SelfContained=false`).

## Estrutura

```
TaskbarMonitor.csproj          raiz: alvo, versão, ícone, manifest, publish single-file
src/                           TODO o código-fonte
  Program.cs                   entrada; mutex global de instância única
  Sensors/SensorReader.cs      LibreHardwareMonitor -> record Snapshot
  UI/MonitorForm.cs            janela, posição, z-order, desenho, menu, animação de carregamento
  UI/Theme.cs                  cores claro/escuro (registro SystemUsesLightTheme)
  UI/MenuStyle.cs, MenuRenderer.cs   menu de contexto estilizado
  UI/Shapes.cs                 retângulo arredondado
  Settings/AppSettings.cs      settings.json em %AppData%\TaskbarMonitor
  Platform/NativeMethods.cs    P/Invoke (user32, dwmapi, winmm, kernel)
  Platform/Autostart.cs        tarefa agendada "TaskbarMonitor" (schtasks /XML)
assets/                        app.ico, app.manifest (requireAdministrator)
installer/TaskbarMonitor.iss   instalador Inno Setup
scripts/                       .bat de build/instalação/release e make-icon.ps1
.github/workflows/release.yml  CI: ao enviar tag vX.Y.Z gera zip + instalador + release
CHANGELOG.md, README.md
```

As pastas só organizam: o namespace é `TaskbarMonitor` para todos os arquivos. Código novo vai em `src/`, na pasta da sua "família". Não crie arquivos de código na raiz.

## Comandos

Rode a partir de qualquer pasta (cada `.bat` entra sozinho na raiz do projeto).

| Comando | O que faz |
|---|---|
| `dotnet build -c Release` | Compila (verificação rápida) |
| `scripts\run.bat` | Compila em Debug e abre o app elevado |
| `scripts\build.bat` | Gera `publish\TaskbarMonitor.exe` |
| `scripts\build-installer.bat` | Gera `release\TaskbarMonitor-Setup-X.Y.Z.exe` e **apaga setups antigos** |
| `scripts\deploy-local.bat` | Instala em `C:\Program Files\TaskbarMonitor` (admin). O Miguel não quer depender dele |
| `scripts\release.bat X.Y.Z` | Valida, atualiza versão, zip, commit, tag e **push ao GitHub** |
| `powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1` | Regenera `assets\app.ico` |

## Fluxo para cada mudança (regra do Miguel)

Toda mudança que altera comportamento vira **versão nova com setup novo**:

1. Edite o código em `src/`.
2. Suba `<Version>` no `TaskbarMonitor.csproj` (patch para correção, minor para funcionalidade).
3. Adicione a seção `## [X.Y.Z] - AAAA-MM-DD` no topo do `CHANGELOG.md` (em português).
4. Rode `scripts\build-installer.bat`. Só deve sobrar **um** `TaskbarMonitor-Setup-*.exe` em `release\`. Se o script avisar que não conseguiu apagar um antigo, é porque ele está em uso: feche-o e apague à mão.
5. Verifique que compila sem erros nem avisos antes de entregar.

Refatorações sem mudança de comportamento não precisam subir a versão.

## Git

- **Não execute `git commit`.** O Miguel é quem commita. Prepare `git add` e uma mensagem pronta para colar (skill `commit`, se existir). Nunca faça push nem force push sem pedido explícito.
- Commits no estilo Conventional Commits (`feat:`, `fix:`, `docs:`, `refactor:`, `build:`, `chore:`), em português.
- `release.bat` exige árvore limpa, branch `main` e seção no CHANGELOG, e **envia ao GitHub**. Só rode se o Miguel pedir.
- `.gitattributes`: `*.cs` e `*.bat` usam CRLF no repositório. A cópia de trabalho pode estar com LF e o git avisa; é normal. Não reescreva arquivos inteiros só para trocar fim de linha.

## Convenções

- Código, comentários, textos da UI e commits em **português**. Nomes de identificadores em inglês, como já está.
- Comente o **porquê**, não o quê. Mantenha o estilo e a densidade de comentários dos arquivos vizinhos.
- Não adicione dependências sem necessidade. Sem frameworks de UI além do WinForms.
- Arquivos `.bat` e `.ps1`: só ASCII (evita problema de codepage no cmd).

## Comportamento importante e armadilhas (já resolvidas, não reintroduza)

**Permissões e processo**
- O app exige **administrador** (`requireAdministrator`): a temperatura da CPU precisa de driver. Um shell **sem elevação não consegue encerrar** o `TaskbarMonitor.exe` em execução. O `build.bat` e o `run.bat` avisam; o `build-installer.bat` compila em `publish\`, que não fica travado pelo app instalado.
- Se `dotnet build` ou `publish` falhar com "arquivo em uso", há uma instância rodando a partir de `bin\` ou `publish\`. Peça para o Miguel fechá-la (botão direito → Sair).
- Para depurar na IDE, abra a IDE como administrador.

**Sensores (`SensorReader.cs`)**
- RAM: o LibreHardwareMonitor expõe dois nós de memória, "Total Memory" (física) e "Virtual Memory" (confirmada). Escolhe-se o que **não** contém "Virtual". Sem isso a RAM aparece com ~92%.
- Carga da GPU: ler `D3D 3D` **antes** de `GPU Core`. `GPU Core` marcava 98% quando o Gerenciador de Tarefas mostrava 31%.
- GPU híbrida: prioridade NVIDIA > AMD > Intel, desempate por VRAM total.
- Leitura de sensores roda em `System.Threading.Timer`, fora da UI thread (`Computer.Open()` é lento).
- Se a temperatura da CPU aparecer `--°C`, falta o driver PawnIO (https://pawnio.eu).

**Janela e z-order (`MonitorForm.cs`)**
- **Modo fixo: a taskbar é a DONA (owner) da janela** (`SyncOwner`, `SetWindowLongPtr(GWLP_HWNDPARENT)`). O Windows mantém janelas "owned" acima da dona no mesmo instante em que ela sobe, o que elimina o pisca ao clicar na taskbar (medido: 0 de 193 leituras abaixo, contra 143 sem dono). Mudar o dono não reordena sozinho, por isso `SyncOwner` chama `SetWindowPos` em seguida. No modo livre e no monitor de fuga o dono é removido. Se o Windows recusar o dono (`_ownerFailed`), cai na thread abaixo. **Janelas "owned" são destruídas com a dona:** se o Explorer reiniciar, `OnHandleDestroyed` relança o app e sai.
- Reserva (modo livre, monitor de fuga ou dono recusado): a taskbar também é topmost e sobe por cima do monitor quando clicada. Mitigação: hook `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` + uma thread (`TopLoop`, ~2 ms, com `timeBeginPeriod(1)`) que só chama `SetWindowPos` quando a taskbar passou por cima, inserindo o monitor logo acima dela e **atrás** de quem estiver acima (painel da setinha da bandeja). **Não volte a forçar `HWND_TOPMOST` em loop**: brigava com o painel da bandeja e causava pisca.
- A thread só usa o handle em cache (`_hwnd`) e flags `volatile`; não acesse propriedades de controle nela.
- Tela cheia: só conta se o app em tela cheia estiver no monitor "de casa" do widget (taskbar no modo fixo, posição salva no modo livre). Com outro monitor, o widget vai para ele (`AwayLocation`) e volta depois. Com um monitor só, esconde. Janelas do shell (`explorer`, `ShellExperienceHost`, `StartMenuExperienceHost`, `SearchHost`, `SearchApp`, `TextInputHost`) **não** contam como tela cheia, e a tela cheia só vale após 2 leituras seguidas (~0,6 s).
- A janela é `WS_EX_NOACTIVATE`, então o menu de contexto não recebe cliques fora. `OnMenuWatchTick` vigia o mouse e fecha o menu ao clicar fora (cliques na própria janela o Windows já trata).
- Enquanto não há primeira leitura, desenha-se um esqueleto com brilho (`DrawSkeleton`), animado por `_loadingTimer` (16 ms). Para quando chega a leitura ou dá erro.
- Larguras de coluna fixas (pior caso de texto) para os números não "pularem".

**Instalador (`installer/TaskbarMonitor.iss`)**
- **Nunca** use `schtasks /Change /TR` no instalador: pede a senha do usuário e, sem console, trava em "Instalando". Use `Set-ScheduledTask` via PowerShell (já feito).
- O `[Run]` que abre o app precisa de `shellexec`, senão dá erro 740 (o exe exige elevação).
- Mesmo `AppId` sempre: reinstalar por cima atualiza. Instala em `C:\Program Files\TaskbarMonitor`. O caminho não pode mudar, pois a tarefa de autostart guarda o caminho do exe.
- Exige o .NET 8 Desktop Runtime; o instalador avisa se faltar.

**Autostart (`Autostart.cs`)**: tarefa agendada via XML (`HighestAvailable`, logon, atraso de 10 s, sem limite de tempo, roda na bateria). O Run do registro não eleva, por isso não é usado.

## Limitações conhecidas

- Pensado para taskbar **horizontal embaixo**. Taskbar vertical ou no topo não é tratada.
- No modo fixo, usa só a taskbar principal (`Shell_TrayWnd`). A taskbar secundária só é usada como destino quando o widget foge de uma tela cheia.
- No modo fixo **Centro** no Win11 com ícones centralizados, o monitor cobre ícones fixados.
- No modo livre, se o widget estiver sobre a taskbar, ele não é "owned": pode restar um pisca de poucos milissegundos ao clicar nela (a thread de 2 ms atenua).
- O dono (owner) foi medido de fora com um processo sem elevação e **confirmado pelo Miguel no app real** (administrador) na 1.4.5: o pisca acabou. Se um dia o Windows recusar o dono, o código cai sozinho na thread (`_ownerFailed`).
- Em modo livre, arrastar fica desativado enquanto o widget está no monitor de fuga.

## Como verificar uma mudança

Não há testes automatizados. Verifique assim:
1. `dotnet build -c Release` sem erros nem avisos.
2. `scripts\build-installer.bat` gera o setup novo.
3. Peça ao Miguel para instalar e testar o que mudou. O app é visual e depende do hardware dele, então **não declare uma correção visual como confirmada** sem o retorno dele; diga o que foi medido e o que é hipótese.
4. Para investigar problemas de janela, dá para medir de fora com um script que observa visibilidade, z-order e foreground a cada 1 ms enquanto simula cliques (foi assim que se achou o pisca de ~15 ms).

## Ideias futuras

Escolher quais métricas exibir, mini-gráficos (sparklines), clocks e potência da GPU, rede e disco, ícone de bandeja, ajuste de cores e limiares pelo menu, suporte a taskbar vertical e a múltiplas taskbars.
