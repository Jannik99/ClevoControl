using ClevoControl.Core.Platform;

namespace ClevoControl.Core.Hardware.PerKey;

/// <summary>
/// A keyboard with one RGB LED per key. Only created when a known per-key controller was really found
/// (see <see cref="WindowsHidPerKey"/>, <see cref="LinuxSysfsPerKey"/>); <see cref="IClevoHardware.PerKey"/> is null otherwise.
/// </summary>
public interface IPerKeyKeyboard
{
    PerKeyController Controller { get; }

    /// <summary>For logs and the system page, e.g. "ITE 8291 (USB 048D:600B rev 0003), Windows HID".</summary>
    string Description { get; }

    /// <summary>True when the key (id from <see cref="KeyIds"/>) has an LED on this controller and keyboard shape.</summary>
    bool HasLed(int keyId);

    /// <summary>Shows these colors (by key id); keys without an entry are dark.</summary>
    void SetKeyColors(IReadOnlyDictionary<int, RgbColor> colors);

    /// <summary>Every LED of the matrix in one color (also LEDs without a known key).</summary>
    void SetAll(RgbColor color);

    /// <summary>Brightness 0..255 (scaled to the controller's range).</summary>
    void SetBrightness(byte level);

    void SetPower(bool on);

    /// <summary>Starts an effect computed by the controller itself, if it has one for <paramref name="effect"/>.</summary>
    void SetHardwareEffect(HardwareEffect effect, RgbColor color);

    /// <summary>Forces the next write to send everything again (e.g. after resume).</summary>
    void Invalidate();
}

/// <summary>
/// Keeps the LED frame, brightness and power state and only writes what changed; the transport (Windows HID,
/// Linux sysfs, simulation) is implemented by subclasses.
/// </summary>
public abstract class PerKeyKeyboardBase : IPerKeyKeyboard
{
    private RgbColor[]? _sent;
    private bool _force = true;

    protected PerKeyKeyboardBase(PerKeyController controller)
    {
        Controller = controller;
        Frame = new RgbColor[IteKeyMap.LedCount(controller)];
    }

    public PerKeyController Controller { get; }

    public abstract string Description { get; }

    /// <summary>The LED frame last written (linear index = row × columns + column).</summary>
    public RgbColor[] Frame { get; private set; }

    /// <summary>Brightness 0..255 as set by the app.</summary>
    public byte Brightness { get; private set; } = 255;

    public bool On { get; private set; } = true;

    /// <summary>ISO or ANSI shape decides where Enter and the keys around it are (see <see cref="IteKeyMap.Positions"/>).</summary>
    protected virtual bool Iso => KeyboardLayout.Current.Iso;

    public bool HasLed(int keyId) => IteKeyMap.HasLed(Controller, keyId, Iso);

    public void SetKeyColors(IReadOnlyDictionary<int, RgbColor> colors) => Apply(IteKeyMap.BuildFrame(Controller, colors, Iso));

    public void SetAll(RgbColor color)
    {
        var frame = new RgbColor[Frame.Length];
        Array.Fill(frame, color);
        Apply(frame);
    }

    private void Apply(RgbColor[] frame)
    {
        var changed = new bool[frame.Length];
        var any = false;
        for (var i = 0; i < frame.Length; i++)
        {
            changed[i] = _force || _sent is null || _sent[i] != frame[i];
            any |= changed[i];
        }
        Frame = frame;
        if (!any || !On) return;
        var force = _force;
        _force = true; // stays set if the write throws, so the next attempt sends everything
        ActiveHardwareEffect = null; // writing the frame switches the controller back to per-key mode
        WriteFrame(frame, changed, force);
        _force = false;
        _sent = (RgbColor[])frame.Clone();
    }

    /// <summary>
    /// The controller's built-in effect while it runs (set by <see cref="SetHardwareEffect"/>, cleared as soon as a
    /// per-key frame is written). Transports use it to change the brightness without leaving the effect.
    /// </summary>
    protected (HardwareEffect Effect, RgbColor Color)? ActiveHardwareEffect { get; private set; }

    // True until the current Brightness reached the controller (also after a failed write, so it is retried).
    private bool _brightnessPending = true;

    public void SetBrightness(byte level)
    {
        if (level == Brightness && !_force && !_brightnessPending) return;
        Brightness = level; // the transports read it while writing
        _brightnessPending = true;
        if (!On) return; // SetPower(true) sends it
        WriteBrightness(level);
        _brightnessPending = false;
    }

    public void SetPower(bool on)
    {
        On = on;
        if (on)
        {
            _brightnessPending = true;
            WriteBrightness(Brightness);
            _brightnessPending = false;
            _force = true;
            Apply(Frame);
        }
        else
        {
            ActiveHardwareEffect = null; // switching off ends a running effect
            WriteOff();
        }
    }

    public void SetHardwareEffect(HardwareEffect effect, RgbColor color)
    {
        _force = true; // per-key mode must be entered again before the next frame
        WriteHardwareEffect(effect, color);
        ActiveHardwareEffect = (effect, color);
    }

    public void Invalidate() => _force = true;

    /// <summary>Writes the frame; <paramref name="changed"/> marks LEDs that differ from the last write, <paramref name="all"/> means everything must be sent.</summary>
    protected abstract void WriteFrame(RgbColor[] frame, bool[] changed, bool all);

    protected abstract void WriteBrightness(byte level);

    protected abstract void WriteOff();

    protected abstract void WriteHardwareEffect(HardwareEffect effect, RgbColor color);
}

/// <summary>Per-key keyboard for <c>--simulate</c> with <c>CC_SIM_KEYBOARD=perkey</c> (UI tests); behaves like an ITE 8291.</summary>
public sealed class SimulatedPerKey() : PerKeyKeyboardBase(PerKeyController.Ite8291)
{
    public override string Description => "Simulated ITE 8291 per-key keyboard";

    public int Writes { get; private set; }

    /// <summary>Brightness levels that reached the (simulated) controller, in order.</summary>
    public List<byte> BrightnessWrites { get; } = [];

    /// <summary>Number of times the built-in effect was (re)started, including brightness changes while it runs.</summary>
    public int EffectWrites { get; private set; }

    /// <summary>Test hook: the next write throws like a failing USB transfer.</summary>
    public bool FailNextWrite { get; set; }

    public bool HardwareEffectRunning => ActiveHardwareEffect is not null;

    private void MaybeFail()
    {
        if (!FailNextWrite) return;
        FailNextWrite = false;
        throw new IOException("Simulated HID write failure");
    }

    protected override void WriteFrame(RgbColor[] frame, bool[] changed, bool all)
    {
        MaybeFail();
        Writes++;
    }

    protected override void WriteBrightness(byte level)
    {
        MaybeFail();
        // Like the ITE 8291: the brightness is part of the effect command while an effect runs.
        if (ActiveHardwareEffect is not null) EffectWrites++;
        BrightnessWrites.Add(level);
    }

    protected override void WriteOff() => MaybeFail();

    protected override void WriteHardwareEffect(HardwareEffect effect, RgbColor color)
    {
        MaybeFail();
        EffectWrites++;
    }
}
