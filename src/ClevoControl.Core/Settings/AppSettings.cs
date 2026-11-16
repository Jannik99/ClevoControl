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
    /// <summary>Every key in its own color (<see cref="KeyboardSettings.PerKeyColors"/>); only offered on per-key keyboards.</summary>
    PerKey,
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

    /// <summary>
    /// Colors of the "per key" effect by key id (PC scan code, see <see cref="Platform.KeyIds"/>), so they do not depend on the
    /// keyboard layout. Keys without an entry stay dark.
    /// </summary>
    public Dictionary<int, RgbColor> PerKeyColors
    {
        get => _perKeyColors;
        // "PerKeyColors": null in a hand-edited settings.json (or a scene) becomes empty instead of crashing later.
        set => _perKeyColors = value ?? [];
    }

    private Dictionary<int, RgbColor> _perKeyColors = [];

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

    /// <summary>
    /// Internal: true while ClevoControl has overridden the firmware fan control (max or custom duty).
    /// Persisted so that after a crash the next start knows it must hand control back.
    /// </summary>
    public bool OverrideActive { get; set; }

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
    /// <summary>Show the number pad on the virtual keyboard (17" models).</summary>
    public bool ShowNumpad { get; set; }
    /// <summary>Layout of the keyboard picture: "auto" (follow the system) or a layout id such as "de", "us", "fr".</summary>
    public string KeyboardLayout { get; set; } = "auto";
}

/// <summary>Own rule: a process gets a category or directly a scene.</summary>
public sealed class AppRule
{
    public string ProcessName { get; set; } = "";
    public string? DisplayName { get; set; }
    public Automation.AppCategory Category { get; set; } = Automation.AppCategory.Game;
    /// <summary>Optional: this scene instead of the category's scene.</summary>
    public string? SceneName { get; set; }
}

public sealed class AutoProfileSettings
{
    public bool Enabled { get; set; }
    /// <summary>Restore the previous state when the app is left.</summary>
    public bool RestoreOnLeave { get; set; } = true;
    /// <summary>Treat any exclusive-fullscreen app as a game.</summary>
    public bool DetectFullscreenGames { get; set; } = true;
    public Dictionary<Automation.AppCategory, string> CategoryScenes { get; set; } = DefaultCategoryScenes();
    public List<AppRule> CustomRules { get; set; } = [];

    /// <summary>Default category → scene mapping, using the default scene names in the given language (null = system language).</summary>
    public static Dictionary<Automation.AppCategory, string> DefaultCategoryScenes(bool? german = null)
    {
        var de = german ?? Language.IsGerman();
        return new()
        {
            [Automation.AppCategory.Game] = "Gaming",
            [Automation.AppCategory.Video] = de ? "Film" : "Movie",
            [Automation.AppCategory.Music] = de ? "Musik" : "Music",
        };
    }

    /// <summary>Where a scene is used: by a category mapping or by an own app rule.</summary>
    public sealed record SceneUsage(Automation.AppCategory? Category, AppRule? Rule);

    /// <summary>References removed by <see cref="RemoveSceneReferences"/>, for undo.</summary>
    public sealed record RemovedReferences(string SceneName, IReadOnlyList<Automation.AppCategory> Categories, IReadOnlyList<AppRule> Rules)
    {
        public bool IsEmpty => Categories.Count == 0 && Rules.Count == 0;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Category mappings and own rules that refer to the scene <paramref name="sceneName"/>.</summary>
    public List<SceneUsage> UsagesOf(string sceneName)
    {
        var list = new List<SceneUsage>();
        foreach (var (cat, name) in CategoryScenes.OrderBy(x => x.Key))
            if (Same(name, sceneName)) list.Add(new SceneUsage(cat, null));
        foreach (var r in CustomRules)
            if (Same(r.SceneName, sceneName)) list.Add(new SceneUsage(null, r));
        return list;
    }

    /// <summary>Points every reference to <paramref name="oldName"/> at <paramref name="newName"/>. Returns the number changed.</summary>
    public int RenameSceneReferences(string oldName, string newName)
    {
        var n = 0;
        foreach (var cat in CategoryScenes.Where(x => Same(x.Value, oldName)).Select(x => x.Key).ToList())
        {
            CategoryScenes[cat] = newName;
            n++;
        }
        foreach (var r in CustomRules.Where(r => Same(r.SceneName, oldName)))
        {
            r.SceneName = newName;
            n++;
        }
        return n;
    }

    /// <summary>
    /// Removes every reference to a deleted scene: the category does nothing any more, an own rule falls back
    /// to its category's scene. Returns what was removed so it can be restored with <see cref="RestoreSceneReferences"/>.
    /// </summary>
    public RemovedReferences RemoveSceneReferences(string sceneName)
    {
        var cats = CategoryScenes.Where(x => Same(x.Value, sceneName)).Select(x => x.Key).ToList();
        foreach (var c in cats) CategoryScenes.Remove(c);
        var rules = CustomRules.Where(r => Same(r.SceneName, sceneName)).ToList();
        foreach (var r in rules) r.SceneName = null;
        return new RemovedReferences(sceneName, cats, rules);
    }

