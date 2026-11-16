using ClevoControl.Core.Hardware.PerKey;

namespace ClevoControl.Core.Hardware;

/// <summary>Everything the app needs from the laptop's embedded controller.</summary>
public interface IClevoHardware
{
    /// <summary>True when talking to real hardware, false in simulation mode.</summary>
    bool IsReal { get; }

    string Description { get; }

    KeyboardType KeyboardType { get; }

    /// <summary>
    /// The per-key controller, only when one was really detected (then <see cref="KeyboardType"/> is
    /// <see cref="KeyboardType.PerKeyRgb"/>). The keyboard calls below then also go to it.
    /// </summary>
    IPerKeyKeyboard? PerKey => null;

    /// <summary>Reads fan speed, duty cycle and temperature of CPU/GPU from the EC.</summary>
    (FanReading Cpu, FanReading Gpu, FanReading? Gpu2) ReadFans();

    /// <summary>Sets the whole keyboard (all zones) to one color. Values are sent as-is.</summary>
    void SetKeyboardColor(RgbColor color);

    /// <summary>Sets one zone (0 = left/single, 1 = center, 2 = right) to a color.</summary>
    void SetKeyboardZoneColor(int zone, RgbColor color);

    /// <summary>Hardware brightness 0..255.</summary>
    void SetKeyboardBrightness(byte level);

    void SetKeyboardPower(bool on);

    void SetKeyboardHardwareEffect(HardwareEffect effect);

    void SetFanMode(FanMode mode);

    /// <summary>Sets fan duty directly (0..255 per fan). Only use with a safety net, see FanController.</summary>
    void SetFanDuty(byte cpu, byte gpu1, byte gpu2);

    void SetPerformanceProfile(PerformanceProfile profile);

    /// <summary>Reads the profile stored by Clevo's Control Center (may be null if unknown).</summary>
    PerformanceProfile? ReadStoredPerformanceProfile();

    /// <summary>FlexiCharger state, or null if the model does not report support.</summary>
    ChargeLimitInfo? ReadChargeLimit();

    /// <summary>Sets FlexiCharger. Only call with values from <see cref="ChargeLimitInfo"/> options.</summary>
    void SetChargeLimit(bool enabled, int start, int stop);

    /// <summary>Raw spec buffer (EC command 0x0D) for diagnostics.</summary>
    byte[] ReadSpecBuffer();

    /// <summary>Raw fan buffer (EC command 0x0C) for diagnostics.</summary>
    byte[] ReadFanBuffer();
}
