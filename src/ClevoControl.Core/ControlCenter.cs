using System.Text.Json;
using ClevoControl.Core.Games;
using ClevoControl.Core.Hardware;
using ClevoControl.Core.Lighting;
using ClevoControl.Core.Services;
using ClevoControl.Core.Settings;

namespace ClevoControl.Core;

/// <summary>Wires all services together. One instance per app.</summary>
public sealed class ControlCenter : IDisposable
{
    private OpenRgbServer? _openRgb;
    private MiniHttpServer? _api;
    private readonly Timer _powerTimer;

    public ControlCenter(IClevoHardware hardware, SettingsStore store)
    {
        Hardware = hardware;
        Store = store;
        Sensors = new SensorService(hardware);
        Cs2 = new Cs2Integration();
        Lighting = new LightingEngine(hardware, Settings.Keyboard, () => Sensors.Latest, Cs2);
        Fans = new FanController(hardware, Sensors, Settings.Fan);
        Power = new PowerService(hardware);
        Sound = new SoundService();
        Hook = new KeyboardHook();
        Hook.KeyPressed += Lighting.NotifyKeyPress;
        Hook.BlockWindowsKey = Settings.General.WinKeyLock;

        if (store.IsFirstRun)
        {
            // Adopt whatever profile the laptop currently uses instead of overriding it.
            if (hardware.ReadStoredPerformanceProfile() is { } current) Settings.Power.Profile = current;
            store.SaveNow();
        }
        else
        {
            Power.SetProfile(Settings.Power.Profile, false);
        }
        Power.CheckPowerSource(Settings.Power, force: Settings.Power.AutoSwitch);
        _powerTimer = new Timer(_ => { try { Power.CheckPowerSource(Settings.Power); } catch { } }, null, 5000, 5000);

        ApplyIntegrations();
    }

    public IClevoHardware Hardware { get; }
    public SettingsStore Store { get; }
    public AppSettings Settings => Store.Current;
    public SensorService Sensors { get; }
    public LightingEngine Lighting { get; }
    public FanController Fans { get; }
    public PowerService Power { get; }
    public SoundService Sound { get; }
    public KeyboardHook Hook { get; }
    public Cs2Integration Cs2 { get; }

    public int OpenRgbClients => _openRgb?.ClientCount ?? 0;
    public string? IntegrationError { get; private set; }

    public void ApplyKeyboard()
    {
        Lighting.Apply(Settings.Keyboard);
        ApplyIntegrations();
        Store.SaveSoon();
    }

    public void ApplyFans()
    {
        Fans.Apply(Settings.Fan);
        Store.SaveSoon();
    }

    public void ApplyPower()
    {
        Power.SetProfile(Settings.Power.Profile, Settings.Power.SyncWindowsPowerMode);
        Power.CheckPowerSource(Settings.Power, force: true);
        Store.SaveSoon();
    }

    public void ApplyGeneral()
    {
        Hook.BlockWindowsKey = Settings.General.WinKeyLock;
        Store.SaveSoon();
    }

    public void ApplyScene(Scene scene)
    {
        if (scene.Keyboard is not null)
        {
            // Keep the user's white balance and favorites when applying a scene.
            var kb = scene.Keyboard.Clone();
            kb.GainRed = Settings.Keyboard.GainRed;
            kb.GainGreen = Settings.Keyboard.GainGreen;
            kb.GainBlue = Settings.Keyboard.GainBlue;
            kb.Favorites = Settings.Keyboard.Favorites;
            kb.Region = Settings.Keyboard.Region;
            Settings.Keyboard = kb;
            ApplyKeyboard();
        }
        if (scene.FanMode is { } fm)
        {
            Settings.Fan.Mode = fm;
            ApplyFans();
        }
        if (scene.Profile is { } p)
        {
            Settings.Power.Profile = p;
            ApplyPower();
        }
        if (scene.WindowsPowerMode is { } wpm) PowerService.SetWindowsPowerMode(wpm);
        if (scene.Volume is { } v) Sound.Volume = v;
        Store.SaveSoon();
    }

    public Scene CaptureScene(string name, string icon) => new()
    {
        Name = name,
        Icon = icon,
        Keyboard = Settings.Keyboard.Clone(),
        FanMode = Settings.Fan.Mode,
        Profile = Settings.Power.Profile,
        Volume = Sound.Volume,
    };

