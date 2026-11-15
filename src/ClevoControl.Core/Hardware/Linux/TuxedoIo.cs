using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ClevoControl.Core.Hardware.Linux;

/// <summary>
/// The Clevo part of the <c>/dev/tuxedo_io</c> interface from the open-source tuxedo-drivers
/// kernel package. Kept behind an interface so <see cref="LinuxClevoHardware"/> can be tested
/// without the device.
/// </summary>
public interface ITuxedoIo : IDisposable
{
    /// <summary>True if the driver found a Clevo ACPI/WMI interface.</summary>
    bool IsClevo();

    /// <summary>"clevo_acpi" or "clevo_wmi" (empty if none).</summary>
    string InterfaceName();

    string ModuleVersion();

    /// <summary>Raw fan info for fan 0..2: bits 0-7 duty (0-255), bits 16-23 temperature (°C, signed).</summary>
    uint ReadFanInfo(int fan);

    /// <summary>Fixed duty, one byte per fan (0-255): fan0 | fan1 &lt;&lt; 8 | fan2 &lt;&lt; 16.</summary>
    void SetFanSpeed(uint packed);

    /// <summary>Hands the fans in <paramref name="fanMask"/> (bit per fan) back to the EC.</summary>
    void SetFanAuto(uint fanMask);

    /// <summary>Clevo performance profile, same value as on Windows (0 quiet … 3 entertainment).</summary>
    void SetPerformanceProfile(uint value);
}

/// <summary>
/// ioctl access to /dev/tuxedo_io. Request numbers follow the kernel's _IOR/_IOW encoding with the
/// magic numbers of tuxedo-drivers (0xEC general, 0xED Clevo read, 0xEE Clevo write); the argument
/// is always a pointer, so the encoded size is 8 bytes. See docs/PROTOCOL.md.
/// </summary>
public sealed unsafe class TuxedoIo : ITuxedoIo
{
    public const string DevicePath = "/dev/tuxedo_io";

    private const uint MagicGeneral = 0xEC, MagicReadClevo = 0xED, MagicWriteClevo = 0xEE;

    internal static uint Ior(uint type, uint nr) => (2u << 30) | (8u << 16) | (type << 8) | nr;
    internal static uint Iow(uint type, uint nr) => (1u << 30) | (8u << 16) | (type << 8) | nr;

    internal static readonly uint ReadModuleVersion = Ior(MagicGeneral, 0x00);
    internal static readonly uint ReadHwCheckClevo = Ior(MagicGeneral, 0x05);
    internal static readonly uint ReadClevoInterface = Ior(MagicReadClevo, 0x00);
    internal static readonly uint ReadClevoFanInfo1 = Ior(MagicReadClevo, 0x10); // +1, +2 for fan 2 and 3
    internal static readonly uint WriteClevoFanSpeed = Iow(MagicWriteClevo, 0x10);
    internal static readonly uint WriteClevoFanAuto = Iow(MagicWriteClevo, 0x11);
    internal static readonly uint WriteClevoPerfProfile = Iow(MagicWriteClevo, 0x15);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int Ioctl(int fd, nuint request, void* arg);

    private readonly SafeFileHandle _handle;
    private readonly object _sync = new();

    private TuxedoIo(SafeFileHandle handle) => _handle = handle;

    /// <summary>Opens the device. Throws with a helpful message when it is missing or not accessible.</summary>
    public static TuxedoIo Open()
    {
        if (!File.Exists(DevicePath))
            throw new FileNotFoundException($"{DevicePath} not found – is tuxedo-drivers installed and the tuxedo_io module loaded?");
        try
        {
            return new TuxedoIo(File.OpenHandle(DevicePath, FileMode.Open, FileAccess.ReadWrite));
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException($"No permission for {DevicePath} – install the udev rule from contrib/linux (see docs/LINUX.md) or run as root.");
        }
    }

    private int Fd => (int)_handle.DangerousGetHandle();

    private void Call(uint request, void* arg, string what)
    {
        int r;
        lock (_sync) r = Ioctl(Fd, request, arg);
        if (r < 0) throw new IOException($"tuxedo_io {what} failed (errno {Marshal.GetLastPInvokeError()})");
    }

    private uint ReadUInt(uint request, string what)
    {
        ulong value = 0; // 8 bytes, the driver writes 4
        Call(request, &value, what);
        return (uint)value;
    }

    private void WriteUInt(uint request, uint value, string what)
    {
        var v = value;
        Call(request, &v, what);
    }

    private string ReadString(uint request, string what)
    {
        var buf = stackalloc byte[256];
        new Span<byte>(buf, 256).Clear();
        Call(request, buf, what);
        var span = new ReadOnlySpan<byte>(buf, 255);
        var end = span.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? span : span[..end]);
    }

    public bool IsClevo() => ReadUInt(ReadHwCheckClevo, "hwcheck") == 1;

    public string InterfaceName() => ReadString(ReadClevoInterface, "interface");

    public string ModuleVersion() => ReadString(ReadModuleVersion, "version");

    public uint ReadFanInfo(int fan) => ReadUInt(ReadClevoFanInfo1 + (uint)Math.Clamp(fan, 0, 2), $"fan info {fan}");

    public void SetFanSpeed(uint packed) => WriteUInt(WriteClevoFanSpeed, packed & 0xFFFFFF, "fan speed");

    public void SetFanAuto(uint fanMask) => WriteUInt(WriteClevoFanAuto, fanMask, "fan auto");

    public void SetPerformanceProfile(uint value) => WriteUInt(WriteClevoPerfProfile, value & 0xFF, "performance profile");

    public void Dispose() => _handle.Dispose();
}
