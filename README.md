# Taskbar Monitor

Monitor de hardware em tempo real ancorado na barra de tarefas do Windows, ao lado da bandeja/relógio.

```
CPU  23%   GPU  41%  VRAM 68%   RAM  62%
     54°C       63°C  8.2/12G         19.8/32G
```

- **CPU**: uso total e temperatura (Tctl/Tdie na AMD, Package na Intel)
- **GPU**: uso e temperatura do core, com **VRAM** logo ao lado (% e usado/total)
- **RAM**: % e usado/total
- Os valores mudam de cor quando passam dos limites: uso ≥70% fica amarelo e ≥90% vermelho; temperatura ≥72°C fica amarela e ≥85°C vermelha.
- Segue o tema claro/escuro do Windows, some quando um app entra em tela cheia e não aparece no Alt+Tab.

## Build

Requer o **.NET 8 SDK** (`winget install Microsoft.DotNet.SDK.8`).

```bat
scripts\build.bat
```

Saída: `publish\TaskbarMonitor.exe`, um arquivo único (~5 MB) que roda direto com dois cliques. Ele usa o .NET 8 Desktop Runtime instalado na máquina. O `build.bat` pede para fechar o app antes, porque o exe em execução fica travado, e limpa a pasta `publish` a cada build.

Para instalar de forma fixa (necessário para a inicialização automática, porque a tarefa agendada guarda o caminho do exe), use o instalador da release ou rode `scripts\deploy-local.bat` como administrador. Ambos instalam em `C:\Program Files\TaskbarMonitor`. Para gerar o instalador localmente (precisa do Inno Setup: `winget install JRSoftware.InnoSetup`), rode `scripts\build-installer.bat`: ele cria `release\TaskbarMonitor-Setup-X.Y.Z.exe` e apaga os setups de versões antigas.

## Uso

Tudo é configurado pelo **menu do botão direito**:

| Item | O que faz |
|---|---|
| **Modo → Fixo na barra de tarefas** | Fica preso à taskbar e não arrasta |
| **Posição na barra** *(modo fixo)* | Direita (junto à bandeja), Centro ou Esquerda |
| **Modo → Livre (arrastar)** | Arraste com o botão esquerdo para qualquer lugar da tela (cursor ✥) |
| **Travar posição** *(modo livre)* | Fixa onde você soltou. Um pontinho no canto indica que está travado |
| Intervalo de atualização | 0,5 / 1 / 2 / 5 s |
| Iniciar com o Windows | Tarefa agendada elevada, sem UAC no logon |

Passe o mouse sobre o monitor para ver o nome da CPU/GPU e eventuais erros.

O app roda **como administrador**, pois os sensores de temperatura da CPU exigem isso. "Iniciar com o Windows" cria uma tarefa no Agendador com privilégio elevado, então não aparece prompt do UAC no logon. A tarefa também não para depois de 3 dias e roda na bateria.

Configurações ficam em `%AppData%\TaskbarMonitor\settings.json`.

## Temperatura da CPU aparecendo `--°C`

