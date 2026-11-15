using System.Globalization;
using Material.Icons;

namespace ClevoControl.Desktop;

/// <summary>Tiny two-language localisation: T("Deutsch", "English").</summary>
public static class Loc
{
    public static bool German { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";

    public static void Init(string setting) =>
        German = setting switch
        {
            "de" => true,
            "en" => false,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de",
        };

    public static string T(string de, string en) => German ? de : en;
}

/// <summary>
/// Icon names shared with the Windows app. The values are Segoe Fluent Icons code points (they are
/// also stored in settings, e.g. scene icons), mapped to Material Design icons for rendering here.
/// </summary>
public static class Icons
{
    public const string Home = "", Settings = "", Game = "", Keyboard = "", Color = "",
        Brightness = "", Lightning = "", Battery = "", BatterySaver = "", Volume = "", Mute = "",
        Microphone = "", Speaker = "", Star = "", Music = "", Video = "", Power = "",
        Tune = "", Timer = "", Heart = "", Thermometer = "", Gauge = "", Lamp = "",
        Bulb = "", Shuffle = "", Pulse = "", Diagnostic = "", Monitor = "", Chip = "",
        Laptop = "", Swap = "", Bell = "", Layers = "", Frame = "", Fire = "",
        Chart = "", Puzzle = "", History = "", Info = "", Warning = "", Add = "",
        Delete = "", Edit = "", Up = "", Refresh = "", OpenExternal = "", Broadcast = "",
        Circle = "", Sparkle = "", Plug = "", Moon = "", Shield = "", Target = "",
        Code = "", Save = "", Fan = "", Max = "", Auto = "", Briefcase = "", Brush = "";

    private static readonly Dictionary<string, MaterialIconKind> Map = BuildMap();

    private static Dictionary<string, MaterialIconKind> BuildMap()
    {
        var d = new Dictionary<string, MaterialIconKind>();
        void A(string glyph, MaterialIconKind kind) => d.TryAdd(glyph, kind);
        A(Home, MaterialIconKind.HomeOutline); A(Settings, MaterialIconKind.CogOutline); A(Game, MaterialIconKind.GamepadVariantOutline);
        A(Keyboard, MaterialIconKind.KeyboardOutline); A(Color, MaterialIconKind.PaletteOutline); A(Brightness, MaterialIconKind.Brightness6);
        A(Lightning, MaterialIconKind.LightningBoltOutline); A(Battery, MaterialIconKind.BatteryOutline); A(BatterySaver, MaterialIconKind.BatteryHeartVariant);
        A(Volume, MaterialIconKind.VolumeHigh); A(Mute, MaterialIconKind.VolumeOff); A(Microphone, MaterialIconKind.MicrophoneOutline);
        A(Speaker, MaterialIconKind.Speaker); A(Star, MaterialIconKind.StarOutline); A(Music, MaterialIconKind.Music);
        A(Video, MaterialIconKind.VideoOutline); A(Power, MaterialIconKind.Power); A(Tune, MaterialIconKind.Tune);
        A(Timer, MaterialIconKind.TimerOutline); A(Heart, MaterialIconKind.HeartOutline); A(Thermometer, MaterialIconKind.Thermometer);
        A(Gauge, MaterialIconKind.Speedometer); A(Lamp, MaterialIconKind.LampOutline); A(Bulb, MaterialIconKind.LightbulbOutline);
        A(Shuffle, MaterialIconKind.Shuffle); A(Pulse, MaterialIconKind.Pulse); A(Diagnostic, MaterialIconKind.Pulse);
        A(Monitor, MaterialIconKind.Monitor); A(Chip, MaterialIconKind.Chip); A(Laptop, MaterialIconKind.Laptop);
        A(Swap, MaterialIconKind.SwapHorizontal); A(Bell, MaterialIconKind.BellOutline); A(Layers, MaterialIconKind.LayersOutline);
        A(Frame, MaterialIconKind.CropFree); A(Fire, MaterialIconKind.Fire); A(Chart, MaterialIconKind.ChartBar);
        A(Puzzle, MaterialIconKind.PuzzleOutline); A(History, MaterialIconKind.History); A(Info, MaterialIconKind.InformationOutline);
        A(Warning, MaterialIconKind.AlertOutline); A(Add, MaterialIconKind.Plus); A(Delete, MaterialIconKind.DeleteOutline);
        A(Edit, MaterialIconKind.PencilOutline); A(Up, MaterialIconKind.ArrowUp); A(Refresh, MaterialIconKind.Refresh);
        A(OpenExternal, MaterialIconKind.OpenInNew); A(Broadcast, MaterialIconKind.AccessPoint); A(Circle, MaterialIconKind.CircleOutline);
        A(Sparkle, MaterialIconKind.CreationOutline); A(Plug, MaterialIconKind.PowerPlug); A(Moon, MaterialIconKind.WeatherNight);
        A(Shield, MaterialIconKind.ShieldOutline); A(Target, MaterialIconKind.Target); A(Code, MaterialIconKind.CodeBraces);
        A(Save, MaterialIconKind.ContentSaveOutline); A(Fan, MaterialIconKind.Fan); A(Max, MaterialIconKind.ArrowExpand);
        A(Auto, MaterialIconKind.Cogs); A(Briefcase, MaterialIconKind.BriefcaseOutline); A(Brush, MaterialIconKind.BrushOutline);
        A("", MaterialIconKind.Check); // check mark used by the quick-tile editor
        return d;
    }

    /// <summary>Material icon for a glyph, or null for plain text (emoji, letters).</summary>
    public static MaterialIconKind? Kind(string glyph) => Map.TryGetValue(glyph, out var k) ? k : null;
}
