using System.Globalization;
using System.Text.RegularExpressions;

namespace ClevoControl.Core.Platform;

/// <summary>
/// Sound on Linux through <c>pactl</c>, which talks to PulseAudio and to PipeWire (pipewire-pulse),
/// i.e. practically every desktop distribution. Parsers are separate and public for tests.
/// </summary>
public static partial class LinuxAudio
{
    public static bool Available => Shell.Has("pactl");

    // ---- volume / mute -----------------------------------------------------------------

    public static int? GetVolume(bool input = false) =>
        ParseVolumePercent(Shell.Run("pactl", input ? "get-source-volume" : "get-sink-volume", input ? "@DEFAULT_SOURCE@" : "@DEFAULT_SINK@"));

    public static void SetVolume(int percent, bool input = false) =>
        Shell.Run("pactl", input ? "set-source-volume" : "set-sink-volume", input ? "@DEFAULT_SOURCE@" : "@DEFAULT_SINK@",
            Math.Clamp(percent, 0, 100).ToString(CultureInfo.InvariantCulture) + "%");

    public static bool? GetMute(bool input = false) =>
        ParseMute(Shell.Run("pactl", input ? "get-source-mute" : "get-sink-mute", input ? "@DEFAULT_SOURCE@" : "@DEFAULT_SINK@"));

    public static void SetMute(bool mute, bool input = false) =>
        Shell.Run("pactl", input ? "set-source-mute" : "set-sink-mute", input ? "@DEFAULT_SOURCE@" : "@DEFAULT_SINK@", mute ? "1" : "0");

    /// <summary>"Volume: front-left: 29491 /  45% / -20.81 dB, ..." → 45 (first channel).</summary>
    public static int? ParseVolumePercent(string? output)
    {
        if (output is null) return null;
        var m = PercentRegex().Match(output);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>"Mute: yes" → true.</summary>
    public static bool? ParseMute(string? output)
    {
        if (output is null) return null;
        if (output.Contains("Mute: yes", StringComparison.Ordinal)) return true;
        if (output.Contains("Mute: no", StringComparison.Ordinal)) return false;
        return null;
    }

    // ---- devices -----------------------------------------------------------------------

    public sealed record Device(string Name, string Description);

    public static List<Device> Sinks() => ParseDevices(Shell.Run("pactl", "list", "sinks"));

    public static List<Device> Sources() => ParseDevices(Shell.Run("pactl", "list", "sources"));

    public static string? DefaultSink() => Shell.Run("pactl", "get-default-sink")?.Trim();

    public static bool SetDefaultSink(string name) => Shell.Run("pactl", "set-default-sink", name) is not null;

    /// <summary>Parses "pactl list sinks|sources" blocks into (Name, Description).</summary>
    public static List<Device> ParseDevices(string? output)
    {
        var list = new List<Device>();
        if (output is null) return list;
        string? name = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Sink #", StringComparison.Ordinal) || line.StartsWith("Source #", StringComparison.Ordinal)) name = null;
            else if (line.StartsWith("Name: ", StringComparison.Ordinal)) name = line[6..].Trim();
            else if (line.StartsWith("Description: ", StringComparison.Ordinal) && name is not null)
            {
                list.Add(new Device(name, line[13..].Trim()));
                name = null;
            }
        }
        return list;
    }

    // ---- microphone in use ("On Air") --------------------------------------------------

    /// <summary>True if any application records from a real microphone (monitor sources and ClevoControl itself are ignored).</summary>
    public static bool IsMicrophoneInUse()
    {
        var outputs = Shell.Run("pactl", "list", "source-outputs");
        if (string.IsNullOrWhiteSpace(outputs)) return false;
        var monitors = ParseMonitorSourceIds(Shell.Run("pactl", "list", "short", "sources"));
        return CountMicrophoneRecorders(outputs, monitors) > 0;
    }

    /// <summary>"pactl list short sources": ids of sources whose name ends in ".monitor".</summary>
    public static HashSet<string> ParseMonitorSourceIds(string? shortSources)
    {
        var set = new HashSet<string>();
        if (shortSources is null) return set;
        foreach (var line in shortSources.Split('\n'))
        {
            var f = line.Split('\t');
            if (f.Length >= 2 && f[1].EndsWith(".monitor", StringComparison.Ordinal)) set.Add(f[0].Trim());
        }
        return set;
    }

    /// <summary>Counts source outputs (recording streams) that do not read from a monitor source and are not our own capture.</summary>
    public static int CountMicrophoneRecorders(string sourceOutputs, HashSet<string> monitorSourceIds)
    {
        var count = 0;
        string? source = null;
        string? binary = null;
        bool inBlock = false;
        void Flush()
        {
            if (inBlock && source is not null && !monitorSourceIds.Contains(source) && binary is not ("parec" or "pw-record" or "pavucontrol"))
                count++;
            source = binary = null;
        }
        foreach (var raw in sourceOutputs.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Source Output #", StringComparison.Ordinal)) { Flush(); inBlock = true; }
            else if (line.StartsWith("Source: ", StringComparison.Ordinal)) source = line[8..].Trim();
            else if (line.StartsWith("application.process.binary = ", StringComparison.Ordinal)) binary = line[29..].Trim().Trim('"');
        }
        Flush();
        return count;
    }

    [GeneratedRegex(@"(\d+)%")]
    private static partial Regex PercentRegex();
}
