using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ClevoControl.Core.Hardware.PerKey;

/// <summary>Identity of a HID top-level collection, as read with HidD_GetAttributes / HidP_GetCaps.</summary>
public readonly record struct HidInfo(ushort Vendor, ushort Product, ushort Version, ushort UsagePage, ushort Usage, int FeatureLength, int OutputLength);

/// <summary>
/// Per-key keyboard on Windows: talks to the ITE controller through the standard HID class driver
/// (SetupAPI enumeration, <c>HidD_SetFeature</c> for commands, <c>WriteFile</c> for the row data).
/// The controller's collection uses a vendor usage page (0xFF03 / 0xFF89), which Windows does not open
/// exclusively (only keyboard and mouse collections are), so normal user rights are enough – unless another
/// program opened it without sharing, then <see cref="TryOpen"/> logs that and returns null.
/// </summary>
public sealed class WindowsHidPerKey : PerKeyKeyboardBase, IDisposable
{
    public const ushort IteVendor = 0x048D;

    /// <summary>ITE 8291 product ids: tuxedo-drivers ite_8291 (ce00, 6004, 600a, 600b) and ite8291r3-ctl (6006).</summary>
    public static readonly ushort[] Ite8291Products = [0xCE00, 0x6004, 0x6006, 0x600A, 0x600B];

    public const ushort Ite829xProduct = 0x8910;

    /// <summary>Minimum time between two attempts to reopen the device after it went away.</summary>
    private static readonly TimeSpan ReopenInterval = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private SafeFileHandle? _handle;
    private HidInfo _info;
    private bool _broken;
    private bool _disposed;
    private DateTime _nextReopen = DateTime.MinValue;

    private WindowsHidPerKey(PerKeyController controller, SafeFileHandle handle, HidInfo info) : base(controller)
    {
        _handle = handle;
        _info = info;
    }

    public override string Description =>
        $"{(Controller == PerKeyController.Ite8291 ? "ITE 8291" : "ITE 829x")} (USB {_info.Vendor:X4}:{_info.Product:X4} rev {_info.Version:X4}), Windows HID";

    /// <summary>
    /// Strict check whether a HID collection is a per-key controller this app can drive. Pure (unit-tested).
    /// <list type="bullet">
    /// <item>ITE 8291: vendor 048d, product from <see cref="Ite8291Products"/> but not ce00 rev 0002 (a 4-zone keyboard in
    /// tuxedo-drivers), usage page 0xFF03 / usage 0x01 (OpenRGB's detector), a feature report of 8 bytes and an output
    /// report of 64 bytes (plus the report id byte).</item>
    /// <item>ITE 829x: vendor 048d, product 8910, usage page 0xFF89 / usage 0xCC (OpenRGB), a 6-byte feature report.</item>
    /// </list>
    /// </summary>
    public static PerKeyController? Classify(HidInfo h)
    {
        if (h.Vendor != IteVendor) return null;
        if (Ite8291Products.Contains(h.Product))
        {
            if (h.Product == 0xCE00 && h.Version == 0x0002) return null;
            if (h.UsagePage != 0xFF03 || h.Usage != 0x01) return null;
            if (h.FeatureLength < 1 + Ite8291Protocol.ControlLength || h.OutputLength < Ite8291Protocol.RowReportLength) return null;
            return PerKeyController.Ite8291;
        }
        if (h.Product == Ite829xProduct)
        {
            if (h.UsagePage != 0xFF89 || h.Usage != 0xCC) return null;
            if (h.FeatureLength < Ite829xProtocol.ReportLength) return null;
            return PerKeyController.Ite829x;
        }
        return null;
    }

    /// <summary>Finds and opens a per-key controller; null (nothing is written) if there is none.</summary>
    public static WindowsHidPerKey? TryOpen()
    {
        if (!OperatingSystem.IsWindows()) return null;
        return Open(null, log: true) is { } found ? new WindowsHidPerKey(found.Controller, found.Handle, found.Info) : null;
    }

