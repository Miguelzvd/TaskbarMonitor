using Microsoft.Win32;

namespace TaskbarMonitor;

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
