namespace TaskbarMonitor;

/// <summary>
/// Só os valores visuais do menu de contexto: cores (derivadas do tema), medidas, fonte e ícones.
/// O desenho fica no <see cref="MenuRenderer"/>; quem monta os itens é o MonitorForm.
/// </summary>
internal sealed record MenuStyle(
    Color Panel, Color Border, Color Text, Color TextDim, Color Danger,
    Color Accent, Color Hover, Color SwitchOff, Color SwitchKnobOn, Color SwitchKnobOff)
{
    public const string FontName = "Segoe UI";
    public const float FontSize = 9.5f; // pt
    public const float IconSize = 16f;  // px

    public const int PanelRadius = 8;     // igual ao arredondamento nativo do Windows 11
    public const int ItemRadius = 6;
    public const int PanelPadding = 6;
    public const int ItemHeight = 34;
    public const int HighlightInset = 4;  // margem entre o realce e a borda do painel, nos dois lados
    public const int IconColumn = 38;     // espaço do ícone, à esquerda dos itens principais
    public const int SubmenuIndent = 34;  // espaço do ponto de seleção, nos submenus
    public const int TrailingSpace = 44;  // espaço da seta ou do interruptor, à direita
    public const int SubmenuTrailing = 16;
    public const int SwitchWidth = 30;
    public const int SwitchHeight = 16;

    /// <summary>Glifos da fonte de ícones do Windows (Segoe Fluent Icons / Segoe MDL2 Assets).</summary>
    public static class Icons
    {
        public const string Mode = "";
        public const string DockPosition = "";
        public const string Lock = "";
        public const string Interval = "";
        public const string Autostart = "";
        public const string Exit = "";
    }

    public static MenuStyle From(Theme theme)
    {
        bool dark = theme.Background.GetBrightness() < 0.5f;

        return dark
            ? new MenuStyle(
                Panel: Color.FromArgb(32, 32, 32),
                Border: Color.FromArgb(58, 58, 58),
                Text: theme.Foreground,
                TextDim: theme.Dim,
                Danger: theme.Critical,
                Accent: theme.CpuAccent,
                Hover: Color.FromArgb(40, theme.CpuAccent),
                SwitchOff: Color.FromArgb(74, 74, 74),
                SwitchKnobOn: Color.FromArgb(28, 28, 28),
                SwitchKnobOff: theme.Dim)
            : new MenuStyle(
                Panel: Color.FromArgb(249, 249, 249),
                Border: Color.FromArgb(214, 214, 214),
                Text: theme.Foreground,
                TextDim: theme.Dim,
                Danger: theme.Critical,
                Accent: theme.CpuAccent,
                Hover: Color.FromArgb(34, theme.CpuAccent),
                SwitchOff: Color.FromArgb(180, 180, 180),
                SwitchKnobOn: Color.White,
                SwitchKnobOff: Color.White);
    }
}
