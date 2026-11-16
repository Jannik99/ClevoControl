using ClevoControl.Core.Services;
using Xunit;

namespace ClevoControl.Tests;

public class VendorAutostartTests
{
    [Theory]
    [InlineData("C:\\Program Files\\WindowsApps\\CLEVOCO.ControlCenter3.0_3.92.0.0_x64__6h6z29zh29qx0\\ControlCenter30.exe")]
    [InlineData("CLEVOCO.FnhotkeysandOSD_6h6z29zh29qx0")]
    [InlineData("C:\\WINDOWS\\System32\\DriverStore\\FileRepository\\acpibridge1.inf_amd64_46c2643f8c307edd\\DCHUService.exe")]
    [InlineData("C:\\Program Files (x86)\\ControlCenter\\Driver\\x64\\HKClipSvc.exe")]
    [InlineData("OBSIDIAN Control Center")]
    [InlineData("LedKeyboardSetting.exe")]
    [InlineData("FnKey.exe")]
    [InlineData("/usr/bin/tccd")]
    [InlineData("tuxedo-control-center-tray.desktop")]
    public void Matches_vendor_software(string text) => Assert.True(VendorAutostart.IsVendor(text));

    [Theory]
    [InlineData("C:\\Users\\x\\ClevoControl\\ClevoControl.exe")]
    [InlineData("ClevoControl")]
    [InlineData("RivetNetworks.KillerControlCenter_rh07ty8m5nkag")]
    [InlineData("C:\\Program Files\\Obsidian\\Obsidian.exe")]
    [InlineData("OneDrive")]
    [InlineData("deadbeefcc30aa")] // no word boundary: part of a hash
    [InlineData("")]
    public void Ignores_other_software(string text) => Assert.False(VendorAutostart.IsVendor(text));

    [Fact]
    public void Own_entry_is_never_vendor_even_with_vendor_words()
    {
        // ClevoControl's own .desktop file mentions TUXEDO in its keywords.
        Assert.False(VendorAutostart.IsVendor("clevocontrol.desktop", "[Desktop Entry]\nName=ClevoControl\nKeywords=Clevo;TUXEDO;"));
    }

    [Fact]
    public void Hotkey_service_gets_a_warning()
    {
        var (_, warning) = VendorAutostart.Describe("CCDCHUService", "DCHUService.exe");
        Assert.NotNull(warning);
        Assert.Null(VendorAutostart.Describe("LedKeyboardSetting").Warning);
    }

