using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarMonitor;

internal enum PlacementMode
{
    /// <summary>Preso à barra de tarefas numa posição pré-definida.</summary>
    Docked,
    /// <summary>Solto: pode ser arrastado para qualquer lugar da tela.</summary>
    Free,
}

internal enum DockPosition
{
    /// <summary>À direita, colado na bandeja/relógio.</summary>
    Right,
    Center,
    Left,
}

internal sealed class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PlacementMode Mode { get; set; } = PlacementMode.Docked;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DockPosition DockPosition { get; set; } = DockPosition.Right;

    /// <summary>Posição absoluta (px) no modo livre; null = ainda não definida.</summary>
    public int? FreeX { get; set; }
    public int? FreeY { get; set; }

    /// <summary>No modo livre, impede arrastar.</summary>
    public bool FreeLocked { get; set; }

    /// <summary>Intervalo de leitura dos sensores, em ms.</summary>
    public int IntervalMs { get; set; } = 1000;

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarMonitor");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* arquivo corrompido ou formato antigo: volta ao padrão */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
