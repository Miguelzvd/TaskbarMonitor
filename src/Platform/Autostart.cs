using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace TaskbarMonitor;

/// <summary>
/// Inicialização com o Windows via Agendador de Tarefas: roda elevado no logon
/// sem prompt do UAC, sem limite de 3 dias e também na bateria.
/// </summary>
internal static class Autostart
{
    private const string TaskName = "TaskbarMonitor";

    public static bool IsEnabled() => RunSchtasks("/Query", "/TN", TaskName) == 0;

    public static bool Set(bool enable)
    {
        if (!enable) return RunSchtasks("/Delete", "/TN", TaskName, "/F") == 0;

        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Caminho do executável indisponível.");
        string user = WindowsIdentity.GetCurrent().Name;

        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Monitor de hardware na barra de tarefas</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <Delay>PT10S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(exe)}</Command></Exec>
              </Actions>
            </Task>
            """;

        string tmp = Path.Combine(Path.GetTempPath(), $"TaskbarMonitor_{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            return RunSchtasks("/Create", "/TN", TaskName, "/XML", tmp, "/F") == 0;
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    private static int RunSchtasks(params string[] args)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi);
        if (p is null) return -1;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(10_000);
        return p.HasExited ? p.ExitCode : -1;
    }
}