    /// <summary>Undo for <see cref="RemoveSceneReferences"/>.</summary>
    public void RestoreSceneReferences(RemovedReferences removed)
    {
        foreach (var c in removed.Categories) CategoryScenes[c] = removed.SceneName;
        foreach (var r in removed.Rules)
            if (CustomRules.Contains(r)) r.SceneName = removed.SceneName;
    }

    /// <summary>Removes references to scenes that no longer exist (e.g. after restoring the default scenes). Returns the number removed.</summary>
    public int PruneSceneReferences(IEnumerable<string> existing)
    {
        var names = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var n = 0;
        foreach (var cat in CategoryScenes.Where(x => !names.Contains(x.Value)).Select(x => x.Key).ToList())
        {
            CategoryScenes.Remove(cat);
            n++;
        }
        foreach (var r in CustomRules.Where(r => r.SceneName is { Length: > 0 } s && !names.Contains(s)))
        {
            r.SceneName = null;
            n++;
        }
        return n;
    }
}

/// <summary>
/// UI language without the apps' Loc class: "de", "en" or "auto" (follows the system UI language),
/// the same rule the apps use.
/// </summary>
public static class Language
{
    public static bool IsGerman(string? setting = "auto") => setting switch
    {
        "de" => true,
        "en" => false,
        _ => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de",
    };
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

    /// <summary>The first-run setup card on the home page was dismissed.</summary>
    public bool FirstRunDone { get; set; }
    /// <summary>The "keeps running in the tray" notification was shown once.</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>Quick settings tiles shown on the home page (ids, in order).</summary>
    public List<string> QuickTiles { get; set; } = ["kblight", "profile", "fanmax", "throttle", "ambient", "winkey", "mute", "micmute"];
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

    /// <summary>Which parts this scene sets.</summary>
    [JsonIgnore]
    public SceneParts Parts =>
        (Keyboard is not null ? SceneParts.Lighting : 0) |
        (Profile is not null ? SceneParts.Profile : 0) |
        (FanMode is not null ? SceneParts.Fans : 0) |
        (Volume is not null ? SceneParts.Volume : 0);

    public Scene Clone() => new()
    {
        Name = Name, Icon = Icon, Keyboard = Keyboard?.Clone(), FanMode = FanMode, Profile = Profile,
        WindowsPowerMode = WindowsPowerMode, Volume = Volume,
    };

    /// <summary>
    /// Builds a scene that contains only <paramref name="parts"/>. Values come from <paramref name="current"/>
    /// (a full capture of the current state); when editing (<paramref name="previous"/> set) and
    /// <paramref name="recapture"/> is false, parts the scene already had keep their stored values.
    /// </summary>
    public static Scene Compose(string name, string icon, SceneParts parts, Scene current, Scene? previous = null, bool recapture = true)
    {
        bool Keep(SceneParts p) => !recapture && previous is not null && previous.Parts.HasFlag(p);
        var s = new Scene { Name = name, Icon = icon };
        if (parts.HasFlag(SceneParts.Lighting))
            s.Keyboard = (Keep(SceneParts.Lighting) ? previous!.Keyboard : current.Keyboard)?.Clone();
        if (parts.HasFlag(SceneParts.Profile))
        {
            s.Profile = Keep(SceneParts.Profile) ? previous!.Profile : current.Profile;
            // The OS power mode belongs to the profile part; a fresh capture follows the profile sync instead.
            if (Keep(SceneParts.Profile)) s.WindowsPowerMode = previous!.WindowsPowerMode;
        }
        if (parts.HasFlag(SceneParts.Fans)) s.FanMode = Keep(SceneParts.Fans) ? previous!.FanMode : current.FanMode;
        if (parts.HasFlag(SceneParts.Volume)) s.Volume = Keep(SceneParts.Volume) ? previous!.Volume : current.Volume;
        return s;
    }

    /// <summary>Why <paramref name="name"/> cannot be used for a scene (null = fine). <paramref name="except"/> is the scene being edited.</summary>
    public static SceneNameProblem? CheckName(IEnumerable<Scene> scenes, string? name, Scene? except = null)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return SceneNameProblem.Empty;
        if (scenes.Any(s => !ReferenceEquals(s, except) && string.Equals(s.Name.Trim(), n, StringComparison.OrdinalIgnoreCase)))
            return SceneNameProblem.Duplicate;
        return null;
    }

