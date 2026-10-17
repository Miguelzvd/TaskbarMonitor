using System.Drawing.Drawing2D;

namespace TaskbarMonitor;

/// <summary>Formas de desenho usadas pela janela do monitor e pelo menu de contexto.</summary>
internal static class Shapes
{
    /// <summary>Retângulo com cantos arredondados. O raio é limitado à metade do menor lado.</summary>
    public static GraphicsPath RoundedRect(Rectangle r, int radius)
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
}
