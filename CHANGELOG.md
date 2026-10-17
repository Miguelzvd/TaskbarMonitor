# Changelog

Cada versão tem uma seção `## [X.Y.Z] - AAAA-MM-DD`. O `release.bat` exige essa seção antes de publicar, e o texto dela vira as notas da release no GitHub.

## [1.4.4] - 2026-10-17

- Pisca ao clicar na setinha da bandeja: medido, o monitor ficava ~15 ms atrás da taskbar, que sobe por cima dele ao ser clicada. Agora uma thread leve confere a ordem das janelas a cada ~2 ms e o traz de volta quase na hora.

## [1.4.3] - 2026-10-17

- Correção do pisca ao recolher o painel da setinha da bandeja: o monitor deixou de forçar `HWND_TOPMOST` o tempo todo. Agora ele só se reposiciona quando a taskbar passa por cima dele, e se coloca logo acima da taskbar (atrás do painel da bandeja), sem brigar com ele.

## [1.4.2] - 2026-10-17

- Correção do pisca ao clicar na taskbar (ex.: setinha da bandeja): o monitor reafirma o topo da ordem Z na hora em que o foco muda (`SetWinEventHook`) e a cada 60 ms, em vez de a cada 300 ms.

## [1.4.1] - 2026-10-17

- Correção: o monitor piscava (sumia e aparecia) ao abrir a setinha de ícones ocultos da bandeja. Janelas do próprio Windows (bandeja, Iniciar, Pesquisa…) deixam de ser tratadas como tela cheia, e a tela cheia só vale depois de ~0,6 s estável.

## [1.4.0] - 2026-10-17

- Tela cheia com vários monitores: o monitor só se afasta se o app em tela cheia estiver no mesmo monitor em que ele está. Nesse caso ele vai para outro monitor (junto à taskbar dele, ou no canto inferior direito) e volta sozinho quando a tela cheia termina. Antes ele sumia mesmo com a tela cheia em outro monitor.
- Com um monitor só, continua se escondendo durante a tela cheia.

## [1.3.0] - 2026-10-17

- Ícone novo: três barras nas cores de CPU, GPU e RAM. Usado no `TaskbarMonitor.exe`, no instalador, no atalho do Menu Iniciar e em "Aplicativos instalados".
- `scripts\make-icon.ps1` regenera o `assets\app.ico` (vários tamanhos, de 16 a 256 px).

## [1.2.2] - 2026-10-17

- Correção do instalador: "Abrir o Taskbar Monitor" no final falhava com o erro 740 (o app exige administrador). Agora abre com elevação via `shellexec`.

## [1.2.1] - 2026-10-17

- Correção do instalador: ele travava em "Instalando" ao atualizar o caminho da inicialização automática (o `schtasks /Change` ficava esperando a senha). Agora usa `Set-ScheduledTask`.

## [1.2.0] - 2026-10-17

- Carregamento animado: enquanto a primeira leitura não chega, barras arredondadas com um brilho em onda no lugar do `--`.
- O menu de contexto agora fecha ao clicar fora dele.
- Correção: a carga da GPU passa a vir do engine 3D (`D3D 3D`), igual ao Gerenciador de Tarefas, em vez de `GPU Core`.
- `build-installer.bat` gera o instalador local da versão atual e apaga os setups antigos.

## [1.1.0] - 2026-10-11

- Novo visual do menu de contexto: painel arredondado, ícones, interruptores para "Travar posição" e "Iniciar com o Windows", e tema claro/escuro acompanhando o Windows.
- Executável único: `TaskbarMonitor.exe` roda sozinho, sem DLLs ao lado (usa o .NET 8 Desktop Runtime).
- Correção: a RAM agora mostra a memória física em uso (igual ao Gerenciador de Tarefas), e não a memória virtual confirmada.
- Instalação e build: `deploy-local.bat`, `run.bat` e publicação com apenas `dotnet publish -c Release`.

## [1.0.0] - 2026-10-11

- Monitor de CPU, GPU, VRAM e RAM ancorado na barra de tarefas.
- Modos fixo (direita, centro ou esquerda) e livre (arrastar e travar).
- Alerta visual de cor para uso e temperatura altos.
- Inicialização automática com o Windows via tarefa agendada elevada.
- Instalador (`TaskbarMonitor-Setup-X.Y.Z.exe`) que atualiza por cima e corrige o caminho da inicialização automática.
