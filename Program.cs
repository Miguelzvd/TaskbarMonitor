namespace TaskbarMonitor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Global\TaskbarMonitor_SingleInstance", out bool createdNew);
        if (!createdNew) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new MonitorForm());
    }
}