As versões recentes do LibreHardwareMonitor leem MSRs da CPU pelo driver **PawnIO**, que substituiu o antigo WinRing0, hoje bloqueado pelo Defender. Se a temperatura da CPU não aparecer, instale o PawnIO (https://pawnio.eu) e reinicie o app. Uso, GPU, VRAM e RAM funcionam sem ele.

## Rodar em desenvolvimento

```bat
scripts\run.bat
```

Compila em Debug e abre o app como administrador. Se a versão instalada estiver aberta, feche-a antes (botão direito → Sair), porque o app só permite uma instância.

## Publicar uma versão

1. Escreva a nova seção no `CHANGELOG.md` (ex.: `## [1.1.0] - AAAA-MM-DD`) e faça commit.
2. Na branch `main`, rode a partir da pasta do projeto:

   ```bat
   scripts\release.bat 1.1.0
   ```

O script valida a versão, a branch e o changelog, atualiza o `TaskbarMonitor.csproj`, gera `release\TaskbarMonitor-1.1.0-win-x64.zip`, cria o commit e a tag `v1.1.0` e envia ao GitHub. O GitHub Actions então cria a release com o zip e o instalador `TaskbarMonitor-Setup-1.1.0.exe` anexados. Como o repositório é privado, a release fica visível só para quem tem acesso a ele.

## Atualizar uma instalação

1. Baixe `TaskbarMonitor-Setup-X.Y.Z.exe` da release (repositório privado: é preciso estar logado no GitHub).
2. Execute o instalador. Ele fecha o app, atualiza os arquivos e mantém a inicialização automática funcionando.

No PC de desenvolvimento, `scripts\deploy-local.bat` (como administrador) faz o mesmo a partir do código-fonte.

O zip da release continua disponível para uso portátil, sem instalação, mas a inicialização automática guarda o caminho do exe, então extraia em uma pasta fixa.

As configurações ficam em `%AppData%\TaskbarMonitor\settings.json` e não são afetadas. O instalador exige o .NET 8 Desktop Runtime.

## Limitações conhecidas

- Foi pensado para a taskbar **horizontal na parte de baixo**.
- No Windows 11 a âncora é a janela `TrayNotifyWnd`. Se a posição padrão ficar sobreposta a algum ícone, basta arrastar.
- A taskbar também é "topmost". Ao clicar nela, o monitor pode sumir por até ~300 ms até ser trazido de volta.
- GPUs híbridas: a dedicada tem prioridade (NVIDIA > AMD > Intel).

## Estrutura

```
TaskbarMonitor/
├─ TaskbarMonitor.csproj
├─ src/                       todo o código-fonte
│  ├─ Program.cs              ponto de entrada (instância única)
│  ├─ Sensors/
│  │  └─ SensorReader.cs      leitura dos sensores via LibreHardwareMonitorLib
│  ├─ UI/
│  │  ├─ MonitorForm.cs       janela, posicionamento sobre a taskbar, renderização, montagem do menu
│  │  ├─ Theme.cs             cores claras e escuras, seguindo o tema do Windows
│  │  ├─ MenuStyle.cs         valores visuais do menu: cores, medidas, fonte e ícones
│  │  ├─ MenuRenderer.cs      desenho do menu: realce, ícones, interruptores, setas, cantos
│  │  └─ Shapes.cs            formas de desenho compartilhadas (retângulo arredondado)
│  ├─ Settings/
│  │  └─ AppSettings.cs       persistência das configurações (settings.json)
│  └─ Platform/
│     ├─ NativeMethods.cs     P/Invoke Win32
│     └─ Autostart.cs         tarefa agendada de inicialização (logon, elevada)
├─ assets/                    app.ico e app.manifest
├─ installer/                 script do Inno Setup (TaskbarMonitor.iss)
├─ scripts/                   .bat de build, instalação e release, e o gerador do ícone
├─ .github/workflows/         release automática no GitHub
├─ README.md
└─ CHANGELOG.md
```

As pastas só organizam os arquivos: o namespace continua sendo `TaskbarMonitor` para todos.

## Scripts

Ficam em `scripts\` e podem ser executados de qualquer pasta (cada um entra sozinho na raiz do projeto).

| Script | O que faz |
|---|---|
| `run.bat` | Compila em Debug e abre o app como administrador |
| `build.bat` | Gera `publish\TaskbarMonitor.exe` (arquivo único) |
| `build-installer.bat` | Gera `release\TaskbarMonitor-Setup-X.Y.Z.exe` (Inno Setup) e apaga setups antigos |
| `deploy-local.bat` | Compila e instala em `C:\Program Files\TaskbarMonitor` (como administrador) |
| `release.bat X.Y.Z` | Publica uma versão: zip local, commit, tag e envio ao GitHub |
| `make-icon.ps1` | Regenera `assets\app.ico` |

O alvo (`win-x64`, dependente do .NET 8) e o arquivo único ficam no `TaskbarMonitor.csproj`, então todos usam apenas `dotnet publish -c Release`.
