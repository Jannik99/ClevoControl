using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ClevoControl.Core.Monitoring;

public sealed record GpuAdapterSample(
    string Luid,
    string Name,
    bool IsNvidia,
    double LoadPercent,
    IReadOnlyDictionary<string, double> EngineLoad,
    double DedicatedUsedMb,
    double DedicatedTotalMb,
    double SharedUsedMb);

/// <summary>
/// Load of every GPU (integrated and dedicated) the way Task Manager computes it: per engine the
/// sum over all processes from the "GPU Engine" performance counters, adapter load = busiest
/// engine. Adapter names come from DXGI via the LUID.
/// </summary>
public sealed partial class GpuEngineMonitor : IDisposable
{
    private readonly PerformanceCounterCategory? _engines;
    private readonly PerformanceCounterCategory? _memory;
    private Dictionary<string, CounterSample> _previous = [];
    private readonly Dictionary<string, (string Name, double DedicatedMb, bool Nvidia, bool Software)> _adapters;

    [GeneratedRegex(@"luid_(0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+)_phys_(\d+)_eng_(\d+)_engtype_(.*)$")]
    private static partial Regex EngineRegex();

    [GeneratedRegex(@"luid_(0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+)_phys_\d+$")]
    private static partial Regex AdapterRegex();

    public GpuEngineMonitor()
    {
        if (OperatingSystem.IsLinux()) { _adapters = []; _linux = true; return; }
        if (!OperatingSystem.IsWindows()) { _adapters = []; return; }
        _adapters = Dxgi.Adapters();
        try
        {
            _engines = new PerformanceCounterCategory("GPU Engine");
            _memory = new PerformanceCounterCategory("GPU Adapter Memory");
            _previous = ReadUtilization();
        }
        catch
        {
            _engines = null;
        }
    }

    private readonly bool _linux;

    public bool Available => _linux || _engines is not null;

    /// <summary>Linux: one entry per DRM card. Load is known for AMD (amdgpu); NVIDIA load comes from NVML.</summary>
    private static List<GpuAdapterSample> SampleLinux() =>
        Platform.LinuxHardwareInfo.Gpus().Select(g => new GpuAdapterSample(g.Card, g.Name, g.Vendor == "NVIDIA", g.BusyPercent ?? 0,
            g.BusyPercent is { } b ? new Dictionary<string, double> { ["3D"] = b } : new Dictionary<string, double>(),
            g.VramUsedMb ?? 0, g.VramTotalMb ?? 0, 0)).ToList();

    private Dictionary<string, CounterSample> ReadUtilization()
    {
        var result = new Dictionary<string, CounterSample>();
        if (_engines is null) return result;
        var data = _engines.ReadCategory();
        if (!data.Contains("Utilization Percentage")) return result;
        foreach (InstanceData inst in data["Utilization Percentage"].Values) result[inst.InstanceName] = inst.Sample;
        return result;
    }

    public List<GpuAdapterSample> Sample()
    {
        if (_linux) return SampleLinux();
        var list = new List<GpuAdapterSample>();
        if (_engines is null) return list;

        Dictionary<string, CounterSample> current;
        try { current = ReadUtilization(); }
        catch { return list; }

        // engine key (luid|phys|eng) → (type, summed utilisation over processes)
        var engines = new Dictionary<string, (string Luid, string Type, double Load)>();
        foreach (var (name, sample) in current)
        {
            var m = EngineRegex().Match(name);
            if (!m.Success) continue;
            double value = 0;
            if (_previous.TryGetValue(name, out var prev))
            {
                try { value = CounterSample.Calculate(prev, sample); } catch { }
            }
            var key = $"{m.Groups[1].Value}|{m.Groups[2].Value}|{m.Groups[3].Value}";
            var type = m.Groups[4].Value.Length == 0 ? "Other" : m.Groups[4].Value;
            engines.TryGetValue(key, out var e);
            engines[key] = (m.Groups[1].Value.ToUpperInvariant(), type, e.Load + value);
        }
        _previous = current;

        var memory = ReadMemory();
        foreach (var group in engines.Values.GroupBy(e => e.Luid))
        {
            var perType = group.GroupBy(e => e.Type).ToDictionary(g => g.Key, g => Math.Clamp(g.Max(x => x.Load), 0, 100));
            var load = perType.Count == 0 ? 0 : perType.Values.Max();
            _adapters.TryGetValue(group.Key, out var info);
            if (info.Software) continue;
            memory.TryGetValue(group.Key, out var mem);
            list.Add(new GpuAdapterSample(group.Key, info.Name ?? $"GPU {group.Key}", info.Nvidia, load, perType,
                mem.Dedicated, info.DedicatedMb, mem.Shared));
        }
        // Dedicated GPUs first, then the rest by name.
        return list.OrderByDescending(a => a.DedicatedTotalMb > 512).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private Dictionary<string, (double Dedicated, double Shared)> ReadMemory()
    {
        var result = new Dictionary<string, (double, double)>();
        if (_memory is null) return result;
        try
        {
            var data = _memory.ReadCategory();
            const double mb = 1024 * 1024;
            if (!data.Contains("Dedicated Usage")) return result;
            foreach (InstanceData inst in data["Dedicated Usage"].Values)
            {
                var m = AdapterRegex().Match(inst.InstanceName);
                if (!m.Success) continue;
                double shared = 0;
                if (data.Contains("Shared Usage") && data["Shared Usage"][inst.InstanceName] is { } s) shared = s.RawValue / mb;
                result[m.Groups[1].Value.ToUpperInvariant()] = (inst.RawValue / mb, shared);
            }
        }
        catch { }
        return result;
    }

    public void Dispose() { }

    /// <summary>Minimal DXGI enumeration (adapter LUID → name) through raw COM vtables.</summary>
    private static unsafe class Dxgi
    {
        [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DXGI_ADAPTER_DESC1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
            public uint VendorId, DeviceId, SubSysId, Revision;
            public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
            public uint LuidLow;
            public int LuidHigh;
            public uint Flags;
        }

        public static Dictionary<string, (string Name, double DedicatedMb, bool Nvidia, bool Software)> Adapters()
        {
            var result = new Dictionary<string, (string, double, bool, bool)>();
            try
            {
                var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
                if (CreateDXGIFactory1(ref iid, out var factory) != 0) return result;
                try
                {
                    var fvt = *(IntPtr**)factory;
                    var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)fvt[12];
                    for (uint i = 0; ; i++)
                    {
                        IntPtr adapter;
                        if (enumAdapters1(factory, i, &adapter) != 0) break;
                        try
                        {
                            var avt = *(IntPtr**)adapter;
                            var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)avt[10];
                            var size = Marshal.SizeOf<DXGI_ADAPTER_DESC1>();
                            var buf = Marshal.AllocHGlobal(size);
                            try
                            {
                                if (getDesc1(adapter, buf) == 0)
                                {
                                    var d = Marshal.PtrToStructure<DXGI_ADAPTER_DESC1>(buf);
                                    var luid = $"0X{(uint)d.LuidHigh:X8}_0X{d.LuidLow:X8}";
                                    result[luid] = (d.Description.Trim(), d.DedicatedVideoMemory / 1024.0 / 1024.0, d.VendorId == 0x10DE, (d.Flags & 2) != 0);
                                }
                            }
                            finally { Marshal.FreeHGlobal(buf); }
                        }
                        finally { Release(adapter); }
                    }
                }
                finally { Release(factory); }
            }
            catch { }
            return result;
        }

        private static void Release(IntPtr unk)
        {
            var vt = *(IntPtr**)unk;
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)vt[2])(unk);
        }
    }
}
