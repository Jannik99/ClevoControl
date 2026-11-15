using System.Runtime.InteropServices;
using System.Text;

namespace ClevoControl.Core.Monitoring;

public sealed record GpuSample(
    DateTime Time,
    double LoadPercent,
    double MemoryLoadPercent,
    double VramUsedMb,
    double VramTotalMb,
    double CoreClockMhz,
    double MemoryClockMhz,
    double PowerW,
    double? PowerLimitW,
    int TemperatureC,
    int? FanPercent,
    int PState);

/// <summary>
/// NVIDIA GPU details through NVML (nvml.dll, ships with every NVIDIA driver; user mode, read only).
/// Note: polling NVML keeps a hybrid-graphics dGPU awake, so this only runs while the detail page
/// is open.
/// </summary>
public sealed class GpuMonitor : IDisposable
{
    private const string Nvml = "nvml.dll";

    private IntPtr _device;
    private bool _initialized;

    [DllImport(Nvml, EntryPoint = "nvmlInit_v2")] private static extern int NvmlInit();
    [DllImport(Nvml, EntryPoint = "nvmlShutdown")] private static extern int NvmlShutdown();
    [DllImport(Nvml, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] private static extern int GetHandle(uint index, out IntPtr device);
    [DllImport(Nvml, EntryPoint = "nvmlDeviceGetName")] private static extern int GetName(IntPtr device, byte[] name, uint length);
    [DllImport(Nvml, EntryPoint = "nvmlDeviceGetUtilizationRates")] private static extern int GetUtilization(IntPtr device, out Utilization u);
    [DllImport(Nvml, EntryPoint = "nvmlDeviceGetMemoryInfo")] private static extern int GetMemory(IntPtr device, out Memory m);
    [DllImport(Nvml, EntryPoint = "nvmlDeviceGetClockInfo")] private static extern int GetClock(IntPtr device, int type, out uint mhz);
    [DllImport(Nvml, EntryPoint = "nvmlDeviceGetPowerUsage")] private static extern int GetPower(IntPtr device, out uint milliwatts);
    [DllImport(Nvml, EntryPoint = "nvmlDeviceGetEnforcedPowerLimit")] private static extern int GetPowerLimit(IntPtr device, out uint milliwatts);
    [DllImport(Nvml, EntryPoint = "nvmlDeviceGetTemperature")] private static extern int GetTemperature(IntPtr device, int sensor, out uint celsius);
    [DllImport(Nvml, EntryPoint = "nvmlDeviceGetFanSpeed")] private static extern int GetFan(IntPtr device, out uint percent);
    [DllImport(Nvml, EntryPoint = "nvmlDeviceGetPerformanceState")] private static extern int GetPState(IntPtr device, out int state);
    [DllImport(Nvml, EntryPoint = "nvmlSystemGetDriverVersion")] private static extern int GetDriverVersion(byte[] version, uint length);

    [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu, Memory; }
    [StructLayout(LayoutKind.Sequential)] private struct Memory { public ulong Total, Free, Used; }

    public GpuMonitor()
    {
        try
        {
            if (NvmlInit() != 0 || GetHandle(0, out _device) != 0) return;
            _initialized = true;
            var name = new byte[96];
            if (GetName(_device, name, (uint)name.Length) == 0) Name = Str(name);
            var drv = new byte[80];
            if (GetDriverVersion(drv, (uint)drv.Length) == 0) DriverVersion = Str(drv);
        }
        catch
        {
            // No NVIDIA driver / nvml.dll.
        }
    }

    public bool Available => _initialized;
    public string Name { get; } = "GPU";
    public string? DriverVersion { get; }

    private static string Str(byte[] b) => Encoding.ASCII.GetString(b, 0, Math.Max(0, Array.IndexOf(b, (byte)0) is var i and >= 0 ? i : b.Length)).Trim();

    public GpuSample? Sample()
    {
        if (!_initialized) return null;
        try
        {
            GetUtilization(_device, out var u);
            GetMemory(_device, out var m);
            GetClock(_device, 0, out var core);   // NVML_CLOCK_GRAPHICS
            GetClock(_device, 2, out var mem);    // NVML_CLOCK_MEM
            var hasPower = GetPower(_device, out var mw) == 0;
            double? limit = GetPowerLimit(_device, out var lim) == 0 ? lim / 1000.0 : null;
            GetTemperature(_device, 0, out var temp);
            int? fan = GetFan(_device, out var f) == 0 ? (int)f : null; // laptops usually report "not supported"
            GetPState(_device, out var ps);
            const double mb = 1024 * 1024;
            return new GpuSample(DateTime.Now, u.Gpu, u.Memory, m.Used / mb, m.Total / mb, core, mem,
                hasPower ? mw / 1000.0 : 0, limit, (int)temp, fan, ps);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (!_initialized) return;
        _initialized = false;
        try { NvmlShutdown(); } catch { }
    }
}