    /// <summary>
    /// Opens the first per-key controller (or, with <paramref name="same"/>, the one with the same vendor, product and
    /// controller type – used to reopen it after the USB device re-enumerated). Null if none can be opened.
    /// </summary>
    private static (SafeFileHandle Handle, HidInfo Info, PerKeyController Controller)? Open((HidInfo Info, PerKeyController Controller)? same, bool log)
    {
        foreach (var path in HidPaths())
        {
            // Cheap filter before opening anything: only ITE devices.
            if (!path.Contains("vid_048d", StringComparison.OrdinalIgnoreCase)) continue;
            HidInfo info;
            using (var query = CreateFile(path, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
            {
                if (query.IsInvalid || ReadInfo(query) is not { } i) continue;
                info = i;
            }
            if (Classify(info) is not { } controller)
            {
                if (log) Log.Info($"Per-key: ignoring HID {info.Vendor:X4}:{info.Product:X4} rev {info.Version:X4} usage {info.UsagePage:X4}/{info.Usage:X2} (not a known per-key controller)");
                continue;
            }
            if (same is { } s && (info.Vendor != s.Info.Vendor || info.Product != s.Info.Product || controller != s.Controller)) continue;
            var handle = CreateFile(path, GenericRead | GenericWrite, FileShareReadWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                var err = Marshal.GetLastWin32Error();
                handle.Dispose();
                if (log) Log.Warn($"Per-key: found {controller} ({info.Product:X4}) but cannot open it ({new Win32Exception(err).Message}) – another program may hold it exclusively. Keyboard stays single color.");
                return null;
            }
            if (log) Log.Info($"Per-key: {controller} at {path} (feature {info.FeatureLength} B, output {info.OutputLength} B)");
            return (handle, info, controller);
        }
        return null;
    }

    /// <summary>
    /// The open device handle. After a failed write (e.g. the USB device re-enumerated on resume from hibernation and
    /// the old handle is dead) it is closed and the same controller is searched and opened again, at most every
    /// <see cref="ReopenInterval"/>. Throws while the device is unavailable; the callers retry on the next frame.
    /// </summary>
    private SafeFileHandle Handle()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_broken && _handle is { IsInvalid: false, IsClosed: false } h) return h;
            var now = DateTime.UtcNow;
            if (now < _nextReopen) throw new IOException("Per-key keyboard is not available (waiting to reopen it)");
            _nextReopen = now + ReopenInterval;
            _handle?.Dispose();
            _handle = null;
            if (Open((_info, Controller), log: false) is not { } found)
                throw new IOException("Per-key keyboard not found (USB device gone or re-enumerating)");
            _handle = found.Handle;
            _info = found.Info;
            _broken = false;
            Invalidate(); // the controller state is unknown, send everything again
            Log.Info($"Per-key: reopened {Description}");
            return found.Handle;
        }
    }

    /// <summary>Marks the handle as unusable so the next write reopens the device.</summary>
    private void MarkBroken()
    {
        lock (_gate) _broken = true;
    }

    // ---- transport ----------------------------------------------------------------------------

    protected override void WriteFrame(RgbColor[] frame, bool[] changed, bool all)
    {
        if (Controller == PerKeyController.Ite8291)
        {
            // tuxedo-drivers always sends the user mode and all six rows.
            SendRows8291(frame);
            return;
        }
        for (var row = 0; row < Ite829xProtocol.Rows; row++)
        {
            for (var col = 0; col < Ite829xProtocol.Columns; col++)
            {
                var i = row * Ite829xProtocol.Columns + col;
                if (all || changed[i]) Feature(Ite829xProtocol.SetKey(IteKeyMap.Ite829xLedId(row, col), frame[i]));
            }
        }
    }

    private void SendRows8291(RgbColor[] frame)
    {
        Feature(Ite8291Protocol.UserMode(Ite8291Protocol.ScaleBrightness(Brightness)));
        for (var row = 0; row < Ite8291Protocol.Rows; row++)
        {
            Feature(Ite8291Protocol.AnnounceRow(row));
            Output(Ite8291Protocol.RowReport(frame, row));
        }
    }

    protected override void WriteBrightness(byte level)
    {
        if (Controller == PerKeyController.Ite829x)
            Feature(Ite829xProtocol.Brightness(Ite829xProtocol.ScaleBrightness(level)));
        // The 8291 has no separate brightness command: it is part of the user-mode and the effect command.
        // Sending the rows would switch to user mode and end a running built-in effect, so restart that instead.
        else if (ActiveHardwareEffect is { } fx) WriteHardwareEffect(fx.Effect, fx.Color);
        else SendRows8291(Frame);
    }

    protected override void WriteOff()
    {
        if (Controller == PerKeyController.Ite8291) Feature(Ite8291Protocol.Off());
        else Feature(Ite829xProtocol.Brightness(0));
    }

    protected override void WriteHardwareEffect(HardwareEffect effect, RgbColor color)
    {
        if (Controller == PerKeyController.Ite829x)
        {
            // The 829x only has a random-color animation; other effects keep the static color.
            if (effect == HardwareEffect.RandomColor) Feature(Ite829xProtocol.RandomAnimation());
            return;
        }
        var (mode, slot, behaviour) = Ite8291Protocol.EffectFor(effect);
        Feature(Ite8291Protocol.Palette(1, color));
        Feature(Ite8291Protocol.Effect(mode, 5, Ite8291Protocol.ScaleBrightness(Brightness), slot, behaviour));
    }

    /// <summary>Sends a feature report. 8291: report id 0 + 8 bytes; 829x: the report already starts with its id 0xCC.</summary>
    private void Feature(byte[] report)
    {
        var handle = Handle();
        var buf = new byte[_info.FeatureLength];
        if (Controller == PerKeyController.Ite8291) report.CopyTo(buf, 1);
        else report.CopyTo(buf, 0);
        if (!HidD_SetFeature(handle, buf, buf.Length)) Fail("HidD_SetFeature failed", Marshal.GetLastWin32Error());
    }

    /// <summary>Writes an output report (interrupt OUT) with a timeout, so a stuck device cannot block the lighting thread.</summary>
    private unsafe void Output(byte[] report)
    {
        var handle = Handle();
        var buf = new byte[_info.OutputLength];
        report.CopyTo(buf, 0);
        using var done = new ManualResetEvent(false);
        var ov = new NativeOverlapped { EventHandle = done.SafeWaitHandle.DangerousGetHandle() };
        fixed (byte* p = buf)
        {
            if (!WriteFile(handle, p, buf.Length, out _, &ov))
            {
                var err = Marshal.GetLastWin32Error();
                if (err != ErrorIoPending) Fail("HID write failed", err);
                if (!done.WaitOne(500))
                {
                    CancelIoEx(handle, &ov);
                    GetOverlappedResult(handle, &ov, out _, true); // the buffer must stay pinned until the cancel completes
                    throw new IOException("HID write timed out");
                }
            }
            if (!GetOverlappedResult(handle, &ov, out _, false)) Fail("HID write failed", Marshal.GetLastWin32Error());
        }
    }

    /// <summary>
    /// Throws for a failed transfer. Errors that mean the handle no longer reaches a device (it was removed or
    /// re-enumerated, e.g. after hibernation) mark it broken, so the next write reopens the controller.
    /// </summary>
    private void Fail(string what, int err)
    {
        if (err is ErrorFileNotFound or ErrorInvalidHandle or ErrorBadCommand or ErrorGenFailure or ErrorNoSuchDevice or ErrorDeviceNotConnected)
            MarkBroken();
        throw new IOException($"{what}: {new Win32Exception(err).Message}");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _handle?.Dispose();
            _handle = null;
        }
    }

    // ---- enumeration ----------------------------------------------------------------------------

    private static IEnumerable<string> HidPaths()
    {
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) yield break;
        try
        {
            var data = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
                if (size == 0) continue;
                var detail = Marshal.AllocHGlobal(size);
                string? path = null;
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 on 64-bit, 6 on 32-bit (DWORD + one WCHAR, packed).
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero))
                        path = Marshal.PtrToStringUni(detail + 4);
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
                if (path is not null) yield return path;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static HidInfo? ReadInfo(SafeFileHandle h)
    {
        var attr = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
        if (!HidD_GetAttributes(h, ref attr)) return null;
        if (attr.VendorID != IteVendor) return new HidInfo(attr.VendorID, attr.ProductID, attr.VersionNumber, 0, 0, 0, 0);
        if (!HidD_GetPreparsedData(h, out var pre)) return null;
        try
        {
            if (HidP_GetCaps(pre, out var caps) != HidpStatusSuccess) return null;
            return new HidInfo(attr.VendorID, attr.ProductID, attr.VersionNumber, caps.UsagePage, caps.Usage, caps.FeatureReportByteLength, caps.OutputReportByteLength);
        }
        finally
        {
            HidD_FreePreparsedData(pre);
        }
    }

    // ---- Win32 ----------------------------------------------------------------------------------

    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, FileShareReadWrite = 3, OpenExisting = 3, FileFlagOverlapped = 0x40000000;
    private const uint DigcfPresent = 0x02, DigcfDeviceInterface = 0x10;
    private const int HidpStatusSuccess = 0x00110000, ErrorIoPending = 997;
    private const int ErrorFileNotFound = 2, ErrorInvalidHandle = 6, ErrorBadCommand = 22, ErrorGenFailure = 31,
        ErrorNoSuchDevice = 433, ErrorDeviceNotConnected = 1167;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes { public int Size; public ushort VendorID, ProductID, VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    // The HidD_* functions return a 1-byte BOOLEAN, not a 4-byte BOOL.
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetAttributes(SafeFileHandle h, ref HiddAttributes attributes);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HidpCaps caps);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buffer, int length);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SpDeviceInterfaceData data);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, int size, out int required, IntPtr devInfo);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool WriteFile(SafeFileHandle h, byte* buffer, int length, out int written, NativeOverlapped* overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool GetOverlappedResult(SafeFileHandle h, NativeOverlapped* overlapped, out int transferred, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool CancelIoEx(SafeFileHandle h, NativeOverlapped* overlapped);
}
