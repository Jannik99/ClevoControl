using ClevoControl.Core.Platform;
using Xunit;

namespace ClevoControl.Tests;

/// <summary>Keyboard layout tables, shapes and detection parsers (run on every OS).</summary>
public class KeyboardLayoutTests
{
    [Theory]
    [InlineData("de")]
    [InlineData("us")]
    [InlineData("fr")]
    [InlineData("gb")]
    [InlineData("nordic")]
    public void Every_row_has_the_same_width(string id)
    {
        var rows = KeyboardLayout.ForTests(id).MainRows();
        Assert.Equal(6, rows.Count);
        foreach (var row in rows) Assert.Equal(16, row.Sum(k => k.Width), 3);
    }

    [Fact]
    public void German_layout_is_qwertz_iso()
    {
        var de = KeyboardLayout.ForTests("de");
        Assert.True(de.Iso);
        // Physical US "Y" position (scan 0x15) shows Z on QWERTZ; without Windows the code is the US one.
        Assert.Equal("Z", de.KeyLabel('Y'));
        Assert.Equal("Y", de.KeyLabel('Z'));
        Assert.Equal("Ü", de.KeyLabel(0xDB));
        Assert.Equal("<", de.KeyLabel(0xE2));
        Assert.Equal("Entf", de.KeyLabel(0x2E));
        Assert.Contains(de.MainRows()[4], k => k.Label == "<");           // extra key next to left Shift
        Assert.Contains(de.MainRows()[3], k => k is { Vk: 0x0D, Label: "" }); // tall Enter reaches into the home row
    }

    [Fact]
    public void Us_layout_is_ansi_with_english_legends()
    {
        var us = KeyboardLayout.ForTests("us");
        Assert.False(us.Iso);
        Assert.DoesNotContain(us.MainRows()[4], k => k.Vk == 0xE2);
        Assert.Equal("Del", us.KeyLabel(0x2E));
        Assert.Equal("Y", us.KeyLabel('Y'));
        Assert.Equal("Num .", us.KeyLabel(0x6E));
    }

    [Fact]
    public void Azerty_labels_and_lookup_by_legend()
    {
        var fr = KeyboardLayout.ForTests("fr");
        Assert.Equal("A", fr.KeyLabel('Q'));
        Assert.Equal('Q', fr.VkForLabel("A"));
        Assert.Equal("macro(q w)", KeydConfig.Text("az", fr));     // keyd needs the physical keys
        Assert.Equal("macro(S-q)", KeydConfig.Text("A", fr));
        Assert.Null(KeydConfig.Text("!?", KeyboardLayout.ForTests("de")));
    }

    [Theory]
    [InlineData("de", "de")]
    [InlineData("de(nodeadkeys)", "de")]
    [InlineData("de+nodeadkeys", "de")]
    [InlineData("us,de", "us")]
    [InlineData("at", "de")]
    [InlineData("fi", "nordic")]
    [InlineData("ru", null)]
    public void Maps_xkb_layout_codes(string code, string? expected) => Assert.Equal(expected, KeyboardLayout.MapCode(code));

    [Theory]
    [InlineData(0x0407, "de")]
    [InlineData(0x0409, "us")]
    [InlineData(0x0809, "gb")]
    [InlineData(0x0807, "ch")]
    [InlineData(0x040C, "fr")]
    [InlineData(0x041D, "nordic")]
    public void Maps_windows_layout_languages(int lang, string expected) => Assert.Equal(expected, KeyboardLayout.MapCulture(lang));

    [Fact]
    public void Parses_desktop_layout_settings()
    {
        Assert.Equal("de+nodeadkeys", KeyboardLayout.ParseGnomeSources("[('xkb', 'de+nodeadkeys'), ('xkb', 'us')]"));
        Assert.Null(KeyboardLayout.ParseGnomeSources("@a(ss) []"));
        Assert.Equal("de", KeyboardLayout.ParseColonValue("   System Locale: LANG=de_DE.UTF-8\n       X11 Layout: de\n        X11 Model: pc105\n", "X11 Layout"));
        Assert.Equal("fr", KeyboardLayout.ParseColonValue("rules:      evdev\nmodel:      pc105\nlayout:     fr\n", "layout"));
        Assert.Equal("de", KeyboardLayout.ParseIni("XKBMODEL=\"pc105\"\nXKBLAYOUT=\"de\"\n", "XKBLAYOUT"));
        Assert.Equal("us,de", KeyboardLayout.ParseIni("[Layout]\nLayoutList=us,de\nUse=true\n", "LayoutList"));
    }

    [Fact]
    public void Iso_enter_path_is_closed_and_culture_invariant()
    {
        var p = KeyboardLayout.IsoEnterPath(110.5, 95, 44, 4, 6);
        Assert.StartsWith("M 6,0 H 104.5", p);
        Assert.EndsWith("Z", p);
        Assert.DoesNotContain(";", p);
    }
}
