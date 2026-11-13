using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ClevoControl.Core.Services;

/// <summary>
/// Implements the server side of the OpenRGB SDK network protocol (protocol version 1) and
/// exposes the keyboard as a single-LED device. Any OpenRGB SDK client – Artemis, Aurora,
/// Home Assistant, custom scripts – can then drive the keyboard color.
/// </summary>
public sealed class OpenRgbServer : IDisposable
{
    private const uint MaxProtocol = 1;
    private const uint RequestControllerCount = 0, RequestControllerData = 1, RequestProtocolVersion = 40,
        SetClientName = 50, UpdateLeds = 1050, UpdateZoneLeds = 1051, UpdateSingleLed = 1052, SetCustomMode = 1100,
        UpdateMode = 1101, SaveMode = 1102;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Action<RgbColor> _onColor;
    private readonly Func<RgbColor> _currentColor;
    private readonly string _deviceName;

    public OpenRgbServer(int port, string deviceName, Func<RgbColor> currentColor, Action<RgbColor> onColor)
    {
        Port = port;
        _deviceName = deviceName;
        _currentColor = currentColor;
        _onColor = onColor;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        _ = AcceptLoop();
    }

    public int Port { get; }

    public int ClientCount;

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { return; }
            _ = Task.Run(() => Serve(c));
        }
    }

    private async Task Serve(TcpClient client)
    {
        Interlocked.Increment(ref ClientCount);
        uint protocol = 0;
        try
        {
            using var _ = client;
            var s = client.GetStream();
            var header = new byte[16];
            while (!_cts.IsCancellationRequested)
            {
                if (!await ReadExact(s, header)) return;
                if (header[0] != 'O' || header[1] != 'R' || header[2] != 'G' || header[3] != 'B') return;
                var dev = BitConverter.ToUInt32(header, 4);
                var id = BitConverter.ToUInt32(header, 8);
                var size = BitConverter.ToUInt32(header, 12);
                if (size > 1 << 20) return;
                var data = new byte[size];
                if (size > 0 && !await ReadExact(s, data)) return;

                switch (id)
                {
                    case RequestProtocolVersion:
                    {
                        var clientVersion = size >= 4 ? BitConverter.ToUInt32(data, 0) : 0;
                        protocol = Math.Min(clientVersion, MaxProtocol);
                        await Send(s, 0, id, BitConverter.GetBytes(MaxProtocol));
                        break;
                    }
                    case RequestControllerCount:
                        await Send(s, 0, id, BitConverter.GetBytes(1u));
                        break;
                    case RequestControllerData:
                    {
                        var v = size >= 4 ? Math.Min(BitConverter.ToUInt32(data, 0), MaxProtocol) : protocol;
                        await Send(s, dev, id, ControllerData(v));
                        break;
                    }
                    case UpdateLeds:
                        // u32 size, u16 count, colors
                        if (size >= 10 && BitConverter.ToUInt16(data, 4) > 0) _onColor(ToColor(BitConverter.ToUInt32(data, 6)));
                        break;
                    case UpdateZoneLeds:
                        // u32 size, u32 zone, u16 count, colors
                        if (size >= 14 && BitConverter.ToUInt16(data, 8) > 0) _onColor(ToColor(BitConverter.ToUInt32(data, 10)));
                        break;
                    case UpdateSingleLed:
                        if (size >= 8) _onColor(ToColor(BitConverter.ToUInt32(data, 4)));
                        break;
                    case SetClientName:
                    case SetCustomMode:
                    case UpdateMode:
                    case SaveMode:
                    default:
                        break; // no response required
                }
            }
        }
        catch
        {
            // client went away
        }
        finally
        {
            Interlocked.Decrement(ref ClientCount);
        }
    }

    private static RgbColor ToColor(uint c) => new((byte)c, (byte)(c >> 8), (byte)(c >> 16));

    private static uint FromColor(RgbColor c) => c.R | ((uint)c.G << 8) | ((uint)c.B << 16);

    private byte[] ControllerData(uint protocol)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(0u);              // placeholder: data size
        w.Write(5);               // device type: keyboard
        Str(w, _deviceName);
        if (protocol >= 1) Str(w, "Clevo");
        Str(w, "Clevo laptop keyboard backlight via ClevoControl");
        Str(w, "1.0");
        Str(w, "");
        Str(w, "ClevoControl (InsydeDCHU)");

        w.Write((ushort)1);       // one mode
        w.Write(0);               // active mode
        Str(w, "Direct");
        w.Write(0);               // value
        w.Write(1u << 5);         // MODE_FLAG_HAS_PER_LED_COLOR
        w.Write(0u); w.Write(0u); // speed min/max
        w.Write(0u); w.Write(0u); // colors min/max
        w.Write(0u);              // speed
        w.Write(0u);              // direction
        w.Write(1u);              // MODE_COLORS_PER_LED
        w.Write((ushort)0);       // mode colors

        w.Write((ushort)1);       // one zone
        Str(w, "Keyboard");
        w.Write(0);               // ZONE_TYPE_SINGLE
        w.Write(1u); w.Write(1u); w.Write(1u); // leds min/max/count
        w.Write((ushort)0);       // no matrix

        w.Write((ushort)1);       // one LED
        Str(w, "Keyboard");
        w.Write(0u);

        w.Write((ushort)1);       // colors
        w.Write(FromColor(_currentColor()));

        w.Flush();
        var bytes = ms.ToArray();
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), (uint)bytes.Length);
        return bytes;
    }

    private static void Str(BinaryWriter w, string s)
    {
        var b = Encoding.ASCII.GetBytes(s + "\0");
        w.Write((ushort)b.Length);
        w.Write(b);
    }

    private static async Task Send(NetworkStream s, uint dev, uint id, byte[] payload)
    {
        var buf = new byte[16 + payload.Length];
        buf[0] = (byte)'O'; buf[1] = (byte)'R'; buf[2] = (byte)'G'; buf[3] = (byte)'B';
        BitConverter.TryWriteBytes(buf.AsSpan(4), dev);
        BitConverter.TryWriteBytes(buf.AsSpan(8), id);
        BitConverter.TryWriteBytes(buf.AsSpan(12), (uint)payload.Length);
        payload.CopyTo(buf, 16);
        await s.WriteAsync(buf);
    }

    private static async Task<bool> ReadExact(NetworkStream s, byte[] buf)
    {
        var read = 0;
        while (read < buf.Length)
        {
            var n = await s.ReadAsync(buf.AsMemory(read));
            if (n == 0) return false;
            read += n;
        }
        return true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}
