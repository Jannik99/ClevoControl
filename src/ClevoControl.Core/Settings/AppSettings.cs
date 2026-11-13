using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClevoControl.Core.Settings;

public enum LightingEffectKind
{
    Static,
    Breathing,
    Spectrum,
    Gradient,
    Candle,
    Daylight,
    Ambient,
    Music,
    Temperature,
    Battery,
    /// <summary>Reads a health/mana bar (or any area) from the screen.</summary>
    ScreenRegion,
    /// <summary>Counter-Strike 2 game state integration.</summary>
    GameCs2,
    /// <summary>Flashes on every key press.</summary>
    Reactive,
    CpuLoad,
    /// <summary>Red while any app uses the microphone.</summary>
    OnAir,
    Pomodoro,
    Heartbeat,
    Lava,
    Fire,
    Police,
    Hardware,
    Off,
}

public enum HealthDisplay
{
    /// <summary>Green → yellow → red.</summary>
    Hue,
    /// <summary>Keyboard color, brightness follows the value.</summary>
    Brightness,
    /// <summary>Hue and brightness together.</summary>
    Both,
}

public enum RegionMode
{
    /// <summary>Keyboard shows the fill level of a bar (e.g. health) as green → yellow → red.</summary>
    BarFill,
    /// <summary>Keyboard takes the average color of the area.</summary>
    AreaColor,
}

public sealed class ScreenRegionSettings
{
    public int Monitor { get; set; }
    /// <summary>Relative coordinates (0..1) of the bar within the monitor.</summary>
    public double X { get; set; } = 0.02;
    public double Y { get; set; } = 0.93;
    public double Width { get; set; } = 0.2;
    public double Height { get; set; } = 0.02;
    public RegionMode Mode { get; set; } = RegionMode.BarFill;
    /// <summary>Color of the bar when full (picked automatically when the region is set).</summary>
    public RgbColor BarColor { get; set; } = new(200, 30, 30);
    /// <summary>Color tolerance 0..1 when deciding whether a pixel belongs to the bar.</summary>
    public double Tolerance { get; set; } = 0.35;
    public bool VerticalBar { get; set; }
    public RgbColor FullColor { get; set; } = new(0, 255, 40);
    public RgbColor EmptyColor { get; set; } = new(255, 0, 0);
    /// <summary>Below this fill level (percent) the keyboard pulses.</summary>
    public int WarnBelowPercent { get; set; } = 25;
}

public sealed class GameSettings
{
    public int Cs2Port { get; set; } = 3001;
    public bool Cs2FlashEffect { get; set; } = true;
    public bool Cs2BombEffect { get; set; } = true;
    /// <summary>Local HTTP API on 127.0.0.1 for scripts and mods (POST /color).</summary>
    public bool LocalApiEnabled { get; set; }
    public int LocalApiPort { get; set; } = 3002;
    /// <summary>OpenRGB SDK server so Artemis, Aurora &amp; co. can control the keyboard.</summary>
    public bool OpenRgbServerEnabled { get; set; }
    public int OpenRgbPort { get; set; } = 6742;
    /// <summary>How long external colors (OpenRGB / API) win over the selected effect.</summary>
    public int ExternalHoldSeconds { get; set; } = 5;
}

public enum MusicColorMode
{
    /// <summary>Uses the keyboard color, brightness follows the volume.</summary>
    Pulse,
    /// <summary>Bass = red, mids = green, treble = blue.</summary>
    Spectrum,
    /// <summary>Hue rotates with the beat, brightness follows the volume.</summary>
    HueBeat,
}

public sealed class KeyboardSettings
{
    public bool Enabled { get; set; } = true;
    public LightingEffectKind Effect { get; set; } = LightingEffectKind.Static;
    public RgbColor Color { get; set; } = new(0, 140, 255);
    public RgbColor SecondaryColor { get; set; } = new(255, 0, 140);
    /// <summary>0..100 %.</summary>
    public int Brightness { get; set; } = 100;
    /// <summary>Effect speed multiplier (0.1 .. 5).</summary>
    public double Speed { get; set; } = 1.0;
    public HardwareEffect HardwareEffect { get; set; } = HardwareEffect.Wave;

    // Ambient light
    public int AmbientMonitor { get; set; } = 0;
    public double AmbientSaturationBoost { get; set; } = 1.4;
    /// <summary>0 = instant, 0.95 = very smooth.</summary>
    public double AmbientSmoothing { get; set; } = 0.75;
    public int AmbientFps { get; set; } = 25;

    // Music
    public MusicColorMode MusicMode { get; set; } = MusicColorMode.Spectrum;
    public double MusicSensitivity { get; set; } = 1.0;

