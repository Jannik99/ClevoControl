namespace ClevoControl.Core.Hardware;

/// <summary>
/// Talks to a Clevo laptop through InsydeDCHU.dll. Command numbers are documented in docs/PROTOCOL.md.
/// </summary>
public sealed class ClevoHardware : IClevoHardware
{
    // DCHU command ids (same numbering as the ACPI WMBB/_DSM methods).
    internal const int CmdGetFanInfo = 0x0C;       // buffer: fan rpm / duty / temps
    internal const int CmdGetSpecs = 0x0D;         // buffer: hardware capabilities
    internal const int CmdKeyboardRgb = 0x67;      // 103: keyboard color / brightness / effects
    internal const int CmdFanDuty = 0x68;          // 104: direct fan duty (one byte per fan)
    internal const int CmdOption = 0x79;           // 121: option setter, sub-command in the high byte

    internal const uint KbZoneBase = 0xF0000000;   // + zone << 24, payload 0x00BBRRGG
    internal const uint KbBrightness = 0xF4000000; // + level (0..255)
    internal const uint KbOn = 0xE007F001;
    internal const uint KbOff = 0xE0003001;

    internal const uint OptFanMode = 0x01;         // value: 0 auto, 1 max
    internal const uint OptPerformanceProfile = 0x19;

    private readonly DchuNative _dchu;

    public ClevoHardware(DchuNative dchu)
    {
        _dchu = dchu;
        KeyboardType = DetectKeyboardType();
    }

    public bool IsReal => true;

    public string Description => $"Clevo DCHU ({_dchu.LibraryPath})";

    public KeyboardType KeyboardType { get; }

    private KeyboardType DetectKeyboardType()
    {
        try
        {
            var spec = _dchu.GetBuffer(CmdGetSpecs);
            return spec[0x0F] switch
            {
                0x00 => KeyboardType.None,
                0x01 => KeyboardType.WhiteOnly,
                0x02 => KeyboardType.ThreeZoneRgb,
                0x06 => KeyboardType.SingleZoneRgb,
                0xF3 => KeyboardType.PerKeyRgb,
                _ => KeyboardType.Unknown,
            };
        }
        catch
        {
            return KeyboardType.Unknown;
        }
    }

    public (FanReading Cpu, FanReading Gpu, FanReading? Gpu2) ReadFans()
    {
        var b = _dchu.GetBuffer(CmdGetFanInfo);
        static int Rpm(byte hi, byte lo)
        {
            var raw = (hi << 8) | lo;
            return raw <= 0 ? 0 : (int)Math.Round(2_156_250.0 / raw);
        }
        static int Pct(byte duty) => (int)Math.Round(duty * 100 / 255.0);

        var cpu = new FanReading(Rpm(b[2], b[3]), Pct(b[16]), b[18]);
        var gpu = new FanReading(Rpm(b[4], b[5]), Pct(b[19]), b[21]);
        FanReading? gpu2 = (b[6] | b[7] | b[22] | b[24]) != 0 ? new FanReading(Rpm(b[6], b[7]), Pct(b[22]), b[24]) : null;
        return (cpu, gpu, gpu2);
    }

    public void SetKeyboardColor(RgbColor color)
    {
        switch (KeyboardType)
        {
            case KeyboardType.ThreeZoneRgb:
                for (var z = 0; z < 3; z++) SetKeyboardZoneColor(z, color);
                break;
            default:
                SetKeyboardZoneColor(0, color);
                break;
        }
    }

    public void SetKeyboardZoneColor(int zone, RgbColor c)
    {
        zone = Math.Clamp(zone, 0, 3);
        // Payload byte order is B, R, G (from most to least significant).
        var arg = KbZoneBase | ((uint)zone << 24) | ((uint)c.B << 16) | ((uint)c.R << 8) | c.G;
        _dchu.Set(CmdKeyboardRgb, arg);
    }

    public void SetKeyboardBrightness(byte level) => _dchu.Set(CmdKeyboardRgb, KbBrightness | level);

    public void SetKeyboardPower(bool on) => _dchu.Set(CmdKeyboardRgb, on ? KbOn : KbOff);

    public void SetKeyboardHardwareEffect(HardwareEffect effect)
    {
        uint arg = effect switch
        {
            HardwareEffect.Breathe => 0x1002A000,
            HardwareEffect.Cycle => 0x33010000,
            HardwareEffect.Dance => 0x80000000,
            HardwareEffect.Flash => 0xA0000000,
            HardwareEffect.RandomColor => 0x70000000,
            HardwareEffect.Tempo => 0x90000000,
            HardwareEffect.Wave => 0xB0000000,
            _ => 0,
        };
        _dchu.Set(CmdKeyboardRgb, arg);
    }

    public void SetFanMode(FanMode mode)
    {
        switch (mode)
        {
            case FanMode.Max:
                SetOption(OptFanMode, 1);
                break;
            default:
                // Switching to "auto" while the EC already believes it is in auto mode does not
                // cancel a directly written duty. Going through "max" first resets it reliably.
                SetOption(OptFanMode, 1);
                Thread.Sleep(150);
                SetOption(OptFanMode, 0);
                break;
        }
    }

    public void SetFanDuty(byte cpu, byte gpu1, byte gpu2)
    {
        var arg = cpu | ((uint)gpu1 << 8) | ((uint)gpu2 << 16);
        _dchu.Set(CmdFanDuty, arg);
    }

    // The EC value equals Clevo's UI index: 0 Quiet, 1 Power saving, 2 Performance, 3 Entertainment.
    // Verified from Clevo's Fn-key app, which sends ReadAppSettings(1,1) unchanged as option 0x19
    // (only Performance may get extra flags: 0x80 = turbo fan, 0x40 = an unknown model option).
    // NOTE: v0.1.0 used a wrong, permuted mapping (Performance was sent as Quiet).
    internal static uint ProfileToEc(PerformanceProfile p) => p switch
    {
        PerformanceProfile.Quiet => 0,
        PerformanceProfile.PowerSaving => 1,
        PerformanceProfile.Performance => 2,
        _ => 3, // Entertainment
    };

    public void SetPerformanceProfile(PerformanceProfile profile)
    {
        SetOption(OptPerformanceProfile, ProfileToEc(profile));
        try
        {
            // Keep Clevo's own Control Center in sync so it does not re-apply an old mode.
            _dchu.WriteAppSettings(1, 1, [(byte)profile]);
        }
        catch { /* best effort */ }
    }

    public PerformanceProfile? ReadStoredPerformanceProfile()
    {
        try
        {
            var v = _dchu.ReadAppSettings(1, 1, 1)[0];
            return v <= 3 ? (PerformanceProfile)v : null;
        }
        catch
        {
            return null;
        }
    }

    public byte[] ReadSpecBuffer() => _dchu.GetBuffer(CmdGetSpecs);

    public byte[] ReadFanBuffer() => _dchu.GetBuffer(CmdGetFanInfo);

    private void SetOption(uint sub, uint value) => _dchu.Set(CmdOption, (sub << 24) | (value & 0x00FFFFFF));
}
