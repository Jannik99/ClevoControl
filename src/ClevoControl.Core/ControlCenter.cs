using System.Text.Json;
using ClevoControl.Core.Games;
using ClevoControl.Core.Hardware;
using ClevoControl.Core.Lighting;
using ClevoControl.Core.Services;
using ClevoControl.Core.Settings;

namespace ClevoControl.Core;

/// <summary>Which feature changed the state last (see <see cref="ControlCenter.LastChange"/>).</summary>
public enum ChangeSource
{
    None,
    /// <summary>Changed by hand on a page or in the tray.</summary>
    User,
    /// <summary>A scene applied by the user.</summary>
    Scene,
    /// <summary>Automatic scene per app.</summary>
    Automation,
    /// <summary>AC/battery profile auto-switch.</summary>
    PowerAutoSwitch,
    /// <summary>Fans per profile loaded the fan settings of the new profile.</summary>
    FanProfile,
    /// <summary>Global hotkey.</summary>
    Hotkey,
    /// <summary>Local API / OpenRGB / game integration.</summary>
    Integration,
}

/// <summary>Last state change: source, a localized one-line description and when it happened.</summary>
public sealed record StateChange(ChangeSource Source, string Description, DateTime Time);

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
        Fans = new FanController(hardware, Sensors, Settings.Fan, () => store.SaveSoon());
        Power = new PowerService(hardware);
        Sound = new SoundService();
        Hook = new KeyboardHook();
        Hook.KeyPressed += Lighting.NotifyKeyPress;
        Hook.BlockWindowsKey = Settings.General.WinKeyLock;
        _layoutChoice = Settings.KeyRemap.KeyboardLayout;
        Platform.KeyboardLayout.Use(_layoutChoice);
        KeyRemap = new KeyRemapService();
        KeyRemap.Apply(Settings.KeyRemap);
        Hook.Filter = KeyRemap.Filter;
        Power.ProfileApplied += OnProfileApplied;
        Sensors.Updated += CheckOverheat;
        _fanProfileOwner = Settings.Power.Profile;

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
        _powerTimer = new Timer(_ => { try { CheckPowerSource(); } catch { } }, null, 5000, 5000);

        German = Language.IsGerman(Settings.General.Language);
        _hotkeysOn = Settings.General.SceneHotkeys;
        store.SaveRequested += OnSaveRequested;

        ApplyIntegrations();
        UpdateLinuxKeyboard();
        AutoProfiles = new Automation.AutoProfileService(this);
    }

    // ------------------------------------------------------------------ status: active scene, last change source

    /// <summary>Language of texts produced by Core (notifications, <see cref="StateChange.Description"/>), fixed at start like the apps' Loc.</summary>
    public bool German { get; }

    private string T(string de, string en) => German ? de : en;

    /// <summary>Name of the scene that is in effect right now (set by applying a scene, cleared by manual changes), or null.</summary>
    public string? ActiveScene { get; private set; }

    /// <summary>Who changed the state last, with a description for the UI (null = nothing since start).</summary>
    public StateChange? LastChange { get; private set; }

    public ChangeSource LastChangeSource => LastChange?.Source ?? ChangeSource.None;

    /// <summary>Raised (any thread) when <see cref="ActiveScene"/> or <see cref="LastChange"/> changed.</summary>
    public event Action? StatusChanged;

    /// <summary>Raised (any thread) when global hotkeys were switched on or off in the settings.</summary>
    public event Action? HotkeysChanged;

    /// <summary>Nesting depth of changes made by a feature (scene, hotkey, automation …) rather than by hand, per thread.</summary>
    [ThreadStatic] private static int t_featureDepth;

    private bool _hotkeysOn;

    private void OnSaveRequested()
    {
        var on = Settings.General.SceneHotkeys;
        if (on == _hotkeysOn) return;
        _hotkeysOn = on;
        UpdateLinuxKeyboard();
        HotkeysChanged?.Invoke();
    }

    /// <summary>Records who changed the state. Pages may call this for changes made through other features.</summary>
    public void ReportChange(ChangeSource source, string description)
    {
        LastChange = new StateChange(source, description, DateTime.Now);
        StatusChanged?.Invoke();
    }

    /// <summary>Runs <paramref name="action"/> as a change made by a feature: manual-change tracking is suspended.</summary>
    private void AsFeature(Action action)
    {
        t_featureDepth++;
        try { action(); }
        finally { t_featureDepth--; }
    }

    /// <summary>Lighting, profile or fans were changed: by hand unless a feature is running.</summary>
    private void NoteManualChange()
    {
        if (t_featureDepth > 0) return;
        if (ActiveScene is null && LastChange?.Source is null or ChangeSource.User) return;
        ActiveScene = null;
        LastChange = new StateChange(ChangeSource.User, T("Von Hand eingestellt", "Set by hand"), DateTime.Now);
        StatusChanged?.Invoke();
    }

    internal static string ProfileText(PerformanceProfile p, bool de) => p switch
    {
        PerformanceProfile.Quiet => de ? "Leise" : "Quiet",
        PerformanceProfile.PowerSaving => de ? "Stromsparen" : "Power saving",
        PerformanceProfile.Performance => de ? "Maximale Leistung" : "Max performance",
        _ => de ? "Ausgewogen" : "Balanced",
    };

    internal static string FanModeText(FanMode m, bool de) => m switch
    {
        FanMode.Max => "Maximum",
        FanMode.Custom => de ? "Eigene Kurve" : "Custom curve",
        _ => de ? "Automatisch" : "Automatic",
    };

    /// <summary>AC/battery check (timer and power events). Records the change when the auto-switch set a profile.</summary>
    public void CheckPowerSource()
    {
        var before = Power.ActiveProfile;
        Power.CheckPowerSource(Settings.Power);
        if (Settings.Power.AutoSwitch && Power.ActiveProfile is { } p && p != before)
            ReportChange(ChangeSource.PowerAutoSwitch, T($"Profil „{ProfileText(p, true)}“ vom Netzteil-Automatismus gesetzt ({(PowerService.IsOnAc ? "Netzteil" : "Akku")})",
                $"Profile “{ProfileText(p, false)}” set by the AC/battery auto-switch ({(PowerService.IsOnAc ? "on AC" : "on battery")})"));
    }

    private Platform.LinuxKeyboard? _linuxKeyboard;

    /// <summary>Why key events are unavailable on Linux (permissions), or null.</summary>
    public string? KeyboardInputError { get; private set; }

    /// <summary>Raised after a hotkey did something, with a short message for a notification.</summary>
    public event Action<string>? HotkeyHandled;

    /// <summary>
    /// Linux: reads /dev/input only while something needs it (reactive typing effect or scene hotkeys).
    /// On Windows the app's own keyboard hook and RegisterHotKey do this.
    /// </summary>
    private void UpdateLinuxKeyboard()
    {
        if (!OperatingSystem.IsLinux()) return;
        var needed = Settings.General.SceneHotkeys || Settings.Keyboard.Effect == LightingEffectKind.Reactive;
        if (needed && _linuxKeyboard is null)
        {
            _linuxKeyboard = new Platform.LinuxKeyboard();
            KeyboardInputError = _linuxKeyboard.Error;
            _linuxKeyboard.KeyPressed += Lighting.NotifyKeyPress;
            _linuxKeyboard.Hotkey += id => { if (Settings.General.SceneHotkeys) HandleHotkey(id); };
        }
        else if (!needed && _linuxKeyboard is not null)
        {
            _linuxKeyboard.Dispose();
            _linuxKeyboard = null;
            KeyboardInputError = null;
        }
    }

    /// <summary>
    /// Ctrl+Alt+Shift hotkeys: 0 = keyboard light on/off, 1–9 = scenes, 20 (F) = fans full on/off.
    /// Returns the message to show, or null if the id does nothing.
    /// </summary>
    public string? HandleHotkey(int id)
    {
        string? msg = null;
        if (id == 20)
        {
            AsFeature(ToggleFanBoost);
            msg = T("Lüfter: ", "Fans: ") + FanModeText(Settings.Fan.Mode, German);
            ReportChange(ChangeSource.Hotkey, T($"Lüfter per Tastenkürzel auf „{FanModeText(Settings.Fan.Mode, true)}“ gestellt",
                $"Fans set to “{FanModeText(Settings.Fan.Mode, false)}” by hotkey"));
        }
        else if (id == 0)
        {
            Settings.Keyboard.Enabled = !Settings.Keyboard.Enabled;
            AsFeature(ApplyKeyboard);
            msg = Settings.Keyboard.Enabled ? T("Tastaturlicht an", "Keyboard light on") : T("Tastaturlicht aus", "Keyboard light off");
            ReportChange(ChangeSource.Hotkey, msg + T(" (Tastenkürzel)", " (hotkey)"));
        }
        else if (id >= 1 && id <= Settings.Scenes.Count)
        {
            var scene = Settings.Scenes[id - 1];
            ApplyScene(scene, ChangeSource.Hotkey, T($"Szene „{scene.Name}“ per Tastenkürzel", $"Scene “{scene.Name}” by hotkey"));
            msg = $"{scene.Icon} {scene.Name}";
        }
        if (msg is not null) HotkeyHandled?.Invoke(msg);
        return msg;
    }

    /// <summary>Text of a hotkey for notifications: "Strg+Alt+Umschalt+3" / "Ctrl+Alt+Shift+3".</summary>
    public string HotkeyText(string key) => T("Strg+Alt+Umschalt+", "Ctrl+Alt+Shift+") + key;

    /// <summary>Notification text when a hotkey could not be registered.</summary>
    public string HotkeyTakenText(IReadOnlyList<string> keys) => keys.Count == 1
        ? T($"{HotkeyText(keys[0])} ist bereits von einem anderen Programm belegt.", $"{HotkeyText(keys[0])} is already used by another program.")
        : T($"{HotkeyText(string.Join(", ", keys))} sind bereits von anderen Programmen belegt.", $"{HotkeyText(string.Join(", ", keys))} are already used by other programs.");

    public Automation.AutoProfileService AutoProfiles { get; }

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
    public KeyRemapService KeyRemap { get; }

    /// <summary>Raised (thread pool) when CPU or GPU reach the overheat threshold; at most every 5 minutes.</summary>
    public event Action<string>? Overheat;

    /// <summary>Raised when the active fan settings were swapped because the performance profile changed.</summary>
    public event Action? FanSettingsSwapped;

    private PerformanceProfile _fanProfileOwner;
    private DateTime _lastOverheat = DateTime.MinValue;
    private FanMode? _modeBeforeBoost;

    public int OpenRgbClients => _openRgb?.ClientCount ?? 0;
    public string? IntegrationError { get; private set; }

    public void ApplyKeyboard()
    {
        NoteManualChange();
        Lighting.Apply(Settings.Keyboard);
        ApplyIntegrations();
        UpdateLinuxKeyboard();
        Store.SaveSoon();
    }

    public void ApplyFans()
    {
        NoteManualChange();
        Fans.Apply(Settings.Fan);
        Store.SaveSoon();
    }

    public void ApplyPower()
    {
        NoteManualChange();
        Power.SetProfile(Settings.Power.Profile, Settings.Power.SyncWindowsPowerMode);
        Power.CheckPowerSource(Settings.Power, force: true);
        Store.SaveSoon();
    }

    public void ApplyGeneral()
    {
        Hook.BlockWindowsKey = Settings.General.WinKeyLock;
        UpdateLinuxKeyboard();
        Store.SaveSoon();
    }

    private string? _layoutChoice;

    public void ApplyKeyRemap()
    {
        if (!string.Equals(Settings.KeyRemap.KeyboardLayout, _layoutChoice, StringComparison.Ordinal))
        {
            _layoutChoice = Settings.KeyRemap.KeyboardLayout;
            Platform.KeyboardLayout.Use(_layoutChoice);
        }
        KeyRemap.Apply(Settings.KeyRemap);
        Store.SaveSoon();
    }

    /// <summary>Turbo off / CPU limit through the Windows power plan. Returns false if Windows refused.</summary>
    public bool ApplyCooling()
    {
        var c = Settings.Cooling;
        if (c.SavedBoostMode is null)
        {
            try
            {
                var (boost, max) = CoolingService.Read();
                if (boost != 0 && max >= 100) { c.SavedBoostMode = boost; c.SavedMaxState = max; }
            }
            catch { }
        }
        var ok = CoolingService.Apply(c.Mode, c.SavedBoostMode ?? 2, (uint)(c.SavedMaxState ?? 100));
        Store.SaveSoon();
        return ok;
    }

    /// <summary>Toggles fans between maximum and the previous mode (hotkey).</summary>
    public void ToggleFanBoost()
    {
        if (Settings.Fan.Mode == FanMode.Max)
        {
            Settings.Fan.Mode = _modeBeforeBoost ?? FanMode.Auto;
            _modeBeforeBoost = null;
        }
        else
        {
            _modeBeforeBoost = Settings.Fan.Mode;
            Settings.Fan.Mode = FanMode.Max;
        }
        ApplyFans();
    }

    private FanProfile SnapshotFans() => new()
    {
        Mode = Settings.Fan.Mode,
        CpuCurve = Settings.Fan.CpuCurve.Select(p => new FanCurvePoint(p.Temperature, p.Duty)).ToList(),
        GpuCurve = Settings.Fan.GpuCurve.Select(p => new FanCurvePoint(p.Temperature, p.Duty)).ToList(),
    };

    /// <summary>Call when "fans per profile" is switched on, so the current fans belong to the current profile.</summary>
    public void InitFanProfiles()
    {
        _fanProfileOwner = Settings.Power.Profile;
        Settings.Fan.Profiles[_fanProfileOwner] = SnapshotFans();
        Store.SaveSoon();
    }

    private void OnProfileApplied(PerformanceProfile profile)
    {
        var f = Settings.Fan;
        if (!f.PerProfile || profile == _fanProfileOwner)
        {
            _fanProfileOwner = profile;
            return;
        }
        f.Profiles[_fanProfileOwner] = SnapshotFans();
        if (f.Profiles.TryGetValue(profile, out var fp))
        {
            f.Mode = fp.Mode;
            f.CpuCurve = fp.CpuCurve.Select(p => new FanCurvePoint(p.Temperature, p.Duty)).ToList();
            f.GpuCurve = fp.GpuCurve.Select(p => new FanCurvePoint(p.Temperature, p.Duty)).ToList();
        }
        _fanProfileOwner = profile;
        Fans.Apply(f);
        Store.SaveSoon();
        if (t_featureDepth == 0)
            ReportChange(ChangeSource.FanProfile, T($"Lüfter vom Profil „{ProfileText(profile, true)}“ übernommen (Lüfter pro Profil)",
                $"Fans loaded from profile “{ProfileText(profile, false)}” (fans per profile)"));
        FanSettingsSwapped?.Invoke();
    }

    private void CheckOverheat(SensorSnapshot s)
    {
        var c = Settings.Cooling;
        if (!c.OverheatWarning) return;
        var hot = Math.Max(s.Cpu.TemperatureC, s.Gpu.TemperatureC);
        if (hot < c.OverheatThreshold || DateTime.UtcNow - _lastOverheat < TimeSpan.FromMinutes(5)) return;
        _lastOverheat = DateTime.UtcNow;
        var which = s.Cpu.TemperatureC >= s.Gpu.TemperatureC ? "CPU" : "GPU";
        Overheat?.Invoke($"{which} {hot} °C");
    }

    /// <summary>Applies a scene chosen by the user (scene page, home, tray).</summary>
    public void ApplyScene(Scene scene) =>
        ApplyScene(scene, ChangeSource.Scene, T($"Szene „{scene.Name}“ angewendet", $"Scene “{scene.Name}” applied"));

    /// <summary>Applies a scene and records <paramref name="source"/> / <paramref name="description"/> as the last change.</summary>
    public void ApplyScene(Scene scene, ChangeSource source, string? description) =>
        ApplySceneAs(scene, source, description, scene.Name);

    /// <summary>Like <see cref="ApplyScene(Scene, ChangeSource, string?)"/>, but <paramref name="activeName"/> becomes <see cref="ActiveScene"/> (null for a captured "restore" state).</summary>
    internal void ApplySceneAs(Scene scene, ChangeSource source, string? description, string? activeName)
    {
        AsFeature(() => ApplySceneValues(scene));
        ActiveScene = activeName;
        LastChange = new StateChange(source, description ?? scene.Name, DateTime.Now);
        StatusChanged?.Invoke();
    }

    private void ApplySceneValues(Scene scene)
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

    /// <summary>Captures only <paramref name="parts"/> of the current state.</summary>
    public Scene CaptureScene(string name, string icon, SceneParts parts) =>
        Scene.Compose(name, icon, parts, CaptureScene(name, icon));

    // ------------------------------------------------------------------ scene list management (keeps automation references valid)

    /// <summary>Undo information of <see cref="DeleteScene"/>.</summary>
    public sealed record SceneRemoval(Scene Scene, int Index, AutoProfileSettings.RemovedReferences References, bool WasActive);

    /// <summary>Where the scene is used by the automation (category mappings and own rules).</summary>
    public List<AutoProfileSettings.SceneUsage> SceneUsages(Scene scene) => Settings.AutoProfiles.UsagesOf(scene.Name);

    /// <summary>Adds a scene at the end of the list.</summary>
    public void AddScene(Scene scene)
    {
        Settings.Scenes.Add(scene);
        Store.SaveSoon();
        StatusChanged?.Invoke();
    }

    /// <summary>Removes a scene and every automation reference to it. Pass the result to <see cref="RestoreScene"/> to undo.</summary>
    public SceneRemoval DeleteScene(Scene scene)
    {
        var index = Settings.Scenes.IndexOf(scene);
        if (index >= 0) Settings.Scenes.RemoveAt(index);
        var refs = Settings.AutoProfiles.RemoveSceneReferences(scene.Name);
        var wasActive = string.Equals(ActiveScene, scene.Name, StringComparison.OrdinalIgnoreCase);
        if (wasActive) ActiveScene = null;
        Store.SaveSoon();
        StatusChanged?.Invoke();
        return new SceneRemoval(scene, index, refs, wasActive);
    }

    /// <summary>Undo for <see cref="DeleteScene"/>.</summary>
    public void RestoreScene(SceneRemoval removal)
    {
        var list = Settings.Scenes;
        if (!list.Contains(removal.Scene)) list.Insert(Math.Clamp(removal.Index, 0, list.Count), removal.Scene);
        Settings.AutoProfiles.RestoreSceneReferences(removal.References);
        if (removal.WasActive && ActiveScene is null) ActiveScene = removal.Scene.Name;
        Store.SaveSoon();
        StatusChanged?.Invoke();
    }

    /// <summary>Replaces the scene at <paramref name="index"/> (edit dialog). A new name is carried over to the automation and the active scene.</summary>
    public void ReplaceScene(int index, Scene updated)
    {
        var old = Settings.Scenes[index];
        Settings.Scenes[index] = updated;
        if (!string.Equals(old.Name, updated.Name, StringComparison.Ordinal))
        {
            Settings.AutoProfiles.RenameSceneReferences(old.Name, updated.Name);
            if (string.Equals(ActiveScene, old.Name, StringComparison.OrdinalIgnoreCase)) ActiveScene = updated.Name;
            AutoProfiles.OnSceneRenamed(old.Name, updated.Name);
        }
        Store.SaveSoon();
        StatusChanged?.Invoke();
    }

    /// <summary>Moves a scene up (-1) or down (+1). Hotkey numbers follow the order.</summary>
    public void MoveScene(int index, int delta)
    {
        var list = Settings.Scenes;
        var target = index + delta;
        if (index < 0 || index >= list.Count || target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]);
        Store.SaveSoon();
        StatusChanged?.Invoke();
    }

    /// <summary>Replaces all scenes by the defaults (in <paramref name="german"/> or English) and drops references to scenes that are gone.</summary>
    public void RestoreDefaultScenes(bool german)
    {
        Settings.Scenes = Scene.Defaults(german);
        Settings.AutoProfiles.PruneSceneReferences(Settings.Scenes.Select(s => s.Name));
        if (ActiveScene is not null && !Settings.Scenes.Any(s => string.Equals(s.Name, ActiveScene, StringComparison.OrdinalIgnoreCase))) ActiveScene = null;
        Store.SaveSoon();
        StatusChanged?.Invoke();
    }

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
                    ApplyScene(scene, ChangeSource.Integration, T($"Szene „{scene.Name}“ über die lokale API", $"Scene “{scene.Name}” via the local API"));
                    return (200, "{\"ok\":true}");
                case "/effect":
                    Settings.Keyboard.Effect = Enum.Parse<LightingEffectKind>(r.GetProperty("effect").GetString()!, true);
                    AsFeature(ApplyKeyboard);
                    ActiveScene = null;
                    ReportChange(ChangeSource.Integration, T($"Effekt „{Settings.Keyboard.Effect}“ über die lokale API", $"Effect “{Settings.Keyboard.Effect}” via the local API"));
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

    private Timer? _resumeTimer;

    /// <summary>
    /// Before sleep: stop every access to the EC (lighting, sensors) and hand fans back to the firmware.
    /// </summary>
    public void OnSuspend()
    {
        Log.Info("Suspend: pausing EC access");
        _resumeTimer?.Dispose();
        Lighting.Paused = true;
        Sensors.Paused = true;
        try { Fans.Suspend(); } catch (Exception ex) { Log.Error("Fan suspend failed", ex); }
    }

    /// <summary>
    /// After resume the ACPI/EC and the PCIe devices need a moment. Talking to the EC right away
    /// (as 0.2.0 did) is risky, so everything restarts only after a delay. The performance profile
    /// is kept by the firmware itself and is not re-sent.
    /// </summary>
    public void OnResume()
    {
        Log.Info("Resume: waiting before touching the EC");
        _resumeTimer?.Dispose();
        _resumeTimer = new Timer(_ =>
        {
            try
            {
                Sensors.Paused = false;
                Fans.Resume();
                Lighting.Invalidate();
                Lighting.Paused = false;
                Log.Info("Resume: EC access re-enabled");
            }
            catch (Exception ex)
            {
                Log.Error("Resume failed", ex);
            }
        }, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        Store.SaveRequested -= OnSaveRequested;
        AutoProfiles.Dispose();
        _powerTimer.Dispose();
        Store.SaveNow();
        Hook.Dispose();
        _linuxKeyboard?.Dispose();
        _openRgb?.Dispose();
        _api?.Dispose();
        Cs2.Dispose();
        Lighting.Dispose();
        if (Settings.General.RestoreAutoFanOnExit || Settings.Fan.Mode == FanMode.Custom) Fans.Dispose();
        Sensors.Dispose();
        Sound.Dispose();
    }
}