    // Temperature indicator
    public int TemperatureCool { get; set; } = 45;
    public int TemperatureHot { get; set; } = 90;

    // Daylight
    public int DaylightDayKelvin { get; set; } = 6500;
    public int DaylightNightKelvin { get; set; } = 2200;
    public int DaylightNightBrightness { get; set; } = 30;

    // White balance correction (gain per channel). Single-zone Clevo LEDs are strongly red/blue heavy.
    public double GainRed { get; set; } = 0.71;
    public double GainGreen { get; set; } = 1.0;
    public double GainBlue { get; set; } = 0.78;

    public ScreenRegionSettings Region { get; set; } = new();

    /// <summary>How health-like values (screen bar, CS2) are shown.</summary>
    public HealthDisplay HealthDisplay { get; set; } = HealthDisplay.Hue;

    // Pomodoro
    public int PomodoroWorkMinutes { get; set; } = 25;
    public int PomodoroBreakMinutes { get; set; } = 5;

    /// <summary>Turn the keyboard light off after this many minutes without input (0 = never).</summary>
    public int IdleTimeoutMinutes { get; set; } = 0;

    public List<RgbColor> Favorites { get; set; } =
    [
        new(255, 255, 255), new(255, 0, 0), new(255, 120, 0), new(255, 220, 0), new(0, 255, 60),
        new(0, 255, 200), new(0, 140, 255), new(40, 0, 255), new(170, 0, 255), new(255, 0, 140),
    ];

    public KeyboardSettings Clone() => JsonSerializer.Deserialize<KeyboardSettings>(JsonSerializer.Serialize(this, SettingsStore.JsonOptions), SettingsStore.JsonOptions)!;
}

public sealed class FanCurvePoint
{
    public int Temperature { get; set; }
    public int Duty { get; set; }

    public FanCurvePoint() { }
    public FanCurvePoint(int temperature, int duty) { Temperature = temperature; Duty = duty; }
}

public sealed class FanSettings
{
    public FanMode Mode { get; set; } = FanMode.Auto;
    public List<FanCurvePoint> CpuCurve { get; set; } = DefaultCurve();
    public List<FanCurvePoint> GpuCurve { get; set; } = DefaultCurve();
    /// <summary>GPU fan also reacts to CPU temperature (shared heat pipes on most Clevos).</summary>
    public bool GpuFollowsCpu { get; set; } = true;
    /// <summary>Maximum duty decrease per second (percent points) to avoid fan pumping.</summary>
    public int RampDownPerSecond { get; set; } = 3;

    /// <summary>Each performance profile remembers its own fan mode and curves.</summary>
    public bool PerProfile { get; set; }
    public Dictionary<PerformanceProfile, FanProfile> Profiles { get; set; } = [];

    public static List<FanCurvePoint> DefaultCurve() =>
    [
        new(40, 20), new(55, 30), new(65, 45), new(75, 65), new(85, 90), new(95, 100),
    ];
}

public sealed class FanProfile
{
    public FanMode Mode { get; set; } = FanMode.Auto;
    public List<FanCurvePoint> CpuCurve { get; set; } = FanSettings.DefaultCurve();
    public List<FanCurvePoint> GpuCurve { get; set; } = FanSettings.DefaultCurve();
}

public enum KeyActionType
{
    /// <summary>Sends a key combination, e.g. "Ctrl+Shift+Esc".</summary>
    Shortcut,
    /// <summary>Types a text.</summary>
    Text,
    /// <summary>Starts a program, file or URL.</summary>
    Launch,
    /// <summary>The key does nothing.</summary>
    Disable,
}

