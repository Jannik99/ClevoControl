using ClevoControl.Core.Settings;

namespace ClevoControl.Core.Automation;

/// <summary>
/// Watches the foreground app and switches scenes automatically: e.g. a game → "Gaming",
/// Netflix in the browser → "Film". When you leave the app, the previous state comes back.
/// </summary>
public sealed class AutoProfileService : IDisposable
{
    private readonly ControlCenter _cc;
    private readonly Timer _timer;
    private Detection _candidate = Detection.Nothing;
    private int _stableTicks;
    private Scene? _restore;
    private string? _appliedScene;

    public AutoProfileService(ControlCenter cc)
    {
        _cc = cc;
        _timer = new Timer(_ => Tick(), null, 3000, 1500);
    }

    /// <summary>Marshals scene changes onto the UI thread (set by the app).</summary>
    public Action<Action> Invoke { get; set; } = a => a();

    /// <summary>The app currently in the foreground and how it was classified.</summary>
    public Detection Current { get; private set; } = Detection.Nothing;

    /// <summary>Scene applied automatically right now (null = none).</summary>
    public string? ActiveScene => _appliedScene;

    /// <summary>Last foreground process that was not ClevoControl itself (for "use last app").</summary>
    public string? LastForeign { get; private set; }

    public event Action? Changed;

    private AutoProfileSettings S => _cc.Settings.AutoProfiles;

    private void Tick()
    {
        try
        {
            var fg = AppDetector.Foreground(S);
            if (fg is not null && !fg.ProcessName.Equals("ClevoControl", StringComparison.OrdinalIgnoreCase) && !fg.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                LastForeign = fg.ProcessName;
            var detection = fg is null ? Detection.Nothing : AppDetector.Classify(fg, S);
            if (detection.Category != Current.Category || detection.AppName != Current.AppName)
            {
                Current = detection;
                Changed?.Invoke();
            }
            if (!S.Enabled)
            {
                if (_appliedScene is not null) Leave();
                return;
            }

            // Require two identical readings (~3 s) so alt-tabbing through windows does not flicker.
            if (detection.Category == _candidate.Category && detection.SceneOverride == _candidate.SceneOverride) _stableTicks++;
            else { _candidate = detection; _stableTicks = 0; }
            if (_stableTicks < 1) return;

            var scene = SceneFor(detection);
            if (scene is null)
            {
                // A foreground app without mapping (desktop, browser on another site …) ends the automatic scene.
                if (_appliedScene is not null && detection.Category == AppCategory.None) Leave();
                return;
            }
            if (scene.Name == _appliedScene) return;
            Enter(scene, detection);
        }
        catch (Exception ex)
        {
            Log.Warn("Auto profiles: " + ex.Message);
        }
    }

    private Scene? SceneFor(Detection d)
    {
        var name = d.SceneOverride;
        if (name is null && d.Category != AppCategory.None) S.CategoryScenes.TryGetValue(d.Category, out name);
        if (string.IsNullOrEmpty(name)) return null;
        return _cc.Settings.Scenes.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string CategoryText(AppCategory c, bool de) => c switch
    {
        AppCategory.Game => de ? "Spiel" : "game",
        AppCategory.Video => de ? "Video" : "video",
        AppCategory.Music => de ? "Musik" : "music",
        AppCategory.Meeting => de ? "Anruf/Meeting" : "call/meeting",
        _ => "",
    };

    private string? _activeBefore;

    private void Enter(Scene scene, Detection d)
    {
        Invoke(() =>
        {
            if (_appliedScene is null)
            {
                _activeBefore = _cc.ActiveScene;
                if (S.RestoreOnLeave) _restore = _cc.CaptureScene("restore", "");
            }
            var de = _cc.German;
            var app = d.AppName.Length > 0 ? d.AppName : d.ProcessName;
            _cc.ApplyScene(scene, ChangeSource.Automation, de
                ? $"Szene „{scene.Name}“ – automatisch, weil {CategoryText(d.Category, true)} „{app}“ erkannt"
                : $"Scene “{scene.Name}” – automatically, because {CategoryText(d.Category, false)} “{app}” was detected");
            _appliedScene = scene.Name;
            Log.Info($"Auto profile: {d.AppName} ({d.Category}) -> scene \"{scene.Name}\"");
            Changed?.Invoke();
        });
    }

    private void Leave()
    {
        Invoke(() =>
        {
            if (_restore is not null)
            {
                _cc.ApplySceneAs(_restore, ChangeSource.Automation,
                    _cc.German ? "Vorheriger Zustand wiederhergestellt (Automatik)" : "Previous state restored (automation)", _activeBefore);
                Log.Info("Auto profile: restored previous state");
            }
            _restore = null;
            _appliedScene = null;
            _activeBefore = null;
            Changed?.Invoke();
        });
    }

    /// <summary>Keeps the automatic scene when it was renamed (otherwise the next tick would apply it again).</summary>
    internal void OnSceneRenamed(string oldName, string newName)
    {
        if (string.Equals(_appliedScene, oldName, StringComparison.OrdinalIgnoreCase)) _appliedScene = newName;
        if (string.Equals(_activeBefore, oldName, StringComparison.OrdinalIgnoreCase)) _activeBefore = newName;
    }

    public void Dispose() => _timer.Dispose();
}
