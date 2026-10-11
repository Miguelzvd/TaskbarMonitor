using LibreHardwareMonitor.Hardware;

namespace TaskbarMonitor;

internal sealed record Snapshot(
    float? CpuLoad, float? CpuTemp,
    float? GpuLoad, float? GpuTemp, float? VramUsedMb, float? VramTotalMb,
    float? RamLoad, float? RamUsedGb, float? RamTotalGb,
    string CpuName, string GpuName);

/// <summary>Lê os sensores via LibreHardwareMonitorLib.</summary>
internal sealed class SensorReader : IDisposable
{
    private readonly Computer _computer;

    public SensorReader()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
        };
        _computer.Open();
    }

    public Snapshot Read()
    {
        foreach (var hw in _computer.Hardware) UpdateRecursive(hw);

        var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        var gpu = PickGpu();
        var mem = _computer.Hardware.FirstOrDefault(h =>
            h.HardwareType == HardwareType.Memory && Value(h, SensorType.Data, "Memory Used") is not null);

        // CPU — AMD expõe "Core (Tctl/Tdie)", Intel expõe "CPU Package"
        float? cpuLoad = Value(cpu, SensorType.Load, "CPU Total");
        float? cpuTemp = Value(cpu, SensorType.Temperature,
                             "Core (Tctl/Tdie)", "CPU Package", "Core (Tctl)", "Core (Tdie)", "Package")
                         ?? Max(cpu, SensorType.Temperature, n => n.StartsWith("Core", StringComparison.OrdinalIgnoreCase))
                         ?? Max(cpu, SensorType.Temperature, _ => true);

        // GPU
        float? gpuLoad = Value(gpu, SensorType.Load, "GPU Core", "D3D 3D");
        float? gpuTemp = Value(gpu, SensorType.Temperature, "GPU Core")
                         ?? Max(gpu, SensorType.Temperature, _ => true);
        float? vramUsed = Value(gpu, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
        float? vramTotal = Value(gpu, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total");

        // RAM
        float? ramLoad = Value(mem, SensorType.Load, "Memory");
        float? ramUsed = Value(mem, SensorType.Data, "Memory Used");
        float? ramAvail = Value(mem, SensorType.Data, "Memory Available");
        float? ramTotal = ramUsed + ramAvail;

        return new Snapshot(
            cpuLoad, cpuTemp,
            gpuLoad, gpuTemp, vramUsed, vramTotal,
            ramLoad, ramUsed, ramTotal,
            cpu?.Name ?? "CPU", gpu?.Name ?? "GPU não encontrada");
    }

    /// <summary>Prefere GPU dedicada (NVIDIA > AMD > Intel).</summary>
    private IHardware? PickGpu()
    {
        static int Rank(HardwareType t) => t switch
        {
            HardwareType.GpuNvidia => 0,
            HardwareType.GpuAmd => 1,
            HardwareType.GpuIntel => 2,
            _ => 99,
        };

        return _computer.Hardware
            .Where(h => Rank(h.HardwareType) < 99)
            .OrderBy(h => Rank(h.HardwareType))
            .ThenByDescending(h => Value(h, SensorType.SmallData, "GPU Memory Total") ?? 0)
            .FirstOrDefault();
    }

    private static void UpdateRecursive(IHardware hw)
    {
        hw.Update();
        foreach (var sub in hw.SubHardware) UpdateRecursive(sub);
    }

    private static IEnumerable<ISensor> AllSensors(IHardware? hw) =>
        hw is null
            ? Enumerable.Empty<ISensor>()
            : hw.Sensors.Concat(hw.SubHardware.SelectMany(s => AllSensors(s)));

    private static bool Valid(ISensor s) => s.Value.HasValue && !float.IsNaN(s.Value.Value);

    private static float? Value(IHardware? hw, SensorType type, params string[] names)
    {
        var sensors = AllSensors(hw).Where(s => s.SensorType == type && Valid(s)).ToList();
        foreach (var name in names)
        {
            var hit = sensors.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit.Value;
        }
        return null;
    }

    private static float? Max(IHardware? hw, SensorType type, Func<string, bool> filter)
    {
        var values = AllSensors(hw)
            .Where(s => s.SensorType == type && Valid(s) && filter(s.Name))
            .Select(s => s.Value!.Value)
            .ToList();
        return values.Count > 0 ? values.Max() : null;
    }

    public void Dispose() => _computer.Close();
}
