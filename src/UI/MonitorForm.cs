using System.Drawing.Drawing2D;
using Microsoft.Win32;
using static TaskbarMonitor.NativeMethods;

namespace TaskbarMonitor;

/// <summary>
/// Janela sem borda, sempre no topo. Dois modos:
///  • Fixo  — preso à barra de tarefas (direita/centro/esquerda), não arrasta.
///  • Livre — arrasta para qualquer lugar; pode ser travado na posição.
/// </summary>
internal sealed class MonitorForm : Form
{
    // ---- layout ----
    private const int Pad = 8;
    private const int LabelGap = 6;
    private const int ColGap = 10;
    private const int GroupGap = 16;
    private const int EdgeMargin = 6;

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 300 };
    private readonly ToolTip _toolTip = new() { InitialDelay = 400, ShowAlways = true };

    // A taskbar também é topmost e sobe por cima de nós quando clicada (ex.: setinha da bandeja).
    // Reafirmamos o topo na hora em que o foco muda (hook) e numa thread que confere a cada ~2 ms,
    // porque o sumiço dura só o tempo de a taskbar subir e nós descermos de volta.
    private Thread? _topThread;
    private volatile bool _topStop;
    private volatile bool _topSuppressed; // menu aberto ou arrastando: não mexer na ordem Z
    private IntPtr _hwnd;
    private IntPtr _foregroundHook;
    private WinEventProc? _foregroundProc; // referência mantida para o GC não coletar o delegate

    // Animação de carregamento (esqueleto com brilho) enquanto não chega a primeira leitura.
    private const int ShimmerPeriodMs = 1500;
    private readonly System.Windows.Forms.Timer _loadingTimer = new() { Interval = 16 };
    private readonly System.Diagnostics.Stopwatch _loadingClock = System.Diagnostics.Stopwatch.StartNew();
    private float _shimmerX; // posição do brilho em px (coordenadas do cliente)

    // A janela não é ativada (WS_EX_NOACTIVATE), então o menu não recebe o clique fora sozinho.
    // Enquanto o menu está aberto, vigiamos o mouse e fechamos ao clicar fora dele.
    private readonly System.Windows.Forms.Timer _menuWatch = new() { Interval = 30 };
    private bool _menuPrevDown;
    private System.Threading.Timer? _sensorTimer;
    private SensorReader? _reader;
    private int _reading;

    private Snapshot? _snap;
    private string _lastTooltip = "";
    private string? _sensorError;

    private Rectangle _taskbar;
    private Rectangle _anchor; // taskbar até a borda esquerda da bandeja
    private int _layoutHeight = -1;

    private bool _dragging;
    private Point _dragStartCursor, _dragStartLocation;

    private Font _labelFont = new("Segoe UI Semibold", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
    private Font _valueFont = new("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
    private int _labelW, _colLoadW, _colMemW;
    private Theme _theme = Theme.Detect();

    // itens de menu que precisam refletir o estado
    private ToolStripMenuItem _miDocked = null!, _miFree = null!, _miDockPos = null!, _miLock = null!, _miAutostart = null!;
    private readonly Dictionary<DockPosition, ToolStripMenuItem> _miDockPositions = new();
    private ToolStripMenuItem _miInterval = null!;

    // Monitor para onde fugimos quando um app em tela cheia ocupa o monitor "de casa"; null = em casa.
    private Screen? _awayScreen;
    private int _fullscreenTicks;

    private bool CanDrag => _settings.Mode == PlacementMode.Free && !_settings.FreeLocked && _awayScreen is null;

    public MonitorForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Text = "Taskbar Monitor";

        ContextMenuStrip = BuildMenu();
        _uiTimer.Tick += OnUiTick;
        _loadingTimer.Tick += OnLoadingTick;
        _menuWatch.Tick += OnMenuWatchTick;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST; // fora do Alt+Tab, não rouba foco
            return cp;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Reposition(force: true);
        UpdateCursor();
        _uiTimer.Start();
        _hwnd = Handle;
        timeBeginPeriod(1);
        _topThread = new Thread(TopLoop) { IsBackground = true, Name = "KeepOnTop" };
        _topThread.Start();
        _foregroundProc = (_, _, _, _, _, _, _) => KeepOnTop();
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        _loadingTimer.Start();
        _sensorTimer = new System.Threading.Timer(_ => ReadSensors(), null, 0, _settings.IntervalMs);
    }

    // ===================== sensores =====================

    private void ReadSensors()
    {
        if (Interlocked.Exchange(ref _reading, 1) == 1) return; // evita leituras sobrepostas
        try
        {
            _reader ??= new SensorReader(); // Open() é lento; roda fora da UI thread
            var snap = _reader.Read();
            PostToUi(() =>
            {
                _snap = snap;
                _sensorError = null;
                _loadingTimer.Stop();
                UpdateTooltip();
                Invalidate();
            });
        }
        catch (Exception ex)
        {
            PostToUi(() => { _sensorError = ex.Message; _loadingTimer.Stop(); UpdateTooltip(); Invalidate(); });
        }
        finally
        {
            Interlocked.Exchange(ref _reading, 0);
        }
    }

    private void PostToUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { /* fechando */ }
    }

    // ===================== posicionamento =====================

    private void OnUiTick(object? sender, EventArgs e)
    {
        // Tela cheia só nos incomoda se estiver no monitor onde o widget mora. Com outro monitor
        // disponível, o widget vai para ele; com um monitor só, não há para onde ir e ele se esconde.
        // Exige 2 leituras seguidas (~0,6 s) para ignorar telas cheias passageiras e evitar o "pisca".
        var rawFullscreen = ForegroundFullscreenScreen();
        _fullscreenTicks = rawFullscreen is null ? 0 : _fullscreenTicks + 1;
        var fullscreenScreen = _fullscreenTicks >= 2 ? rawFullscreen : null;
        Screen? away = null;
        bool hide = false;
        if (fullscreenScreen is not null && SameScreen(fullscreenScreen, HomeScreen()))
        {
            away = Screen.AllScreens.FirstOrDefault(s => !SameScreen(s, fullscreenScreen));
            hide = away is null;
        }

        if (hide) { if (Visible) Hide(); return; }
        if (!Visible) Show();

        if (!SameScreen(away, _awayScreen))
        {
            _awayScreen = away;
            UpdateCursor();
            ApplyPosition();
        }

        if (!_dragging) Reposition();
        KeepOnTop();
    }

    /// <summary>
    /// Garante que ficamos logo acima da taskbar na ordem Z (ela também é topmost e sobe quando clicada).
    /// Só mexemos quando a taskbar passou por cima de nós, e nos colocamos atrás de quem estiver acima dela
    /// (ex.: o painel da setinha da bandeja). Forçar HWND_TOPMOST o tempo todo brigava com esse painel
    /// e fazia o monitor piscar ao fechá-lo.
    /// </summary>
    private void KeepOnTop()
    {
        _topSuppressed = _dragging || (ContextMenuStrip?.Visible ?? false);
        RaiseAboveTaskbarIfNeeded();
    }

    /// <summary>Roda numa thread própria: confere a ordem Z a cada ~2 ms (barato: só lê a ordem das janelas).</summary>
    private void TopLoop()
    {
        while (!_topStop)
        {
            try { RaiseAboveTaskbarIfNeeded(); } catch { /* janela sendo fechada */ }
            Thread.Sleep(2);
        }
    }

    /// <summary>Seguro para chamar de qualquer thread: usa só o handle em cache e flags voláteis.</summary>
    private void RaiseAboveTaskbarIfNeeded()
    {
        IntPtr me = _hwnd;
        if (me == IntPtr.Zero || _topSuppressed || !IsWindowVisible(me)) return;

        IntPtr tray = FindWindow("Shell_TrayWnd", null);
        IntPtr insertAfter = HWND_TOPMOST;
        if (tray != IntPtr.Zero)
        {
            if (IsAbove(me, tray)) return; // já estamos acima da taskbar: nada a fazer
            IntPtr above = GetWindow(tray, GW_HWNDPREV);
            if (above != IntPtr.Zero) insertAfter = above;
        }
        SetWindowPos(me, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>true se <paramref name="me"/> está acima de <paramref name="other"/> na ordem Z.</summary>
    private static bool IsAbove(IntPtr me, IntPtr other)
    {
        for (IntPtr w = GetWindow(other, GW_HWNDPREV); w != IntPtr.Zero; w = GetWindow(w, GW_HWNDPREV))
            if (w == me) return true;
        return false;
    }

    /// <summary>Lê a geometria da taskbar e reaplica a posição se algo mudou.</summary>
    private void Reposition(bool force = false)
    {
        IntPtr tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero || !GetWindowRect(tray, out var tb)) return;

        var taskbar = Rectangle.FromLTRB(tb.Left, tb.Top, tb.Right, tb.Bottom);
        int anchorRight = tb.Right - 320; // fallback

        IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify != IntPtr.Zero && GetWindowRect(notify, out var nr) && nr.Width > 0 && nr.Left > tb.Left)
            anchorRight = nr.Left;

        var anchor = Rectangle.FromLTRB(tb.Left, tb.Top, anchorRight, tb.Bottom);
        if (!force && anchor == _anchor && taskbar == _taskbar) return;

        _taskbar = taskbar;
        _anchor = anchor;
        ApplyPosition();
    }

    private void ApplyPosition()
    {
        if (_anchor.IsEmpty) return;

        int h = Math.Clamp(_anchor.Height - 8, 24, 56);
        if (h != _layoutHeight) RecalcLayout(h);

        Point p = _awayScreen is { } away ? AwayLocation(away, h)
                : _settings.Mode == PlacementMode.Docked ? DockedLocation(h) : FreeLocation();
        if (p != Location || Height != h) SetBounds(p.X, p.Y, Width, h);
    }

    private Point DockedLocation(int h)
    {
        int y = _taskbar.Top + (_taskbar.Height - h) / 2;
        int x = _settings.DockPosition switch
        {
            DockPosition.Left => _taskbar.Left + EdgeMargin,
            DockPosition.Center => _taskbar.Left + (_taskbar.Width - Width) / 2,
            _ => _anchor.Right - Width - EdgeMargin,
        };
        x = Math.Clamp(x, _taskbar.Left, Math.Max(_taskbar.Left, _taskbar.Right - Width));
        return new Point(x, y);
    }

    private static bool SameScreen(Screen? a, Screen? b) => a?.DeviceName == b?.DeviceName;

    /// <summary>Monitor onde o widget mora: o da taskbar (modo fixo) ou o da posição salva (modo livre).</summary>
    private Screen HomeScreen()
    {
        var primary = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        if (_settings.Mode == PlacementMode.Docked)
            return _taskbar.IsEmpty ? primary : Screen.FromRectangle(_taskbar);

        var center = new Point((_settings.FreeX ?? Left) + Width / 2, (_settings.FreeY ?? Top) + Height / 2);
        return Screen.FromPoint(center);
    }

    /// <summary>Posição no monitor de fuga: junto à bandeja da taskbar dele, ou no canto inferior direito.</summary>
    private Point AwayLocation(Screen screen, int h)
    {
        var bounds = screen.Bounds;

        // Monitor da taskbar principal: mesma âncora do modo fixo à direita.
        if (!_taskbar.IsEmpty && SameScreen(Screen.FromRectangle(_taskbar), screen))
        {
            int px = _anchor.Right - Width - EdgeMargin;
            int py = _taskbar.Top + (_taskbar.Height - h) / 2;
            return new Point(Math.Clamp(px, bounds.Left, Math.Max(bounds.Left, bounds.Right - Width)), py);
        }

        // Taskbar secundária desse monitor (se houver): fica à esquerda da bandeja/relógio dela.
        IntPtr tray = IntPtr.Zero;
        while ((tray = FindWindowEx(IntPtr.Zero, tray, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            if (!GetWindowRect(tray, out var r)) continue;
            var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (!bounds.Contains(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2)) continue;

            int right = rect.Right - 140; // Win11 não tem TrayNotifyWnd na secundária: reserva o espaço do relógio
            IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (notify != IntPtr.Zero && GetWindowRect(notify, out var nr) && nr.Width > 0) right = nr.Left;

            int x = Math.Clamp(right - Width - EdgeMargin, bounds.Left, Math.Max(bounds.Left, bounds.Right - Width));
            return new Point(x, rect.Top + (rect.Height - h) / 2);
        }

        // Sem taskbar nesse monitor: canto inferior direito.
        return new Point(bounds.Right - Width - 12, bounds.Bottom - h - 12);
    }

    private Point FreeLocation()
    {
        // Primeira vez no modo livre: começa onde está.
        if (_settings.FreeX is null || _settings.FreeY is null)
        {
            _settings.FreeX = Left;
            _settings.FreeY = Top;
        }
        return ClampToScreens(new Point(_settings.FreeX.Value, _settings.FreeY.Value));
    }

    /// <summary>Garante que a janela continue visível (ex.: monitor desconectado).</summary>
    private Point ClampToScreens(Point p)
    {
        var rect = new Rectangle(p, Size);
        var screen = Screen.FromRectangle(rect).Bounds;
        int x = Math.Clamp(p.X, screen.Left, Math.Max(screen.Left, screen.Right - Width));
        int y = Math.Clamp(p.Y, screen.Top, Math.Max(screen.Top, screen.Bottom - Height));
        return new Point(x, y);
    }

    /// <summary>Monitor ocupado por um app em tela cheia em primeiro plano, ou null se não há nenhum.</summary>
    private static Screen? ForegroundFullscreenScreen()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return null;

        string cls = ClassNameOf(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return null;
        if (!GetWindowRect(fg, out var r)) return null;

        var screen = Screen.FromHandle(fg);
        var b = screen.Bounds;
        bool full = r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
        return full && !IsShellWindow(fg) ? screen : null;
    }

    /// <summary>
    /// Janelas do próprio Windows (bandeja/setinha de ícones ocultos, Iniciar, Pesquisa, Central de Ações…)
    /// podem ter o tamanho do monitor inteiro, mas não são apps em tela cheia.
    /// </summary>
    private static bool IsShellWindow(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out uint pid);
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName is "explorer" or "ShellExperienceHost" or "StartMenuExperienceHost"
                or "SearchHost" or "SearchApp" or "TextInputHost";
        }
        catch { return false; }
    }

    // ===================== layout / desenho =====================

    private void RecalcLayout(int height)
    {
        _layoutHeight = height;

        // Segoe UI: altura de linha ≈ 1.33 × em. Duas linhas cabendo na altura disponível.
        float emPx = Math.Max(9f, MathF.Floor((height - 4) / 2f / 1.33f));
        var oldLabel = _labelFont;
        var oldValue = _valueFont;
        _labelFont = new Font("Segoe UI Semibold", emPx + 1, FontStyle.Regular, GraphicsUnit.Pixel);
        _valueFont = new Font("Segoe UI", emPx, FontStyle.Regular, GraphicsUnit.Pixel);
        oldLabel.Dispose();
        oldValue.Dispose();

        // Larguras fixas a partir de "piores casos" para o texto não ficar pulando.
        _labelW = new[] { "CPU", "GPU", "RAM" }.Max(t => Measure(t, _labelFont));
        _colLoadW = Math.Max(Measure("100%", _valueFont), Measure("100°C", _valueFont));
        _colMemW = Math.Max(Measure("VRAM 100%", _valueFont), Measure("88,8/88,8G", _valueFont));

        int cpu = _labelW + LabelGap + _colLoadW;
        int gpu = _labelW + LabelGap + _colLoadW + ColGap + _colMemW;
        int ram = _labelW + LabelGap + _colMemW;
        Width = Pad + cpu + GroupGap + gpu + GroupGap + ram + Pad;
        Height = height;
    }

    private static int Measure(string text, Font font) =>
        TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 2;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var path = Shapes.RoundedRect(new Rectangle(0, 0, Width, Height), 6);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(_theme.Background);

        var s = _snap;
        int lineH = _valueFont.Height;
        int row1 = (ClientSize.Height - lineH * 2) / 2;
        int row2 = row1 + lineH;
        int x = Pad;

        // ---- CPU ----
        DrawLabel(g, "CPU", _theme.CpuAccent, x);
        x += _labelW + LabelGap;
        DrawValue(g, Pct(s?.CpuLoad), LoadColor(s?.CpuLoad), x, row1, _colLoadW, lineH);
        DrawValue(g, Temp(s?.CpuTemp), TempColor(s?.CpuTemp), x, row2, _colLoadW, lineH);
        x += _colLoadW + GroupGap;

        // ---- GPU + VRAM (lado a lado) ----
        DrawLabel(g, "GPU", _theme.GpuAccent, x);
        x += _labelW + LabelGap;
        DrawValue(g, Pct(s?.GpuLoad), LoadColor(s?.GpuLoad), x, row1, _colLoadW, lineH);
        DrawValue(g, Temp(s?.GpuTemp), TempColor(s?.GpuTemp), x, row2, _colLoadW, lineH);
        x += _colLoadW + ColGap;

        float? vramPct = s?.VramUsedMb is float u && s.VramTotalMb is float t && t > 0 ? u / t * 100f : null;
        DrawValue(g, "VRAM " + Pct(vramPct), LoadColor(vramPct), x, row1, _colMemW, lineH);
        DrawValue(g, Gb(s?.VramUsedMb / 1024f, s?.VramTotalMb / 1024f), _theme.Dim, x, row2, _colMemW, lineH);
        x += _colMemW + GroupGap;

        // ---- RAM ----
        DrawLabel(g, "RAM", _theme.RamAccent, x);
        x += _labelW + LabelGap;
        DrawValue(g, Pct(s?.RamLoad), LoadColor(s?.RamLoad), x, row1, _colMemW, lineH);
        DrawValue(g, Gb(s?.RamUsedGb, s?.RamTotalGb), _theme.Dim, x, row2, _colMemW, lineH);

        // Indicador discreto de "travado" no modo livre
        if (_settings.Mode == PlacementMode.Free && _settings.FreeLocked)
        {
            using var b = new SolidBrush(_theme.Dim);
            g.FillEllipse(b, Width - 6, 3, 3, 3);
        }
    }

    private void DrawLabel(Graphics g, string text, Color color, int x)
    {
        var rect = new Rectangle(x, 0, _labelW, ClientSize.Height);
        TextRenderer.DrawText(g, text, _labelFont, rect, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine | TextFormatFlags.NoClipping);
    }

    private void DrawValue(Graphics g, string text, Color color, int x, int y, int width, int height)
    {
        if (Loading)
        {
            // 2ª linha (temperatura / memória) usa barras um pouco mais largas que a 1ª.
            bool secondRow = y + height / 2 > ClientSize.Height / 2;
            DrawSkeleton(g, x, y, width, height, secondRow ? 0.8f : 0.6f);
            return;
        }

        var rect = new Rectangle(x, y, width, height);
        TextRenderer.DrawText(g, text, _valueFont, rect, color,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }

    private bool Loading => _snap is null && _sensorError is null;

    private void OnLoadingTick(object? sender, EventArgs e)
    {
        if (!Loading) { _loadingTimer.Stop(); return; }

        // O brilho atravessa a janela de ponta a ponta, com folga nas bordas para entrar e sair suave.
        float t = _loadingClock.ElapsedMilliseconds % ShimmerPeriodMs / (float)ShimmerPeriodMs;
        _shimmerX = -Width * 0.3f + t * Width * 1.6f;
        if (Visible) Invalidate();
    }

    /// <summary>Barra arredondada no lugar do valor; o brilho passa por ela em onda.</summary>
    private void DrawSkeleton(Graphics g, int x, int y, int width, int height, float ratio)
    {
        int bh = Math.Max(5, height / 2 - 1);
        int bw = Math.Max(bh, (int)(width * ratio));
        var rect = new Rectangle(x + width - bw, y + (height - bh) / 2, bw, bh); // à direita, como o texto

        float d = (rect.Left + rect.Width / 2f - _shimmerX) / (Width * 0.22f);
        float glow = MathF.Exp(-d * d); // gaussiana: 1 sob o brilho, ~0 longe dele

        var baseColor = Blend(_theme.Background, _theme.Foreground, 0.14f);
        var glowColor = Blend(_theme.Background, _theme.Foreground, 0.42f);
        using var brush = new SolidBrush(Blend(baseColor, glowColor, glow));
        using var path = Shapes.RoundedRect(rect, bh / 2);

        var oldMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPath(brush, path);
        g.SmoothingMode = oldMode;
    }

    private static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));

    private static string Pct(float? v) => v is float f ? $"{f:0}%" : "--";
    private static string Temp(float? v) => v is float f ? $"{f:0}°C" : "--°C";

    private static string Gb(float? used, float? total) => (used, total) switch
    {
        (float u, float t) => $"{u:0.0}/{t:0.#}G",
        (float u, null) => $"{u:0.0}G",
        _ => "--",
    };

    private Color LoadColor(float? v) => v switch
    {
        >= 90 => _theme.Critical,
        >= 70 => _theme.Warning,
        _ => _theme.Foreground,
    };

    private Color TempColor(float? v) => v switch
    {
        >= 85 => _theme.Critical,
        >= 72 => _theme.Warning,
        _ => _theme.Foreground,
    };

    private void UpdateTooltip()
    {
        var s = _snap;
        string text = s is null
            ? "Iniciando sensores…"
            : $"{s.CpuName}\n{s.GpuName}" +
              (s.CpuTemp is null ? "\n\nTemperatura da CPU indisponível (veja o README: driver PawnIO)." : "");
        if (_sensorError is not null) text += $"\n\nErro: {_sensorError}";

        if (text == _lastTooltip) return;
        _lastTooltip = text;
        _toolTip.SetToolTip(this, text);
    }

    // ===================== arrastar (modo livre) =====================

    private void UpdateCursor() => Cursor = CanDrag ? Cursors.SizeAll : Cursors.Default;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !CanDrag) return;
        _dragging = true;
        _topSuppressed = true;
        _dragStartCursor = Cursor.Position;
        _dragStartLocation = Location;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var c = Cursor.Position;
        var p = ClampToScreens(new Point(
            _dragStartLocation.X + c.X - _dragStartCursor.X,
            _dragStartLocation.Y + c.Y - _dragStartCursor.Y));
        Location = p;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        _topSuppressed = ContextMenuStrip?.Visible ?? false;
        Capture = false;
        _settings.FreeX = Left;
        _settings.FreeY = Top;
        _settings.Save();
    }

    // ===================== menu de contexto =====================

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        // ---- Modo ----
        var mode = new StyledMenuItem("Modo") { Icon = MenuStyle.Icons.Mode };
        _miDocked = new ToolStripMenuItem("Fixo na barra de tarefas", null, (_, _) => SetMode(PlacementMode.Docked));
        _miFree = new ToolStripMenuItem("Livre (arrastar)", null, (_, _) => SetMode(PlacementMode.Free));
        mode.DropDownItems.AddRange(new ToolStripItem[] { _miDocked, _miFree });

        // ---- Posição na barra (modo fixo) ----
        _miDockPos = new StyledMenuItem("Posição na barra") { Icon = MenuStyle.Icons.DockPosition };
        foreach (var (pos, label) in new[]
                 {
                     (DockPosition.Right, "Direita (junto à bandeja)"),
                     (DockPosition.Center, "Centro"),
                     (DockPosition.Left, "Esquerda"),
                 })
        {
            var item = new ToolStripMenuItem(label, null, (_, _) => SetDockPosition(pos));
            _miDockPositions[pos] = item;
            _miDockPos.DropDownItems.Add(item);
        }

        // ---- Travar (modo livre) ----
        _miLock = new StyledMenuItem("Travar posição", (_, _) =>
        {
            _settings.FreeLocked = !_settings.FreeLocked;
            _settings.Save();
            UpdateCursor();
            Invalidate();
        }) { Icon = MenuStyle.Icons.Lock, IsSwitch = true };

        // ---- Intervalo ----
        _miInterval = new StyledMenuItem("Intervalo de atualização") { Icon = MenuStyle.Icons.Interval };
        foreach (int ms in new[] { 500, 1000, 2000, 5000 })
        {
            var item = new ToolStripMenuItem($"{ms / 1000.0:0.#} s") { Tag = ms };
            item.Click += (_, _) =>
            {
                _settings.IntervalMs = ms;
                _settings.Save();
                _sensorTimer?.Change(0, ms);
            };
            _miInterval.DropDownItems.Add(item);
        }

        // ---- Autostart ----
        _miAutostart = new StyledMenuItem("Iniciar com o Windows") { Icon = MenuStyle.Icons.Autostart, IsSwitch = true };
        _miAutostart.Click += (_, _) =>
        {
            bool target = !_miAutostart.Checked;
            if (!Autostart.Set(target))
                MessageBox.Show("Não foi possível alterar a tarefa agendada.", "Taskbar Monitor",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };

        var exit = new StyledMenuItem("Sair", (_, _) => Close()) { Icon = MenuStyle.Icons.Exit, IsDanger = true };

        menu.Items.AddRange(new ToolStripItem[]
        {
            mode, _miDockPos, _miLock,
            new ToolStripSeparator(),
            _miInterval, _miAutostart,
            new ToolStripSeparator(),
            exit,
        });

        menu.Opening += (_, _) => SyncMenu();
        menu.Opened += (_, _) => { _topSuppressed = true; _menuPrevDown = AnyMouseButtonDown(); _menuWatch.Start(); };
        menu.Closed += (_, _) => { _topSuppressed = _dragging; _menuWatch.Stop(); };
        MenuRenderer.Attach(menu, MenuStyle.From(_theme));
        return menu;
    }

    private static bool AnyMouseButtonDown() =>
        IsKeyDown(VK_LBUTTON) || IsKeyDown(VK_RBUTTON) || IsKeyDown(VK_MBUTTON);

    private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool IsInsideMenu(ToolStripDropDown dropDown, Point screenPoint) =>
        dropDown.Bounds.Contains(screenPoint) ||
        dropDown.Items.OfType<ToolStripMenuItem>().Any(i => i.DropDown.Visible && IsInsideMenu(i.DropDown, screenPoint));

    /// <summary>Fecha o menu (e submenus) quando um botão do mouse é pressionado fora dele.</summary>
    private void OnMenuWatchTick(object? sender, EventArgs e)
    {
        var menu = ContextMenuStrip;
        if (menu is null || !menu.Visible) { _menuWatch.Stop(); return; }

        bool down = AnyMouseButtonDown();
        bool pressed = down && !_menuPrevDown; // só na transição, para não fechar por um clique já em curso
        _menuPrevDown = down;
        if (!pressed) return;

        var cursor = Cursor.Position;
        // Cliques na própria janela do monitor o Windows já trata sozinho.
        if (IsInsideMenu(menu, cursor) || Bounds.Contains(cursor)) return;
        menu.Close(ToolStripDropDownCloseReason.AppClicked);
    }

    /// <summary>Atualiza checks/visibilidade conforme o estado atual antes de abrir.</summary>
    private void SyncMenu()
    {
        bool docked = _settings.Mode == PlacementMode.Docked;

        _miDocked.Checked = docked;
        _miFree.Checked = !docked;

        _miDockPos.Visible = docked;
        foreach (var (pos, item) in _miDockPositions) item.Checked = _settings.DockPosition == pos;

        _miLock.Visible = !docked;
        _miLock.Checked = _settings.FreeLocked;

        foreach (ToolStripMenuItem i in _miInterval.DropDownItems) i.Checked = (int)i.Tag! == _settings.IntervalMs;

        _miAutostart.Checked = Autostart.IsEnabled();
    }

    private void SetMode(PlacementMode mode)
    {
        if (_settings.Mode == mode) return;

        if (mode == PlacementMode.Free)
        {
            // Solta exatamente onde está, destravado para poder arrastar.
            _settings.FreeX = Left;
            _settings.FreeY = Top;
            _settings.FreeLocked = false;
        }

        _settings.Mode = mode;
        _settings.Save();
        UpdateCursor();
        ApplyPosition();
        Invalidate();
    }

    private void SetDockPosition(DockPosition pos)
    {
        _settings.DockPosition = pos;
        _settings.Save();
        ApplyPosition();
    }

    // ===================== eventos de sistema =====================

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        _theme = Theme.Detect();
        if (ContextMenuStrip is ToolStripDropDownMenu menu) MenuRenderer.Attach(menu, MenuStyle.From(_theme));
        Invalidate();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _layoutHeight = -1;
        Reposition(force: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
            _uiTimer.Dispose();
            _topStop = true;
            _topThread?.Join(200);
            timeEndPeriod(1);
            _loadingTimer.Dispose();
            _menuWatch.Dispose();
            _sensorTimer?.Dispose();
            _toolTip.Dispose();
            _reader?.Dispose();
            _labelFont.Dispose();
            _valueFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