    /// <summary>The default scenes, named in German or English (null = system language).</summary>
    public static List<Scene> Defaults(bool? german = null)
    {
        var de = german ?? Language.IsGerman();
        KeyboardSettings Kb(Action<KeyboardSettings> f) { var k = new KeyboardSettings(); f(k); return k; }
        return
        [
            new Scene
            {
                Name = de ? "Film" : "Movie", Icon = "🎬", Profile = PerformanceProfile.Quiet,
                Keyboard = Kb(k => { k.Effect = LightingEffectKind.Ambient; k.Brightness = 60; }),
            },
            new Scene
            {
                Name = "Gaming", Icon = "🎮", Profile = PerformanceProfile.Performance, FanMode = Core.FanMode.Auto,
                Keyboard = Kb(k => { k.Effect = LightingEffectKind.Temperature; k.Brightness = 100; }),
            },
            new Scene
            {
                Name = de ? "Musik" : "Music", Icon = "🎵", Profile = PerformanceProfile.Entertainment,
                Keyboard = Kb(k => { k.Effect = LightingEffectKind.Music; k.Brightness = 100; }),
            },
            new Scene
            {
                Name = de ? "Arbeit" : "Work", Icon = "💼", Profile = PerformanceProfile.Entertainment,
                Keyboard = Kb(k => { k.Effect = LightingEffectKind.Static; k.Color = new RgbColor(255, 255, 255); k.Brightness = 70; }),
            },
            new Scene
            {
                Name = de ? "Nacht" : "Night", Icon = "🌙", Profile = PerformanceProfile.Quiet,
                Keyboard = Kb(k => { k.Effect = LightingEffectKind.Static; k.Color = new RgbColor(255, 90, 20); k.Brightness = 20; }),
            },
            new Scene
            {
                Name = de ? "Akku sparen" : "Battery saver", Icon = "🔋", Profile = PerformanceProfile.PowerSaving,
                WindowsPowerMode = Core.WindowsPowerMode.BestEfficiency,
                Keyboard = Kb(k => { k.Effect = LightingEffectKind.Off; }),
            },
        ];
    }
}

/// <summary>Parts a scene can contain.</summary>
[Flags]
public enum SceneParts
{
    None = 0,
    Lighting = 1,
    Profile = 2,
    Fans = 4,
    Volume = 8,
    All = Lighting | Profile | Fans | Volume,
}

public enum SceneNameProblem
{
    Empty,
    Duplicate,
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
    public AutoProfileSettings AutoProfiles { get; set; } = new();
    public List<Scene> Scenes { get; set; } = Scene.Defaults();
}

public sealed class SettingsStore : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>~/.config/ClevoControl or %APPDATA%ClevoControl; CLEVOCONTROL_CONFIG_DIR overrides it (tests, screenshots, portable use).</summary>
    public static string DefaultDirectory =>
        Environment.GetEnvironmentVariable("CLEVOCONTROL_CONFIG_DIR") is { Length: > 0 } custom ? custom :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClevoControl");

    private readonly string _path;
    private readonly object _sync = new();
    private Timer? _debounce;
    private volatile bool _disposed;
    private int _retries;
    private const int MaxRetries = 5;

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

    /// <summary>
    /// Raised (on the caller's thread) whenever a page changed a setting and asked for a save. Lets services react to
    /// settings that pages change directly (e.g. global hotkeys on/off) without a restart.
    /// </summary>
    public event Action? SaveRequested;

    /// <summary>Saves after a short delay so rapid slider changes do not hammer the disk. Does nothing after <see cref="Dispose"/>.</summary>
    public void SaveSoon()
    {
        if (_disposed) return;
        try { SaveRequested?.Invoke(); } catch (Exception ex) { Log.Warn("SaveRequested handler: " + ex.Message); }
        Schedule(800);
    }

    private void Schedule(int delayMs)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _debounce?.Dispose();
            _debounce = new Timer(_ => SaveFromTimer(), null, delayMs, Timeout.Infinite);
        }
    }

    private void SaveFromTimer()
    {
        lock (_sync)
        {
            if (_disposed) return;
            SaveNow();
        }
    }

    /// <summary>
    /// Writes the settings right away (cancels a pending delayed save). Returns false if it failed; a failed save
    /// (e.g. another thread changed a list while it was serialized) is retried shortly.
    /// </summary>
    public bool SaveNow()
    {
        lock (_sync)
        {
            _debounce?.Dispose();
            _debounce = null;
            try
            {
                var json = JsonSerializer.Serialize(Current, JsonOptions);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _path, true);
                _retries = 0;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"Saving settings failed: {ex.Message}");
                if (!_disposed && _retries < MaxRetries)
                {
                    _retries++;
                    _debounce = new Timer(_ => SaveFromTimer(), null, 500, Timeout.Infinite);
                }
                return false;
            }
        }
    }

    /// <summary>
    /// Stops delayed saves without writing (an explicit <see cref="SaveNow"/> still works). Used when the
    /// settings are reloaded from disk: the old in-memory state must not overwrite the file afterwards.
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _debounce?.Dispose();
            _debounce = null;
        }
    }
}
