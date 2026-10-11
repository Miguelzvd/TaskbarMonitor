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
build.bat
```

Saída: `publish\TaskbarMonitor.exe`. Mova a pasta `publish` para um lugar fixo (ex.: `C:\Tools\TaskbarMonitor`) **antes** de ativar a inicialização automática, porque a tarefa agendada guarda o caminho do exe.

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

## Limitações conhecidas

- Foi pensado para a taskbar **horizontal na parte de baixo**.
- No Windows 11 a âncora é a janela `TrayNotifyWnd`. Se a posição padrão ficar sobreposta a algum ícone, basta arrastar.
- A taskbar também é "topmost". Ao clicar nela, o monitor pode sumir por até ~300 ms até ser trazido de volta.
- GPUs híbridas: a dedicada tem prioridade (NVIDIA > AMD > Intel).

## Estrutura

| Arquivo | Papel |
|---|---|
| `SensorReader.cs` | Leitura dos sensores via `LibreHardwareMonitorLib` |
| `MonitorForm.cs` | Janela, posicionamento sobre a taskbar, renderização, menu |
| `Autostart.cs` | Tarefa agendada (logon, elevada) |
| `AppSettings.cs` | Persistência de offset/intervalo |
| `NativeMethods.cs` | P/Invoke Win32 |
