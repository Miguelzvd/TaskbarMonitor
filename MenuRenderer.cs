using System.Drawing.Drawing2D;
using System.Drawing.Text;
using static TaskbarMonitor.NativeMethods;

namespace TaskbarMonitor;

/// <summary>Item de menu com o que o <see cref="MenuRenderer"/> precisa para desenhar: ícone, interruptor e destaque de perigo.</summary>
internal sealed class StyledMenuItem : ToolStripMenuItem
{
    public StyledMenuItem(string text, EventHandler? onClick = null) : base(text, null, onClick) { }

    public string? Icon { get; init; }
    public bool IsSwitch { get; init; }
    public bool IsDanger { get; init; }
}

/// <summary>Desenha o menu de contexto conforme o <see cref="MenuStyle"/> e arredonda o painel.</summary>
internal sealed class MenuRenderer : ToolStripRenderer
{
    private static readonly Font TextFont = new(MenuStyle.FontName, MenuStyle.FontSize, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font IconFont = new(ResolveIconFontName(), MenuStyle.IconSize, FontStyle.Regular, GraphicsUnit.Pixel);

    private readonly MenuStyle _s;

    private MenuRenderer(MenuStyle style) => _s = style;

    /// <summary>Aplica o estilo ao menu e, recursivamente, aos submenus. Pode ser chamado de novo ao trocar o tema.</summary>
    public static void Attach(ToolStripDropDownMenu menu, MenuStyle style)
    {
        menu.Renderer = new MenuRenderer(style);
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = false;
        menu.Font = TextFont;
        menu.BackColor = style.Panel;
        menu.ForeColor = style.Text;
        menu.Padding = new Padding(MenuStyle.PanelPadding);
        menu.Opened -= RoundCorners;
        menu.Opened += RoundCorners;

        // O Padding do item não alarga o menu; a largura é calculada aqui e fixada em cada item.
        bool submenu = menu.OwnerItem is not null;
        int left = submenu ? MenuStyle.SubmenuIndent : MenuStyle.IconColumn;
        int right = submenu ? MenuStyle.SubmenuTrailing : MenuStyle.TrailingSpace;
        int textWidth = menu.Items.OfType<ToolStripMenuItem>().Select(MeasureText).DefaultIfEmpty(0).Max();
        var itemSize = new Size(MenuStyle.HighlightInset * 2 + left + textWidth + right, MenuStyle.ItemHeight);
        menu.MinimumSize = new Size(itemSize.Width, 0);

        foreach (ToolStripItem item in menu.Items)
        {
            if (item is ToolStripSeparator)
            {
                item.Margin = new Padding(0, 4, 0, 4);
                continue;
            }

            item.Margin = new Padding(0, 1, 0, 1);
            item.AutoSize = false;
            item.Size = itemSize;

            if (item is ToolStripMenuItem { HasDropDownItems: true } owner && owner.DropDown is ToolStripDropDownMenu sub)
                Attach(sub, style);
        }
    }

    private static int MeasureText(ToolStripMenuItem item) =>
        TextRenderer.MeasureText(item.Text, TextFont, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;

    // ===================== painel =====================

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) =>
        e.Graphics.Clear(_s.Panel);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), MenuStyle.PanelRadius);
        using var pen = new Pen(_s.Border);
        g.DrawPath(pen, path);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(_s.Border);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    // ===================== itens =====================

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is not ToolStripMenuItem item) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var state = g.Save();
        g.TranslateTransform(MenuStyle.HighlightInset, 0);
        var bounds = new Rectangle(0, 0, item.Width - MenuStyle.HighlightInset * 2, item.Height);
        bool hot = item.Enabled && (item.Selected || item.Pressed);

        if (hot)
        {
            using var path = RoundedRect(bounds, MenuStyle.ItemRadius);
            using var brush = new SolidBrush(_s.Hover);
            g.FillPath(brush, path);
        }

        var styled = item as StyledMenuItem;
        if (styled?.Icon is { } glyph)
        {
            var color = styled.IsDanger ? _s.Danger : hot ? _s.Accent : _s.TextDim;
            var iconRect = new Rectangle(10, 0, 22, bounds.Height);
            TextRenderer.DrawText(g, glyph, IconFont, iconRect, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        if (styled?.IsSwitch == true)
            DrawSwitch(g, bounds, item.Checked);
        else if (item.Checked)
            DrawDot(g, bounds);

        g.Restore(state);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var item = e.Item;
        bool submenu = (item.Owner as ToolStripDropDown)?.OwnerItem is not null;
        int left = submenu ? MenuStyle.SubmenuIndent : MenuStyle.IconColumn;
        int right = submenu ? MenuStyle.SubmenuTrailing : MenuStyle.TrailingSpace;
        int inset = MenuStyle.HighlightInset;
        var rect = new Rectangle(inset + left, 0, Math.Max(0, item.Width - inset * 2 - left - right), item.Height);

        var color = !item.Enabled ? _s.TextDim
            : item is StyledMenuItem { IsDanger: true } ? _s.Danger
            : _s.Text;

        TextRenderer.DrawText(e.Graphics, item.Text, item.Font, rect, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (e.Item is not { } item) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float x = item.Width - MenuStyle.HighlightInset - 18, cy = item.Height / 2f;
        using var pen = new Pen(item.Selected ? _s.Text : _s.TextDim, 1.5f)
        {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round,
        };
        g.DrawLines(pen, new[] { new PointF(x, cy - 4), new PointF(x + 4, cy), new PointF(x, cy + 4) });
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { } // marcas desenhadas em OnRenderMenuItemBackground

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) { }

    private void DrawSwitch(Graphics g, Rectangle bounds, bool on)
    {
        int w = MenuStyle.SwitchWidth, h = MenuStyle.SwitchHeight;
        var track = new Rectangle(bounds.Right - 12 - w, (bounds.Height - h) / 2, w, h);

        using (var path = RoundedRect(track, h / 2))
        using (var brush = new SolidBrush(on ? _s.Accent : _s.SwitchOff))
            g.FillPath(brush, path);

        int knob = h - 6;
        int kx = on ? track.Right - 3 - knob : track.Left + 3;
        using var knobBrush = new SolidBrush(on ? _s.SwitchKnobOn : _s.SwitchKnobOff);
        g.FillEllipse(knobBrush, kx, track.Top + 3, knob, knob);
    }

    private void DrawDot(Graphics g, Rectangle bounds)
    {
        const int d = 6;
        using var brush = new SolidBrush(_s.Accent);
        g.FillEllipse(brush, 14, (bounds.Height - d) / 2, d, d);
    }

    // ===================== cantos =====================

    private static void RoundCorners(object? sender, EventArgs e)
    {
        if (sender is not ToolStripDropDown dropDown || !dropDown.IsHandleCreated) return;

        if (Environment.OSVersion.Version.Build >= 22000)
        {
            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(dropDown.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            int noBorder = DWMWA_COLOR_NONE; // a borda é desenhada em OnRenderToolStripBorder
            DwmSetWindowAttribute(dropDown.Handle, DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));
        }
        else
        {
            using var path = RoundedRect(new Rectangle(Point.Empty, dropDown.Size), MenuStyle.PanelRadius);
            dropDown.Region = new Region(path);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string ResolveIconFontName()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }
}
