using System.Text.Json;
using ClevoControl.Core.Services;
using Microsoft.Win32;

namespace ClevoControl.Core.Games;

public sealed record Cs2State(DateTime Received, int Health, int Armor, int Flashed, int Burning, string? Bomb, string? Phase);

/// <summary>
/// Counter-Strike 2 "Game State Integration" – an official Valve feature: the game POSTs its
/// state as JSON to a local URL configured by a .cfg file. No memory reading, no anti-cheat risk.
/// </summary>
public sealed class Cs2Integration : IDisposable
{
    public const string ConfigFileName = "gamestate_integration_clevocontrol.cfg";
    private MiniHttpServer? _server;

    public Cs2State? Latest { get; private set; }

    public bool IsListening => _server is not null;

    public string? Error { get; private set; }

    public void Start(int port)
    {
        if (_server?.Port == port) return;
        Stop();
        try
        {
            _server = new MiniHttpServer(port, Handle);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    public void Stop()
    {
        _server?.Dispose();
        _server = null;
    }

    private (int, string) Handle(string method, string path, string body)
    {
        if (method != "POST") return (200, "{}");
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;
            int health = 100, armor = 0, flashed = 0, burning = 0;
            if (root.TryGetProperty("player", out var player) && player.TryGetProperty("state", out var st))
            {
                health = Int(st, "health");
                armor = Int(st, "armor");
                flashed = Int(st, "flashed");
                burning = Int(st, "burning");
            }
            string? bomb = null, phase = null;
            if (root.TryGetProperty("round", out var round))
            {
                if (round.TryGetProperty("bomb", out var b)) bomb = b.GetString();
                if (round.TryGetProperty("phase", out var p)) phase = p.GetString();
            }
            Latest = new Cs2State(DateTime.UtcNow, health, armor, flashed, burning, bomb, phase);
        }
        catch
        {
            // malformed payload – ignore
        }
        return (200, "{}");
    }

    public static string ConfigContent(int port) => $$"""
        "ClevoControl"
        {
            "uri"       "http://127.0.0.1:{{port}}"
            "timeout"   "1.0"
            "buffer"    "0.0"
            "throttle"  "0.05"
            "heartbeat" "10.0"
            "data"
            {
                "provider"      "1"
                "player_id"     "1"
                "player_state"  "1"
                "round"         "1"
            }
        }
        """;

    /// <summary>Finds the CS2 cfg folder via Steam's library list.</summary>
    public static string? FindCs2ConfigFolder()
    {
        foreach (var lib in SteamLibraries())
        {
            var cfg = Path.Combine(lib, "steamapps", "common", "Counter-Strike Global Offensive", "game", "csgo", "cfg");
            if (Directory.Exists(cfg)) return cfg;
        }
        return null;
    }

    /// <summary>Writes the GSI config file. Returns the path, or null if CS2 was not found.</summary>
    public static string? InstallConfig(int port)
    {
        var folder = FindCs2ConfigFolder();
        if (folder is null) return null;
        var path = Path.Combine(folder, ConfigFileName);
        File.WriteAllText(path, ConfigContent(port));
        return path;
    }

    private static IEnumerable<string> SteamLibraries()
    {
        string? steam;
        if (OperatingSystem.IsWindows())
        {
            steam = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string)?.Replace('/', '\\');
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            steam = new[] { ".steam/steam", ".local/share/Steam", ".var/app/com.valvesoftware.Steam/.local/share/Steam" }
                .Select(p => Path.Combine(home, p)).FirstOrDefault(Directory.Exists);
        }
        if (string.IsNullOrEmpty(steam)) yield break;
        yield return steam;
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        foreach (var line in File.ReadLines(vdf))
        {
            var t = line.Trim();
            if (!t.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = t.Split('"', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3) yield return parts[^1].Replace(@"\\", @"\");
        }
    }

    public void Dispose() => Stop();
}
