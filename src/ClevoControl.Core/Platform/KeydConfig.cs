using System.Text;
using ClevoControl.Core.Settings;

namespace ClevoControl.Core.Platform;

/// <summary>
/// Linux key mapping: instead of grabbing the keyboard itself (a hang would block all typing),
/// ClevoControl writes a configuration for <see href="https://github.com/rvaiya/keyd">keyd</see>,
/// a widely packaged key remapping daemon that works on X11, Wayland and the console.
/// Key codes are the US virtual keys of the physical key positions (see <see cref="KeyboardLayout"/>),
/// so the mapping follows the real keyboard whatever its layout.
/// </summary>
public static class KeydConfig
{
    public const string InstallPath = "/etc/keyd/clevocontrol.conf";

    /// <summary>Virtual-key code (US code of the physical position) → keyd key name.</summary>
    public static string? KeyName(int vk)
    {
        if (vk is >= 'A' and <= 'Z') return char.ToLowerInvariant((char)vk).ToString();
        if (vk is >= '0' and <= '9') return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "f" + (vk - 0x6F);
        if (vk is >= 0x60 and <= 0x69) return "kp" + (vk - 0x60);
        return vk switch
        {
            0x1B => "esc", 0x09 => "tab", 0x0D => "enter", 0x20 => "space", 0x08 => "backspace",
            0x2E => "delete", 0x2D => "insert", 0x24 => "home", 0x23 => "end", 0x21 => "pageup", 0x22 => "pagedown",
            0x26 => "up", 0x28 => "down", 0x25 => "left", 0x27 => "right",
            0x2C => "sysrq", 0x13 => "pause", 0x91 => "scrolllock", 0x14 => "capslock", 0x90 => "numlock", 0x5D => "compose",
            0xAF => "volumeup", 0xAE => "volumedown", 0xAD => "mute", 0xB3 => "playpause", 0xB0 => "nextsong",
            0xB1 => "previoussong", 0xB2 => "stopcd", 0xB7 => "calc", 0xB4 => "mail", 0xAC => "homepage",
            0x11 => "leftcontrol", 0x12 => "leftalt", 0x10 => "leftshift", 0x5B => "leftmeta", 0x5C => "rightmeta",
            0x6A => "kpasterisk", 0x6B => "kpplus", 0x6D => "kpminus", 0x6E => "kpdot", 0x6F => "kpslash",
            // character keys by physical position (US codes)
            0xC0 => "grave", 0xBD => "minus", 0xBB => "equal", 0xDB => "leftbrace", 0xDD => "rightbrace", 0xDC => "backslash",
            0xBA => "semicolon", 0xDE => "apostrophe", 0xE2 => "102nd", 0xBC => "comma", 0xBE => "dot", 0xBF => "slash",
            _ => null,
        };
    }

    /// <summary>"Ctrl+Shift+Esc" → "C-S-esc" (keyd chord syntax).</summary>
    public static string? Shortcut(string text)
    {
        if (Services.KeyRemapService.ParseShortcut(text) is not { } keys) return null;
        var mods = new StringBuilder();
        string? main = null;
        foreach (var vk in keys)
        {
            switch (vk)
            {
                case 0x11: mods.Append("C-"); break;
                case 0x12: mods.Append("A-"); break;
                case 0x10: mods.Append("S-"); break;
                case 0x5B: mods.Append("M-"); break;
                default: main = KeyName(vk); if (main is null) return null; break;
            }
        }
        return main is null ? null : mods + main;
    }

    /// <summary>
    /// Text as a keyd macro, one physical key per character so it types correctly on the real layout
    /// (keyd itself would assume US). Supports what the layout has on its unshifted keys, plus capitals.
    /// </summary>
    public static string? Text(string text) => Text(text, KeyboardLayout.Current);

    internal static string? Text(string text, KeyboardLayout layout)
    {
        if (text.Length == 0) return null;
        var parts = new List<string>();
        foreach (var c in text)
        {
            if (c == ' ') { parts.Add("space"); continue; }
            var upper = char.ToUpperInvariant(c);
            if (layout.ScanForLabel(upper.ToString()) is not { } scan) return null;
            if (KeyName(KeyboardLayout.UsVkForScan(scan)) is not { } name) return null;
            var shifted = char.IsLetter(c) && char.IsUpper(c);
            if (!char.IsLetter(c) && c != upper) return null;
            parts.Add(shifted ? "S-" + name : name);
        }
        return $"macro({string.Join(' ', parts)})";
    }

    private static string Layer(KeyMapping m)
    {
        var layers = new List<string>();
        if (m.Ctrl) layers.Add("control");
        if (m.Alt) layers.Add("alt");
        if (m.Shift) layers.Add("shift");
        if (m.Win) layers.Add("meta");
        return layers.Count == 0 ? "main" : string.Join('+', layers);
    }

    /// <summary>
    /// Builds the keyd configuration. Mappings that keyd cannot express (programs, special
    /// characters in texts) are listed as comments so nothing is silently lost.
    /// </summary>
    public static string Generate(KeyRemapSettings settings, bool lockWindowsKey)
    {
        var layers = new SortedDictionary<string, List<string>>(StringComparer.Ordinal) { ["main"] = [] };
        var skipped = new List<string>();
        if (lockWindowsKey)
        {
            layers["main"].Add("leftmeta = noop");
            layers["main"].Add("rightmeta = noop");
        }
        if (settings.Enabled)
        {
            foreach (var m in settings.Mappings.Where(m => m.Enabled && m.Key != 0))
            {
                var key = KeyName(m.Key);
                var label = Services.KeyRemapService.Describe(m);
                if (key is null) { skipped.Add($"{label}: key not supported"); continue; }
                string? action = m.Action switch
                {
                    KeyActionType.Shortcut => Shortcut(m.Value),
                    KeyActionType.Text => Text(m.Value),
                    KeyActionType.Disable => "noop",
                    _ => null,
                };
                if (action is null)
                {
                    skipped.Add(m.Action == KeyActionType.Launch
                        ? $"{label}: start \"{m.Value}\" – use your desktop's keyboard shortcut settings (keyd runs commands as root)"
                        : $"{label}: \"{m.Value}\" cannot be expressed in keyd");
                    continue;
                }
                var layer = Layer(m);
                if (!layers.TryGetValue(layer, out var list)) layers[layer] = list = [];
                list.Add($"{key} = {action}");
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Generated by ClevoControl – key mapping for keyd (https://github.com/rvaiya/keyd)");
        sb.AppendLine($"# Install: sudo cp <this file> {InstallPath} && sudo keyd reload");
        sb.AppendLine($"# Keyboard layout: {KeyboardLayout.Current.DisplayName} (keys are matched by position, so this file works with any layout).");
        foreach (var s in skipped) sb.AppendLine("# not converted: " + s);
        sb.AppendLine();
        sb.AppendLine("[ids]");
        sb.AppendLine("*");
        foreach (var (layer, lines) in layers)
        {
            if (lines.Count == 0 && layer != "main") continue;
            sb.AppendLine();
            sb.AppendLine($"[{layer}]");
            foreach (var l in lines) sb.AppendLine(l);
        }
        return sb.ToString();
    }
}
