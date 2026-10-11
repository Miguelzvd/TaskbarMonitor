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

    private bool CanDrag => _settings.Mode == PlacementMode.Free && !_settings.FreeLocked;

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
                UpdateTooltip();
                Invalidate();
            });
        }
        catch (Exception ex)
        {
            PostToUi(() => { _sensorError = ex.Message; UpdateTooltip(); });
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
        bool fullscreen = IsForegroundFullscreen();
        if (fullscreen && Visible) { Hide(); return; }
        if (!fullscreen && !Visible) Show();
        if (!Visible) return;

        if (!_dragging) Reposition();
        // A taskbar também é topmost e "sobe" quando clicada; reafirma nossa posição.
        if (!_dragging && !(ContextMenuStrip?.Visible ?? false))
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
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

        Point p = _settings.Mode == PlacementMode.Docked ? DockedLocation(h) : FreeLocation();
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

    private static bool IsForegroundFullscreen()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        string cls = ClassNameOf(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (!GetWindowRect(fg, out var r)) return false;

        var screen = Screen.FromHandle(fg).Bounds;
        return r.Left <= screen.Left && r.Top <= screen.Top && r.Right >= screen.Right && r.Bottom >= screen.Bottom;
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
        using var path = RoundedRect(new Rectangle(0, 0, Width, Height), 6);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
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
        var rect = new Rectangle(x, y, width, height);
        TextRenderer.DrawText(g, text, _valueFont, rect, color,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }

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
        MenuRenderer.Attach(menu, MenuStyle.From(_theme));
        return menu;
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
            _uiTimer.Dispose();
            _sensorTimer?.Dispose();
            _toolTip.Dispose();
            _reader?.Dispose();
            _labelFont.Dispose();
            _valueFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed record Theme(
    Color Background, Color Foreground, Color Dim,
    Color Warning, Color Critical,
    Color CpuAccent, Color GpuAccent, Color RamAccent)
{
    public static Theme Detect()
    {
        bool light = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch { }

        return light
            ? new Theme(
                Background: Color.FromArgb(238, 238, 238),
                Foreground: Color.FromArgb(28, 28, 28),
                Dim: Color.FromArgb(96, 96, 96),
                Warning: Color.FromArgb(176, 112, 0),
                Critical: Color.FromArgb(200, 40, 40),
                CpuAccent: Color.FromArgb(0, 120, 190),
                GpuAccent: Color.FromArgb(40, 140, 60),
                RamAccent: Color.FromArgb(140, 60, 160))
            : new Theme(
                Background: Color.FromArgb(28, 28, 28),
                Foreground: Color.FromArgb(232, 232, 232),
                Dim: Color.FromArgb(160, 160, 160),
                Warning: Color.FromArgb(255, 193, 7),
                Critical: Color.FromArgb(255, 82, 82),
                CpuAccent: Color.FromArgb(79, 195, 247),
                GpuAccent: Color.FromArgb(129, 199, 132),
                RamAccent: Color.FromArgb(206, 147, 216));
    }
}
