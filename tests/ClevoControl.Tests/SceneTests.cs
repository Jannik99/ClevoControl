using ClevoControl.Core;
using ClevoControl.Core.Automation;
using ClevoControl.Core.Settings;
using Xunit;

namespace ClevoControl.Tests;

public class SceneCaptureTests
{
    private static Scene Full() => new()
    {
        Name = "now", Icon = "",
        Keyboard = new KeyboardSettings { Effect = LightingEffectKind.Static, Color = new RgbColor(255, 0, 140), Brightness = 100 },
        Profile = PerformanceProfile.Entertainment,
        FanMode = FanMode.Auto,
        Volume = 18,
    };

    [Fact]
    public void Captures_only_the_chosen_parts()
    {
        var s = Scene.Compose("Abends", "★", SceneParts.Lighting | SceneParts.Profile, Full());
        Assert.Equal("Abends", s.Name);
        Assert.NotNull(s.Keyboard);
        Assert.Equal(PerformanceProfile.Entertainment, s.Profile);
        Assert.Null(s.FanMode);
        Assert.Null(s.Volume);
        Assert.Equal(SceneParts.Lighting | SceneParts.Profile, s.Parts);
    }

    [Fact]
    public void Captured_lighting_is_a_copy()
    {
        var current = Full();
        var s = Scene.Compose("x", "★", SceneParts.Lighting, current);
        current.Keyboard!.Brightness = 5;
        Assert.Equal(100, s.Keyboard!.Brightness);
    }

    [Fact]
    public void Editing_keeps_stored_values_unless_recaptured()
    {
        var previous = new Scene { Name = "Nacht", Profile = PerformanceProfile.Quiet, WindowsPowerMode = WindowsPowerMode.BestEfficiency, Volume = 40 };
        var kept = Scene.Compose("Night", "★", SceneParts.Profile | SceneParts.Fans, Full(), previous, recapture: false);
        Assert.Equal("Night", kept.Name);
        Assert.Equal(PerformanceProfile.Quiet, kept.Profile);            // stored value kept
        Assert.Equal(WindowsPowerMode.BestEfficiency, kept.WindowsPowerMode);
        Assert.Equal(FanMode.Auto, kept.FanMode);                       // newly added part comes from the current state
        Assert.Null(kept.Volume);                                       // unticked part is dropped

        var fresh = Scene.Compose("Night", "★", SceneParts.Profile, Full(), previous, recapture: true);
        Assert.Equal(PerformanceProfile.Entertainment, fresh.Profile);
        Assert.Null(fresh.WindowsPowerMode);
    }

    [Fact]
    public void Rejects_empty_and_duplicate_names()
    {
        var scenes = new List<Scene> { new() { Name = "Gaming" }, new() { Name = "Film" } };
        Assert.Equal(SceneNameProblem.Empty, Scene.CheckName(scenes, "  "));
        Assert.Equal(SceneNameProblem.Duplicate, Scene.CheckName(scenes, " gaming "));
        Assert.Null(Scene.CheckName(scenes, "Abends"));
        Assert.Null(Scene.CheckName(scenes, "Gaming", except: scenes[0])); // renaming to itself is fine
    }

    [Fact]
    public void Default_scenes_follow_the_language()
    {
        Assert.Contains(Scene.Defaults(german: true), s => s.Name == "Nacht");
        var en = Scene.Defaults(german: false);
        Assert.Contains(en, s => s.Name == "Night");
        Assert.DoesNotContain(en, s => s.Name is "Nacht" or "Musik" or "Arbeit");
        // Default category mapping points at default scenes of the same language.
        foreach (var german in new[] { true, false })
        {
            var names = Scene.Defaults(german).Select(s => s.Name).ToHashSet();
            Assert.All(AutoProfileSettings.DefaultCategoryScenes(german).Values, n => Assert.Contains(n, names));
        }
    }
}

public class SceneReferenceTests
{
    private static AutoProfileSettings Settings() => new()
    {
        CategoryScenes = new() { [AppCategory.Game] = "Gaming", [AppCategory.Video] = "Film", [AppCategory.Meeting] = "gaming" },
        CustomRules =
        [
            new AppRule { ProcessName = "obs64", Category = AppCategory.Video, SceneName = "Gaming" },
            new AppRule { ProcessName = "vlc", Category = AppCategory.Video },
        ],
    };

    [Fact]
    public void Finds_usages_ignoring_case()
    {
        var u = Settings().UsagesOf("GAMING");
        Assert.Equal(3, u.Count);
        Assert.Contains(u, x => x.Category == AppCategory.Game);
        Assert.Contains(u, x => x.Category == AppCategory.Meeting);
        Assert.Contains(u, x => x.Rule?.ProcessName == "obs64");
    }

    [Fact]
    public void Rename_updates_categories_and_rules()
    {
        var a = Settings();
        Assert.Equal(3, a.RenameSceneReferences("Gaming", "Zocken"));
        Assert.Equal("Zocken", a.CategoryScenes[AppCategory.Game]);
        Assert.Equal("Zocken", a.CategoryScenes[AppCategory.Meeting]);
        Assert.Equal("Film", a.CategoryScenes[AppCategory.Video]);
        Assert.Equal("Zocken", a.CustomRules[0].SceneName);
        Assert.Null(a.CustomRules[1].SceneName);
    }

    [Fact]
    public void Delete_removes_references_and_undo_restores_them()
    {
        var a = Settings();
        var removed = a.RemoveSceneReferences("Gaming");
        Assert.False(removed.IsEmpty);
        Assert.False(a.CategoryScenes.ContainsKey(AppCategory.Game));
        Assert.False(a.CategoryScenes.ContainsKey(AppCategory.Meeting));
        Assert.Null(a.CustomRules[0].SceneName); // falls back to its category's scene
        Assert.Empty(a.UsagesOf("Gaming"));

        a.RestoreSceneReferences(removed);
        Assert.Equal("Gaming", a.CategoryScenes[AppCategory.Game]);
        Assert.Equal("Gaming", a.CategoryScenes[AppCategory.Meeting]);
        Assert.Equal("Gaming", a.CustomRules[0].SceneName);
    }

    [Fact]
    public void Prune_drops_references_to_missing_scenes()
    {
        var a = Settings();
        Assert.Equal(3, a.PruneSceneReferences(["Film", "Musik"]));
        Assert.Equal("Film", a.CategoryScenes[AppCategory.Video]);
        Assert.Single(a.CategoryScenes);
        Assert.Null(a.CustomRules[0].SceneName);
    }
}