    [Fact]
    public void Approved_values_match_task_manager_format()
    {
        var now = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var off = VendorAutostart.ApprovedValue(false, now);
        Assert.Equal(12, off.Length);
        Assert.Equal(0x03, off[0]);
        Assert.Equal(now.ToFileTimeUtc(), BitConverter.ToInt64(off, 4));
        Assert.Equal(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, VendorAutostart.ApprovedValue(true, now));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(new byte[0], true)]
    [InlineData(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 3, 0, 0, 0, 0x0C, 0xDE, 0x92, 0x86, 0x97, 0x0F, 0xDB, 0x01 }, false)]
    [InlineData(new byte[] { 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    public void Reads_approved_values(byte[]? value, bool enabled) => Assert.Equal(enabled, VendorAutostart.IsApprovedEnabled(value));

    [Fact]
    public void Parses_schtasks_xml_output()
    {
        const string output = """
            <Tasks>
            <!-- \Clevo\StartCC -->
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>
              <Settings>
                <IdleSettings><StopOnIdleEnd>true</StopOnIdleEnd></IdleSettings>
                <Enabled>false</Enabled>
              </Settings>
              <Actions Context="Author"><Exec><Command>"C:\Program Files\ControlCenter\CC30.exe"</Command><Arguments>-tray</Arguments></Exec></Actions>
            </Task>
            <!-- \Other -->
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2"><Settings><Enabled>true</Enabled></Settings>
              <Actions><Exec><Command>C:\x &amp; y\a.exe</Command></Exec></Actions></Task>
            <!-- \ComOnly -->
            <Task version="1.2"><Actions><ComHandler><ClassId>{1}</ClassId></ComHandler></Actions></Task>
            </Tasks>
            """;
        var tasks = VendorAutostart.ParseTaskList(output);
        Assert.Equal(2, tasks.Count);
        Assert.Equal(@"\Clevo\StartCC", tasks[0].Path);
        Assert.False(tasks[0].Enabled);
        Assert.Equal("-tray", tasks[0].Arguments);
        Assert.True(VendorAutostart.IsVendor(tasks[0].Path, tasks[0].Command));
        Assert.True(tasks[1].Enabled);
        Assert.Equal(@"C:\x & y\a.exe", tasks[1].Command);
        Assert.False(VendorAutostart.IsVendor(tasks[1].Path, tasks[1].Command));
    }

    [Theory]
    [InlineData("@oem156.inf,%AcpiBridge1.SVCDESC%;Control Center Hotkey Service", "Control Center Hotkey Service")]
    [InlineData("HotKey Clipboard Service", "HotKey Clipboard Service")]
    [InlineData(null, "Fallback")]
    [InlineData("@oem1.inf,%X%", "Fallback")]
    public void Service_display_names(string? raw, string expected) => Assert.Equal(expected, VendorAutostart.ServiceDisplayName(raw, "Fallback"));

    [Theory]
    [InlineData("enabled\n", true)]
    [InlineData("static", true)]
    [InlineData("disabled\n", false)]
    [InlineData("masked", false)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("not-found", null)]
    public void Parses_systemctl_is_enabled(string? output, bool? expected) => Assert.Equal(expected, VendorAutostart.ParseIsEnabled(output));

    private const string TccEntry = "[Desktop Entry]\nType=Application\nName=TUXEDO Control Center Tray\nExec=tuxedo-control-center --tray\nX-GNOME-Autostart-enabled=true\n\n[Desktop Action New]\nHidden=false\n";

    [Fact]
    public void Desktop_entry_autostart_state()
    {
        Assert.True(VendorAutostart.DesktopAutostarts(TccEntry));
        Assert.False(VendorAutostart.DesktopAutostarts("[Desktop Entry]\nHidden=true\n"));
        Assert.False(VendorAutostart.DesktopAutostarts("[Desktop Entry]\nX-GNOME-Autostart-enabled=false\n"));
        // Keys outside [Desktop Entry] do not count.
        Assert.True(VendorAutostart.DesktopAutostarts("[Desktop Entry]\nName=x\n[Other]\nHidden=true\n"));
        Assert.Equal("TUXEDO Control Center Tray", VendorAutostart.DesktopName(TccEntry));
    }

    [Fact]
    public void Desktop_entry_edit_round_trip()
    {
        var off = VendorAutostart.DesktopSetAutostart(TccEntry, false);
        Assert.False(VendorAutostart.DesktopAutostarts(off));
        Assert.Contains("Hidden=false", off); // the action group is untouched
        Assert.True(off.IndexOf("Hidden=true", StringComparison.Ordinal) < off.IndexOf("[Desktop Action New]", StringComparison.Ordinal));
        var on = VendorAutostart.DesktopSetAutostart(off, true);
        Assert.True(VendorAutostart.DesktopAutostarts(on));
        Assert.DoesNotContain("Hidden=true", on);
        Assert.Contains("Exec=tuxedo-control-center --tray", on);

        var gnomeOff = VendorAutostart.DesktopSetAutostart("[Desktop Entry]\nName=x\nX-GNOME-Autostart-enabled=false", true);
        Assert.True(VendorAutostart.DesktopAutostarts(gnomeOff));
    }

    [Fact]
    public void Override_is_recognised_and_hides_the_entry()
    {
        var o = VendorAutostart.DesktopOverride("TUXEDO Control Center Tray");
        Assert.True(VendorAutostart.IsOurOverride(o));
        Assert.False(VendorAutostart.DesktopAutostarts(o));
        Assert.False(VendorAutostart.IsOurOverride(TccEntry));
    }
}
