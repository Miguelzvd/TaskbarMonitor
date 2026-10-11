# Changelog

Cada versão tem uma seção `## [X.Y.Z] - AAAA-MM-DD`. O `release.bat` exige essa seção antes de publicar, e o texto dela vira as notas da release no GitHub.

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
