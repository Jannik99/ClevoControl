using System.Net.Sockets;
using System.Text;
using ClevoControl.Core;
using ClevoControl.Core.Lighting;
using ClevoControl.Core.Services;
using ClevoControl.Core.Settings;
using Xunit;

namespace ClevoControl.Tests;

public class RgbColorTests
{
    [Theory]
    [InlineData("#FF8800", 255, 136, 0)]
    [InlineData("ff8800", 255, 136, 0)]
    [InlineData("#f80", 255, 136, 0)]
    [InlineData("0x0000FF", 0, 0, 255)]
    public void Parses_hex(string text, byte r, byte g, byte b) => Assert.Equal(new RgbColor(r, g, b), RgbColor.Parse(text));

    [Fact]
    public void Rejects_garbage() => Assert.False(RgbColor.TryParse("nope", out _));

    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(12, 200, 90)]
    [InlineData(255, 255, 255)]
    [InlineData(0, 0, 0)]
    public void Hsv_roundtrip(byte r, byte g, byte b)
    {
        var c = new RgbColor(r, g, b);
        var (h, s, v) = c.ToHsv();
        var back = RgbColor.FromHsv(h, s, v);
        Assert.InRange(Math.Abs(back.R - r), 0, 1);
        Assert.InRange(Math.Abs(back.G - g), 0, 1);
        Assert.InRange(Math.Abs(back.B - b), 0, 1);
    }

    [Fact]
    public void Kelvin_is_warm_at_night_and_neutral_at_day()
    {
        var warm = RgbColor.FromKelvin(2200);
        var day = RgbColor.FromKelvin(6500);
        Assert.True(warm.R > warm.B);
        Assert.InRange(day.R - day.B, -15, 15);
    }
}

public class FanCurveTests
{
    private static readonly List<FanCurvePoint> Curve = [new(40, 20), new(60, 40), new(80, 80)];

    [Theory]
    [InlineData(30, 20)]
    [InlineData(40, 20)]
    [InlineData(50, 30)]
    [InlineData(70, 60)]
    [InlineData(95, 80)]
    public void Interpolates(int temp, double expected) => Assert.Equal(expected, FanController.Evaluate(Curve, temp), 3);

    [Theory]
    [InlineData(0, 69, 0)]
    [InlineData(0, 70, 35)]
    [InlineData(10, 80, 70)]
    [InlineData(90, 82, 90)]
    [InlineData(0, 88, 100)]
    [InlineData(20, 99, 100)]
    public void Safety_floor_always_applies(double duty, int temp, double expected) => Assert.Equal(expected, FanController.Safe(duty, temp));
}

public class ScreenTests
{
    [Fact]
    public void Finds_monitors_and_samples_a_color()
    {
        var monitors = ScreenSampler.GetMonitors();
        Assert.NotEmpty(monitors);
        Assert.All(monitors, m => Assert.True(m.Width > 0 && m.Height > 0));
        using var sampler = new ScreenSampler();
        Assert.NotNull(sampler.Sample(monitors[0]));
    }
}

public class SettingsTests
{
    [Fact]
    public void Roundtrips_through_json()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clevocontrol-test-" + Guid.NewGuid());
        try
        {
            var store = new SettingsStore(dir);
            Assert.True(store.IsFirstRun);
            store.Current.Keyboard.Color = new RgbColor(1, 2, 3);
            store.Current.Keyboard.Effect = LightingEffectKind.Music;
            store.Current.Fan.CpuCurve.Add(new FanCurvePoint(99, 99));
            store.SaveNow();
            var again = new SettingsStore(dir);
            Assert.False(again.IsFirstRun);
            Assert.Equal(new RgbColor(1, 2, 3), again.Current.Keyboard.Color);
            Assert.Equal(LightingEffectKind.Music, again.Current.Keyboard.Effect);
            Assert.Contains(again.Current.Fan.CpuCurve, p => p.Temperature == 99);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class OpenRgbServerTests
{
    private static byte[] Packet(uint dev, uint id, byte[] data)
    {
        var p = new byte[16 + data.Length];
        Encoding.ASCII.GetBytes("ORGB").CopyTo(p, 0);
        BitConverter.GetBytes(dev).CopyTo(p, 4);
        BitConverter.GetBytes(id).CopyTo(p, 8);
        BitConverter.GetBytes((uint)data.Length).CopyTo(p, 12);
        data.CopyTo(p, 16);
        return p;
    }

    private static async Task<(uint Id, byte[] Data)> Read(NetworkStream s)
    {
        var h = new byte[16];
        await s.ReadExactlyAsync(h);
        Assert.Equal("ORGB", Encoding.ASCII.GetString(h, 0, 4));
        var data = new byte[BitConverter.ToUInt32(h, 12)];
        await s.ReadExactlyAsync(data);
        return (BitConverter.ToUInt32(h, 8), data);
    }

    [Fact]
    public async Task Speaks_the_openrgb_protocol()
    {
        var received = new TaskCompletionSource<RgbColor>();
        var port = 46000 + Random.Shared.Next(1000);
        using var server = new OpenRgbServer(port, "Test Keyboard", () => new RgbColor(10, 20, 30), c => received.TrySetResult(c));
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var s = client.GetStream();

        await s.WriteAsync(Packet(0, 40, BitConverter.GetBytes(4u)));
        var (id, data) = await Read(s);
        Assert.Equal(40u, id);
        Assert.Equal(1u, BitConverter.ToUInt32(data));

        await s.WriteAsync(Packet(0, 0, []));
        (id, data) = await Read(s);
        Assert.Equal(1u, BitConverter.ToUInt32(data));

        await s.WriteAsync(Packet(0, 1, BitConverter.GetBytes(1u)));
        (id, data) = await Read(s);
        Assert.Equal(1u, id);
        Assert.Equal((uint)data.Length, BitConverter.ToUInt32(data, 0)); // size prefix matches
        Assert.Equal(5, BitConverter.ToInt32(data, 4));                  // keyboard
        Assert.Contains("Test Keyboard", Encoding.ASCII.GetString(data));

        // UPDATELEDS: u32 size, u16 count, u32 color (R | G<<8 | B<<16)
        var payload = new byte[10];
        BitConverter.GetBytes(10u).CopyTo(payload, 0);
        BitConverter.GetBytes((ushort)1).CopyTo(payload, 4);
        BitConverter.GetBytes(0x00336699u).CopyTo(payload, 6);
        await s.WriteAsync(Packet(0, 1050, payload));
        var color = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new RgbColor(0x99, 0x66, 0x33), color);
    }
}