    /// <summary>Starts/stops CS2 listener, OpenRGB server and local API according to the settings.</summary>
    public void ApplyIntegrations()
    {
        var g = Settings.Games;
        IntegrationError = null;
        var hold = TimeSpan.FromSeconds(Math.Max(1, g.ExternalHoldSeconds));

        if (Settings.Keyboard.Effect == LightingEffectKind.GameCs2) Cs2.Start(g.Cs2Port);
        else Cs2.Stop();
        if (Cs2.Error is not null) IntegrationError = $"CS2 port {g.Cs2Port}: {Cs2.Error}";

        if (g.OpenRgbServerEnabled && _openRgb?.Port != g.OpenRgbPort)
        {
            _openRgb?.Dispose();
            _openRgb = null;
            try
            {
                _openRgb = new OpenRgbServer(g.OpenRgbPort, "Clevo Keyboard", () => Lighting.CurrentColor, c => Lighting.Override(c, hold));
            }
            catch (Exception ex)
            {
                IntegrationError = $"OpenRGB port {g.OpenRgbPort}: {ex.Message}";
            }
        }
        else if (!g.OpenRgbServerEnabled && _openRgb is not null)
        {
            _openRgb.Dispose();
            _openRgb = null;
        }

        if (g.LocalApiEnabled && _api?.Port != g.LocalApiPort)
        {
            _api?.Dispose();
            _api = null;
            try { _api = new MiniHttpServer(g.LocalApiPort, HandleApi); }
            catch (Exception ex) { IntegrationError = $"API port {g.LocalApiPort}: {ex.Message}"; }
        }
        else if (!g.LocalApiEnabled && _api is not null)
        {
            _api.Dispose();
            _api = null;
        }
    }

    /// <summary>
    /// Local API (127.0.0.1 only):
    /// GET /status, POST /color {"color":"#ff0000","seconds":5}, POST /scene {"name":"Film"}, POST /effect {"effect":"Spectrum"}.
    /// </summary>
    private (int, string) HandleApi(string method, string path, string body)
    {
        try
        {
            var p = path.Split('?')[0].TrimEnd('/').ToLowerInvariant();
            if (method == "GET" && p is "" or "/status")
            {
                var s = Sensors.Latest;
                return (200, JsonSerializer.Serialize(new
                {
                    color = Lighting.CurrentColor.ToHex(),
                    effect = Settings.Keyboard.Effect.ToString(),
                    profile = Settings.Power.Profile.ToString(),
                    fanMode = Settings.Fan.Mode.ToString(),
                    cpu = s.Cpu,
                    gpu = s.Gpu,
                }));
            }
            if (method != "POST") return (404, "{\"error\":\"not found\"}");
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var r = doc.RootElement;
            switch (p)
            {
                case "/color":
                    var c = RgbColor.Parse(r.GetProperty("color").GetString()!);
                    var secs = r.TryGetProperty("seconds", out var sec) ? sec.GetDouble() : Settings.Games.ExternalHoldSeconds;
                    Lighting.Override(c, TimeSpan.FromSeconds(Math.Clamp(secs, 0.1, 3600)));
                    return (200, "{\"ok\":true}");
                case "/scene":
                    var name = r.GetProperty("name").GetString();
                    var scene = Settings.Scenes.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (scene is null) return (404, "{\"error\":\"scene not found\"}");
                    ApplyScene(scene);
                    return (200, "{\"ok\":true}");
                case "/effect":
                    Settings.Keyboard.Effect = Enum.Parse<LightingEffectKind>(r.GetProperty("effect").GetString()!, true);
                    ApplyKeyboard();
                    return (200, "{\"ok\":true}");
                case "/clear":
                    Lighting.ClearOverride();
                    return (200, "{\"ok\":true}");
            }
            return (404, "{\"error\":\"not found\"}");
        }
        catch (Exception ex)
        {
            return (400, JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    public void OnSuspend() => Fans.Suspend();

    public void OnResume()
    {
        Fans.Resume();
        Lighting.Invalidate();
        try { Power.SetProfile(Settings.Power.Profile, false); } catch { }
    }

    public void Dispose()
    {
        _powerTimer.Dispose();
        Store.SaveNow();
        Hook.Dispose();
        _openRgb?.Dispose();
        _api?.Dispose();
        Cs2.Dispose();
        Lighting.Dispose();
        if (Settings.General.RestoreAutoFanOnExit || Settings.Fan.Mode == FanMode.Custom) Fans.Dispose();
        Sensors.Dispose();
        Sound.Dispose();
    }
}
