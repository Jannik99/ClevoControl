using System.Net;
using System.Net.Sockets;
using ClevoControl.Core;
using ClevoControl.Core.Hardware;
using ClevoControl.Core.Platform;
using ClevoControl.Core.Services;
using ClevoControl.Core.Settings;
using Xunit;

namespace ClevoControl.Tests;

public class LocalApiSecurityTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public void Rejects_browser_and_foreign_host_headers()
    {
        static Dictionary<string, string> H(params (string, string)[] h) => h.ToDictionary(x => x.Item1, x => x.Item2, StringComparer.OrdinalIgnoreCase);
        Assert.Null(MiniHttpServer.RejectReason(H(("Host", "127.0.0.1:3002")), 3002));
        Assert.Null(MiniHttpServer.RejectReason(H(("Host", "localhost:3002"), ("Content-Type", "application/x-www-form-urlencoded")), 3002));
        Assert.Null(MiniHttpServer.RejectReason(H(), 3002));
        Assert.NotNull(MiniHttpServer.RejectReason(H(("Host", "127.0.0.1:3002"), ("Origin", "https://evil.example")), 3002));
        Assert.NotNull(MiniHttpServer.RejectReason(H(("Host", "127.0.0.1:3002"), ("Origin", "null")), 3002));
        Assert.NotNull(MiniHttpServer.RejectReason(H(("Host", "127.0.0.1:3002"), ("Sec-Fetch-Site", "cross-site")), 3002));
        Assert.NotNull(MiniHttpServer.RejectReason(H(("Host", "evil.example:3002")), 3002)); // DNS rebinding
    }

    [Fact]
    public async Task Server_refuses_cross_site_posts_but_serves_scripts()
    {
        var port = FreePort();
        var calls = 0;
        using var server = new MiniHttpServer(port, (_, _, _) => { Interlocked.Increment(ref calls); return (200, "{\"ok\":true}"); }, rejectBrowsers: true);
        using var http = new HttpClient();
        var url = $"http://127.0.0.1:{port}/color";

        // Like curl -d: form content type, no Origin.
        var plain = await http.PostAsync(url, new StringContent("{\"color\":\"#ff0000\"}", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"));
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);

        var fromPage = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent("{\"color\":\"#ff0000\"}") };
        fromPage.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(fromPage)).StatusCode);

        Assert.Equal(1, calls);
    }
}

public class PowerAutoSwitchTests
{
    private static (PowerService Power, PowerSettings Settings, Func<bool> Ac, Action<bool> SetAc) Create()
    {
        var onAc = true;
        var power = new PowerService(new SimulatedHardware("test")) { OnAcSource = () => onAc };
        var s = new PowerSettings
        {
            Profile = PerformanceProfile.Entertainment,
            AutoSwitch = true,
            ProfileOnAc = PerformanceProfile.Performance,
            ProfileOnBattery = PerformanceProfile.PowerSaving,
            SyncWindowsPowerMode = false,
            LowRefreshOnBattery = false,
        };
        return (power, s, () => onAc, v => onAc = v);
    }

    [Fact]
    public void Auto_switch_keeps_the_settings_profile_in_sync()
    {
        var (power, s, _, setAc) = Create();
        power.CheckPowerSource(s, force: true); // start
        Assert.Equal(PerformanceProfile.Performance, s.Profile);
        Assert.Equal(PerformanceProfile.Performance, power.ActiveProfile);

        setAc(false);
        power.CheckPowerSource(s);
        Assert.Equal(PerformanceProfile.PowerSaving, s.Profile);
        Assert.Equal(PerformanceProfile.PowerSaving, power.ActiveProfile);
    }

    [Fact]
    public void Explicit_profile_choice_is_not_overridden_until_the_source_changes()
    {
        var (power, s, _, setAc) = Create();
        power.CheckPowerSource(s, force: true);

        s.Profile = PerformanceProfile.Quiet; // user, scene or automation
        power.Apply(s);
        Assert.Equal(PerformanceProfile.Quiet, power.ActiveProfile);
        Assert.Equal(PerformanceProfile.Quiet, s.Profile);

        power.CheckPowerSource(s); // timer, same source
        Assert.Equal(PerformanceProfile.Quiet, power.ActiveProfile);

        setAc(false);
        power.CheckPowerSource(s);
        Assert.Equal(PerformanceProfile.PowerSaving, power.ActiveProfile);
        Assert.Equal(PerformanceProfile.PowerSaving, s.Profile);
    }

    [Fact]
    public void Changed_rules_apply_right_away()
    {
        var (power, s, _, setAc) = Create();
        s.AutoSwitch = false;
        power.CheckPowerSource(s, force: true);
        s.Profile = PerformanceProfile.Quiet;
        power.Apply(s);
        Assert.Equal(PerformanceProfile.Quiet, power.ActiveProfile);

        s.AutoSwitch = true; // switched on: the rule for the current source applies now
        power.Apply(s);
        Assert.Equal(PerformanceProfile.Performance, power.ActiveProfile);
        Assert.Equal(PerformanceProfile.Performance, s.Profile);

        s.ProfileOnAc = PerformanceProfile.Entertainment;
        power.Apply(s);
        Assert.Equal(PerformanceProfile.Entertainment, s.Profile);
    }
}

public class SettingsStoreLifetimeTests
{
    [Fact]
    public void Disposed_store_never_writes_a_pending_save()
    {
        var dir = Directory.CreateTempSubdirectory("clevo-store-").FullName;
        try
        {
            var store = new SettingsStore(dir);
            store.SaveSoon();
            store.Dispose();
            Thread.Sleep(1200);
            Assert.False(File.Exists(Path.Combine(dir, "settings.json")));
            store.SaveSoon(); // ignored after Dispose
            Thread.Sleep(1000);
            Assert.False(File.Exists(Path.Combine(dir, "settings.json")));
            Assert.True(store.SaveNow()); // an explicit save still works
            Assert.True(File.Exists(Path.Combine(dir, "settings.json")));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}

[Collection("LinuxSysRoot")] // LinuxSys.Root is global state
public sealed class CpuLimitTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("clevo-cpu-").FullName;

    public CpuLimitTests() => LinuxSys.Root = _root;

    public void Dispose()
    {
        LinuxSys.Root = "/";
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Limit_without_max_perf_pct_is_reported_as_not_applied()
    {
        // AMD / acpi-cpufreq: only the generic boost switch exists.
        var boost = Path.Combine(_root, "sys/devices/system/cpu/cpufreq/boost");
        Directory.CreateDirectory(Path.GetDirectoryName(boost)!);
        File.WriteAllText(boost, "1");

        Assert.True(LinuxPower.ApplyCpuLimits(false, 100));   // "Cool": turbo off works
        Assert.Equal("0", File.ReadAllText(boost));
        Assert.False(LinuxPower.ApplyCpuLimits(false, 80));   // "Very cool": the 80 % limit cannot be applied
        Assert.True(LinuxPower.ApplyCpuLimits(true, 100));    // "Normal"
    }
}
