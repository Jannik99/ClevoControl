using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ClevoControl.Core.Platform;

/// <summary>Maps Windows library names used in [DllImport] to their Linux counterparts.</summary>
internal static class NativeResolver
{
#pragma warning disable CA2255 // a module initializer is the only place that runs before the first [DllImport]
    [ModuleInitializer]
    internal static void Init()
#pragma warning restore CA2255
    {
        if (!OperatingSystem.IsLinux()) return;
        NativeLibrary.SetDllImportResolver(typeof(NativeResolver).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        var candidates = name switch
        {
            "nvml.dll" => new[] { "libnvidia-ml.so.1", "libnvidia-ml.so" }, // part of the NVIDIA driver
            "libc" => ["libc.so.6", "libc"],
            _ => [],
        };
        foreach (var c in candidates)
            if (NativeLibrary.TryLoad(c, out var handle)) return handle;
        return IntPtr.Zero; // fall back to the default probing
    }
}