public sealed class KeyMapping
{
    public bool Enabled { get; set; } = true;
    public int Key { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public KeyActionType Action { get; set; } = KeyActionType.Shortcut;
    public string Value { get; set; } = "";

    public KeyMapping Clone() => (KeyMapping)MemberwiseClone();
}

public sealed class KeyRemapSettings
{
    public bool Enabled { get; set; } = true;
    public List<KeyMapping> Mappings { get; set; } = [];
}

public sealed class CoolingSettings
{
    public Services.CoolingMode Mode { get; set; } = Services.CoolingMode.Normal;
    /// <summary>Original Windows values, saved before the first change so "Normal" restores them.</summary>
    public uint? SavedBoostMode { get; set; }
    public int? SavedMaxState { get; set; }
    public bool OverheatWarning { get; set; } = true;
    public int OverheatThreshold { get; set; } = 95;
}

public sealed class PowerSettings
{
    public PerformanceProfile Profile { get; set; } = PerformanceProfile.Entertainment;
    public bool AutoSwitch { get; set; }
    public PerformanceProfile ProfileOnAc { get; set; } = PerformanceProfile.Performance;
    public PerformanceProfile ProfileOnBattery { get; set; } = PerformanceProfile.PowerSaving;
    /// <summary>Also switch the Windows power mode slider together with the Clevo profile.</summary>
    public bool SyncWindowsPowerMode { get; set; } = true;
    /// <summary>Lower the display refresh rate on battery and restore the maximum on AC.</summary>
    public bool LowRefreshOnBattery { get; set; }
    public int BatteryRefreshRate { get; set; } = 60;
}

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    /// <summary>"auto", "de" or "en".</summary>
    public string Language { get; set; } = "auto";
    public bool SceneHotkeys { get; set; }
    public bool RestoreAutoFanOnExit { get; set; } = true;
    /// <summary>Block the Windows key (gaming mode).</summary>
    public bool WinKeyLock { get; set; }
}

public sealed class Scene
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "★";
    public KeyboardSettings? Keyboard { get; set; }
    public FanMode? FanMode { get; set; }
    public PerformanceProfile? Profile { get; set; }
    public WindowsPowerMode? WindowsPowerMode { get; set; }
    public int? Volume { get; set; }

    public static List<Scene> Defaults()
    {
        KeyboardSettings Kb(Action<KeyboardSettings> f) { var k = new KeyboardSettings(); f(k); return k; }
        return
        [
            new Scene
            {
                Name = "Film", Icon = "🎬", Profile = PerformanceProfile.Quiet,
                Keyboard = Kb(k => { k.Effect = LightingEffectKind.Ambient; k.Brightness = 60; }),
            },
            new Scene
            {
                Name = "Gaming", Icon = "🎮", Profile = PerformanceProfile.Performance, FanMode = Core.FanMode.Auto,
                Keyboard = Kb(k => { k.Effect = LightingEffectKind.Temperature; k.Brightness = 100; }),
            },
            new Scene
            {
                Name = "Musik", Icon = "🎵", Profile = PerformanceProfile.Entertainment,
                Keyboard = Kb(k => { k.Effect = LightingEffectKind.Music; k.Brightness = 100; }),
            },
            new Scene
            {
                Name = "Arbeit", Icon = "💼", Profile = PerformanceProfile.Entertainment,
                Keyboard = Kb(k => { k.Effect = LightingEffectKind.Static; k.Color = new RgbColor(255, 255, 255); k.Brightness = 70; }),
            },
            new Scene
            {
                Name = "Nacht", Icon = "🌙", Profile = PerformanceProfile.Quiet,
                Keyboard = Kb(k => { k.Effect = LightingEffectKind.Static; k.Color = new RgbColor(255, 90, 20); k.Brightness = 20; }),
            },
            new Scene
            {
                Name = "Akku sparen", Icon = "🔋", Profile = PerformanceProfile.PowerSaving,
                WindowsPowerMode = Core.WindowsPowerMode.BestEfficiency,
                Keyboard = Kb(k => { k.Effect = LightingEffectKind.Off; }),
            },
        ];
    }
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public KeyboardSettings Keyboard { get; set; } = new();
    public FanSettings Fan { get; set; } = new();
    public PowerSettings Power { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public GameSettings Games { get; set; } = new();
    public CoolingSettings Cooling { get; set; } = new();
    public KeyRemapSettings KeyRemap { get; set; } = new();
    public List<Scene> Scenes { get; set; } = Scene.Defaults();
}

public sealed class SettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClevoControl");

    private readonly string _path;
    private readonly object _sync = new();
    private Timer? _debounce;

    public SettingsStore(string? directory = null)
    {
        var dir = directory ?? DefaultDirectory;
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        IsFirstRun = !File.Exists(_path);
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public string FilePath => _path;

    /// <summary>True when no settings file existed yet (first start of the app).</summary>
    public bool IsFirstRun { get; }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            try { File.Copy(_path, _path + ".broken", true); } catch { }
        }
        return new AppSettings();
    }

    /// <summary>Saves after a short delay so rapid slider changes do not hammer the disk.</summary>
    public void SaveSoon()
    {
        lock (_sync)
        {
            _debounce?.Dispose();
            _debounce = new Timer(_ => SaveNow(), null, 800, Timeout.Infinite);
        }
    }

    public void SaveNow()
    {
        lock (_sync)
        {
            try
            {
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
                File.Move(tmp, _path, true);
            }
            catch { /* ignore – next save will retry */ }
        }
    }
}
