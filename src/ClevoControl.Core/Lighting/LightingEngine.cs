using System.Diagnostics;
using ClevoControl.Core.Games;
using ClevoControl.Core.Hardware;
using ClevoControl.Core.Native;
using ClevoControl.Core.Settings;

namespace ClevoControl.Core.Lighting;

/// <summary>
/// Runs the selected keyboard effect on a background thread and only talks to the EC when
/// the resulting color actually changes.
/// </summary>
public sealed class LightingEngine : IDisposable
{
    private readonly IClevoHardware _hw;
    private readonly Func<SensorSnapshot> _sensors;
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Random _rng = new();
    private volatile KeyboardSettings _settings;
    private volatile bool _running = true;
    private volatile bool _invalidate = true;
    private DateTime _lastTick = DateTime.UtcNow;
    private DateTime _gapHoldUntil;

    /// <summary>
    /// A pause of the loop this long (wall clock) means the system was asleep. Some laptops (Modern
    /// Standby) do not report suspend/resume to the app, while the firmware still resets the keyboard to a
    /// solid colour – so a gap is treated like a resume: wait for the EC, then send everything again.
    /// </summary>
    internal static readonly TimeSpan SleepGap = TimeSpan.FromSeconds(15);

    internal static readonly TimeSpan ResumeSettle = TimeSpan.FromSeconds(10);

    /// <summary>Pure decision for the gap check (tests): returns the time until which output is held, or null.</summary>
    internal static DateTime? GapHold(DateTime last, DateTime now) => now - last > SleepGap ? now + ResumeSettle : null;

    private ScreenSampler? _screen;
    private RegionSampler? _region;
    private readonly AudioAnalyzer _audio = new();
    private List<MonitorInfo> _monitors = [];
    private DateTime _monitorsRefreshed;

    private RgbColor? _lastSent;
    private byte? _lastBrightness;
    private bool? _lastPower;
    private LightingEffectKind? _lastEffect;
    private HardwareEffect? _lastHwEffect;

    private (double R, double G, double B) _smooth;
    private double _candle = 1, _candleTarget = 1;
    private double _hue;
    private double _beat;

    private int _keyPresses;
    private double _reactive;
    private bool _micInUse;
    private DateTime _micChecked;
    private DateTime _pomodoroStart = DateTime.UtcNow;

    private DateTime _lastErrorLog;
    private RgbColor? _overrideColor;
    private DateTime _overrideUntil;

    public LightingEngine(IClevoHardware hw, KeyboardSettings settings, Func<SensorSnapshot> sensors, Cs2Integration cs2)
    {
        _hw = hw;
        _settings = settings.Clone();
        _sensors = sensors;
        Cs2 = cs2;
        _thread = new Thread(Loop) { IsBackground = true, Name = "LightingEngine", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public Cs2Integration Cs2 { get; }

    /// <summary>Color currently shown on the keyboard (before white-balance correction).</summary>
    public RgbColor CurrentColor { get; private set; }

    public bool KeyboardIsOn { get; private set; } = true;

    /// <summary>While true (sleep / resume) nothing is sent to the EC.</summary>
    public volatile bool Paused;

    /// <summary>Last measured bar fill level of the screen-region effect (0..1).</summary>
    public double? LastRegionFill { get; private set; }

    public event Action<RgbColor>? ColorChanged;

    public void Apply(KeyboardSettings settings)
    {
        _settings = settings.Clone();
        _wake.Set();
    }

    /// <summary>Forces all values to be re-sent (e.g. after resume from sleep).</summary>
    public void Invalidate()
    {
        _invalidate = true;
        _wake.Set();
    }

    /// <summary>Temporarily overrides the effect (local API, previews).</summary>
    public void Override(RgbColor color, TimeSpan duration)
    {
        _overrideColor = color;
        _overrideUntil = DateTime.UtcNow + duration;
        _wake.Set();
    }

    public void ClearOverride()
    {
        _overrideColor = null;
        _wake.Set();
    }

    private void Loop()
    {
        var sw = Stopwatch.StartNew();
        while (_running)
        {
            var now = DateTime.UtcNow;
            if (GapHold(_lastTick, now) is { } hold && !Paused)
            {
                Log.Info("Lighting: system was asleep (no resume event) – re-sending the keyboard state shortly");
                _gapHoldUntil = hold;
            }
            _lastTick = now;
            if (now < _gapHoldUntil)
            {
                _invalidate = true; // after the hold everything is sent again
                _wake.WaitOne(500);
                continue;
            }
            if (Paused)
            {
                _wake.WaitOne(500);
                continue;
            }
            var s = _settings;
            var frameMs = 1000;
            try
            {
                if (_invalidate)
                {
                    _invalidate = false;
                    _lastSent = null; _lastBrightness = null; _lastPower = null; _lastEffect = null; _lastHwEffect = null;
                }

                var t = sw.Elapsed.TotalSeconds;
                var idle = s.IdleTimeoutMinutes > 0 && IdleTime() > TimeSpan.FromMinutes(s.IdleTimeoutMinutes);
                var overrideActive = _overrideColor is not null && DateTime.UtcNow < _overrideUntil;
                var effect = overrideActive ? LightingEffectKind.Static : s.Effect;
                var on = (s.Enabled && effect != LightingEffectKind.Off && !idle) || overrideActive;

                EnsureResources(effect);

                if (_lastPower != on)
                {
                    _hw.SetKeyboardPower(on);
                    _lastPower = on;
                    _lastSent = null;
                    _lastBrightness = null;
                    _lastHwEffect = null;
                    KeyboardIsOn = on;
                    if (!on) ColorChanged?.Invoke(RgbColor.Black);
                }

                if (on)
                {
                    var brightness = (byte)Math.Round(Math.Clamp(s.Brightness, 0, 100) * 2.55);
                    if (_lastBrightness != brightness)
                    {
                        _hw.SetKeyboardBrightness(brightness);
                        _lastBrightness = brightness;
                    }

                    if (effect == LightingEffectKind.Hardware)
                    {
                        if (_lastHwEffect != s.HardwareEffect || _lastEffect != effect)
                        {
                            _hw.SetKeyboardColor(Correct(s.Color, s));
                            _hw.SetKeyboardHardwareEffect(s.HardwareEffect);
                            _lastHwEffect = s.HardwareEffect;
                            _lastSent = null;
                            CurrentColor = s.Color;
                            ColorChanged?.Invoke(s.Color);
                        }
                        frameMs = 500;
                    }
                    else
                    {
                        _lastHwEffect = null;
                        RgbColor color;
                        if (overrideActive) { color = _overrideColor!.Value; frameMs = 50; }
                        else (color, frameMs) = Render(effect, s, t);
                        Send(color, s);
                    }
                }
                _lastEffect = effect;
            }
            catch (Exception ex)
            {
                frameMs = 1000; // EC busy or resuming – retry later
                if (DateTime.UtcNow - _lastErrorLog > TimeSpan.FromMinutes(1))
                {
                    _lastErrorLog = DateTime.UtcNow;
                    Log.Warn("Lighting: " + ex.Message);
                }
            }

            _wake.WaitOne(frameMs);
        }
    }

    private void Send(RgbColor color, KeyboardSettings s)
    {
        var corrected = Correct(color, s);
        if (_lastSent == corrected) return;
        _hw.SetKeyboardColor(corrected);
        _lastSent = corrected;
        CurrentColor = color;
        ColorChanged?.Invoke(color);
    }

    private static RgbColor Correct(RgbColor c, KeyboardSettings s)
    {
        static byte G(byte v, double gain) => (byte)Math.Clamp(Math.Round(v * gain), 0, 255);
        return new RgbColor(G(c.R, s.GainRed), G(c.G, s.GainGreen), G(c.B, s.GainBlue));
    }

    private (RgbColor Color, int FrameMs) Render(LightingEffectKind effect, KeyboardSettings s, double t)
    {
        var speed = Math.Clamp(s.Speed, 0.1, 5);
        switch (effect)
        {
            case LightingEffectKind.Static:
                return (s.Color, 500);

            case LightingEffectKind.Breathing:
            {
                var phase = (Math.Sin(t * speed * Math.PI * 0.5) + 1) / 2; // ~4 s period at speed 1
                return (s.Color.Scale(0.05 + 0.95 * phase * phase), 33);
            }

            case LightingEffectKind.Spectrum:
                return (RgbColor.FromHsv(t * 24 * speed, 1, 1), 40);

            case LightingEffectKind.Gradient:
            {
                var phase = (Math.Sin(t * speed * Math.PI * 0.4) + 1) / 2;
                return (RgbColor.Lerp(s.Color, s.SecondaryColor, phase), 40);
            }

            case LightingEffectKind.Candle:
            {
                if (_rng.NextDouble() < 0.15 * speed) _candleTarget = 0.55 + _rng.NextDouble() * 0.45;
                _candle += (_candleTarget - _candle) * 0.35;
                var baseColor = s.Color == RgbColor.White ? new RgbColor(255, 120, 20) : s.Color;
                return (baseColor.Scale(_candle), 50);
            }

            case LightingEffectKind.Daylight:
            {
                var now = DateTime.Now.TimeOfDay.TotalHours;
                // 1 = day (09–18 h), 0 = night (22–06 h), smooth transitions in between.
                var day = now switch
                {
                    < 6 => 0,
                    < 9 => (now - 6) / 3,
                    < 18 => 1,
                    < 22 => 1 - (now - 18) / 4,
                    _ => 0,
                };
                var k = s.DaylightNightKelvin + (s.DaylightDayKelvin - s.DaylightNightKelvin) * day;
                var dim = (s.DaylightNightBrightness + (100 - s.DaylightNightBrightness) * day) / 100.0;
                return (RgbColor.FromKelvin(k).Scale(dim), 30_000);
            }

            case LightingEffectKind.Ambient:
            {
                var fps = Math.Clamp(s.AmbientFps, 5, 60);
                var m = Monitor(s.AmbientMonitor);
                var sample = m is null ? null : _screen?.Sample(m);
                if (sample is { } c)
                {
                    var (h, sat, v) = c.ToHsv();
                    var boosted = RgbColor.FromHsv(h, Math.Clamp(sat * s.AmbientSaturationBoost, 0, 1), Math.Clamp(v * 1.15, 0, 1));
                    return (SmoothTo(boosted, s.AmbientSmoothing), 1000 / fps);
                }
                return (CurrentColor, 200);
            }

            case LightingEffectKind.Music:
            {
                var (level, bass, mid, high) = _audio.Read(s.MusicSensitivity);
                _beat = Math.Max(bass, _beat * 0.85);
                RgbColor target = s.MusicMode switch
                {
                    MusicColorMode.Pulse => s.Color.Scale(0.08 + 0.92 * level),
                    MusicColorMode.HueBeat => RgbColor.FromHsv(_hue += 0.6 + _beat * 6 * speed, 1, 0.15 + 0.85 * level),
                    _ => new RgbColor((byte)(bass * 255), (byte)(mid * 255), (byte)(high * 255)),
                };
                return (SmoothTo(target, 0.35), 25);
            }

            case LightingEffectKind.Temperature:
            {
                var snap = _sensors();
                var temp = Math.Max(snap.Cpu.TemperatureC, snap.Gpu.TemperatureC);
                var frac = Math.Clamp((temp - s.TemperatureCool) / (double)Math.Max(1, s.TemperatureHot - s.TemperatureCool), 0, 1);
                var c = RgbColor.FromHsv(120 * (1 - frac), 1, 1); // green → yellow → red
                if (frac >= 1) c = c.Scale(0.4 + 0.6 * Math.Abs(Math.Sin(t * 4)));
                return (SmoothTo(c, 0.8), frac >= 1 ? 40 : 250);
            }

            case LightingEffectKind.Battery:
            {
                var (onAc, pct, _) = Platform.SystemStatus.Power();
                var c = RgbColor.FromHsv(120 * pct / 100.0, 1, 1);
                var charging = onAc && pct < 100;
                if (charging) c = c.Scale(0.35 + 0.65 * (Math.Sin(t * 2) + 1) / 2);
                else if (pct <= 15) c = c.Scale(Math.Abs(Math.Sin(t * 3)));
                return (c, charging || pct <= 15 ? 50 : 5000);
            }

            case LightingEffectKind.ScreenRegion:
            {
                var r = s.Region;
                var m = Monitor(r.Monitor);
                if (m is null || _region is null) return (CurrentColor, 500);
                if (r.Mode == RegionMode.AreaColor)
                {
                    var avg = _region.AverageColor(m, r);
                    return (avg is { } a ? SmoothTo(a, 0.5) : CurrentColor, 50);
                }
                var fill = _region.BarFill(m, r);
                if (fill is null) return (CurrentColor, 200);
                LastRegionFill = fill;
                var c = HealthColor(fill.Value, s, r.WarnBelowPercent, t, r.FullColor, r.EmptyColor);
                return (SmoothTo(c, 0.3), 40);
            }

            case LightingEffectKind.GameCs2:
                return RenderCs2(s, t);

            case LightingEffectKind.Reactive:
            {
                var presses = Interlocked.Exchange(ref _keyPresses, 0);
                if (presses > 0)
                {
                    _reactive = 1;
                    _hue = (_hue + 37 * presses) % 360;
                }
                _reactive *= Math.Pow(0.90, speed);
                var flash = s.SecondaryColor == s.Color ? RgbColor.FromHsv(_hue, 1, 1) : s.SecondaryColor;
                return (RgbColor.Lerp(s.Color.Scale(0.25), flash, _reactive), _reactive > 0.01 ? 16 : 50);
            }

            case LightingEffectKind.CpuLoad:
            {
                var load = _sensors().CpuLoadPercent / 100.0;
                return (SmoothTo(RgbColor.FromHsv(200 - 200 * load, 1, 1), 0.85), 100); // blue → red
            }

            case LightingEffectKind.OnAir:
            {
                if (DateTime.UtcNow - _micChecked > TimeSpan.FromSeconds(1))
                {
                    _micInUse = MicrophoneMonitor.IsMicrophoneInUse();
                    _micChecked = DateTime.UtcNow;
                }
                return _micInUse
                    ? (new RgbColor(255, 0, 0).Scale(0.6 + 0.4 * (Math.Sin(t * 2.5) + 1) / 2), 40)
                    : (s.Color, 500);
            }

            case LightingEffectKind.Pomodoro:
            {
                if (_lastEffect != LightingEffectKind.Pomodoro) _pomodoroStart = DateTime.UtcNow;
                double work = Math.Max(1, s.PomodoroWorkMinutes) * 60, brk = Math.Max(1, s.PomodoroBreakMinutes) * 60;
                var pos = (DateTime.UtcNow - _pomodoroStart).TotalSeconds % (work + brk);
                if (pos < work)
                {
                    var left = work - pos;
                    var c = RgbColor.FromHsv(120 * left / work, 1, 1);
                    if (left < 10) c = c.Scale(Math.Sin(t * 10) > 0 ? 1 : 0.1);
                    return (c, left < 10 ? 30 : 1000);
                }
                var breathe = (Math.Sin(t * Math.PI * 0.5) + 1) / 2;
                return (new RgbColor(0, 120, 255).Scale(0.2 + 0.8 * breathe), 40);
            }

            case LightingEffectKind.Heartbeat:
            {
                var p = t * speed % 1.2;
                double v = p < 0.12 ? p / 0.12 : p < 0.25 ? 1 - (p - 0.12) / 0.13 * 0.7 : p < 0.35 ? 0.3 + (p - 0.25) / 0.1 * 0.6 : Math.Max(0.05, 0.9 - (p - 0.35) * 1.6);
                return (s.Color.Scale(v), 20);
            }

            case LightingEffectKind.Lava:
            {
                var h = (Math.Sin(t * 0.07 * speed) * 60 + Math.Sin(t * 0.031 * speed) * 40 + 340) % 360;
                var v = 0.7 + 0.3 * Math.Sin(t * 0.23 * speed);
                return (RgbColor.FromHsv(h, 0.95, v), 60);
            }

            case LightingEffectKind.Fire:
            {
                if (_rng.NextDouble() < 0.4 * speed) _candleTarget = 0.35 + _rng.NextDouble() * 0.65;
                _candle += (_candleTarget - _candle) * 0.5;
                return (RgbColor.FromHsv(8 + 30 * _candle, 1, _candle), 30);
            }

            case LightingEffectKind.Police:
            {
                var phase = (int)(t * 6 * speed) % 8;
                return (phase switch
                {
                    0 or 2 => new RgbColor(255, 0, 0),
                    4 or 6 => new RgbColor(0, 40, 255),
                    _ => RgbColor.Black,
                }, 20);
            }

            default:
                return (s.Color, 500);
        }
    }

    private (RgbColor, int) RenderCs2(KeyboardSettings s, double t)
    {
        var st = Cs2.Latest;
        if (st is null || DateTime.UtcNow - st.Received > TimeSpan.FromSeconds(20))
            return (s.Color.Scale(0.4 + 0.6 * (Math.Sin(t) + 1) / 2), 50); // waiting for game
        if (st.Flashed > 0)
            return (RgbColor.White.Scale(st.Flashed / 255.0), 20);
        if (st.Bomb == "planted")
            return (new RgbColor(255, 0, 0).Scale(Math.Sin(t * 2 * Math.PI * 1.5) > 0 ? 1 : 0.1), 30);
        if (st.Bomb == "exploded")
            return (new RgbColor(255, 100, 0), 100);
        if (st.Burning > 0)
            return (new RgbColor(255, (byte)(60 + _rng.Next(80)), 0), 40);
        if (st.Health <= 0)
            return (new RgbColor(60, 0, 0), 200);
        var c = HealthColor(st.Health / 100.0, s, 25, t, new RgbColor(0, 255, 40), new RgbColor(255, 0, 0));
        return (SmoothTo(c, 0.4), 30);
    }

    /// <summary>Maps a 0..1 value (health, mana, …) to a color according to the user's choice.</summary>
    private static RgbColor HealthColor(double value, KeyboardSettings s, int warnBelowPercent, double t, RgbColor full, RgbColor empty)
    {
        value = Math.Clamp(value, 0, 1);
        var hueColor = RgbColor.Lerp(empty, full, value);
        // Lerp between two saturated colors can look muddy; go through HSV when they are green/red.
        var (h1, _, _) = empty.ToHsv();
        var (h2, _, _) = full.ToHsv();
        if (Math.Abs(h2 - h1) is > 30 and < 330) hueColor = RgbColor.FromHsv(h1 + (h2 - h1) * value, 1, 1);
        var c = s.HealthDisplay switch
        {
            HealthDisplay.Brightness => s.Color.Scale(0.05 + 0.95 * value),
            HealthDisplay.Both => hueColor.Scale(0.15 + 0.85 * value),
            _ => hueColor,
        };
        if (value * 100 < warnBelowPercent)
            c = c.Scale(0.25 + 0.75 * Math.Abs(Math.Sin(t * (3 + 6 * (1 - value)))));
        return c;
    }

    /// <summary>Called by the keyboard hook for the reactive effect.</summary>
    public void NotifyKeyPress()
    {
        if (_settings.Effect != LightingEffectKind.Reactive) return;
        Interlocked.Increment(ref _keyPresses);
        _wake.Set();
    }

    private RgbColor SmoothTo(RgbColor target, double smoothing)
    {
        var a = 1 - Math.Clamp(smoothing, 0, 0.98);
        _smooth = (_smooth.R + (target.R - _smooth.R) * a, _smooth.G + (target.G - _smooth.G) * a, _smooth.B + (target.B - _smooth.B) * a);
        static byte C(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
        return new RgbColor(C(_smooth.R), C(_smooth.G), C(_smooth.B));
    }

    private MonitorInfo? Monitor(int index)
    {
        if (_monitors.Count == 0 || DateTime.UtcNow - _monitorsRefreshed > TimeSpan.FromSeconds(10))
        {
            _monitors = ScreenSampler.GetMonitors();
            _monitorsRefreshed = DateTime.UtcNow;
        }
        if (_monitors.Count == 0) return null;
        return index >= 0 && index < _monitors.Count ? _monitors[index] : _monitors.FirstOrDefault(m => m.Primary) ?? _monitors[0];
    }

    private void EnsureResources(LightingEffectKind effect)
    {
        // Screen capture: GDI on Windows, X11 or grim on Linux; where the desktop forbids it these effects stay idle.
        var canCapture = Platform.ScreenGrabber.UnsupportedReason is null;
        if (effect == LightingEffectKind.Ambient && canCapture) _screen ??= new ScreenSampler();
        else if (_screen is not null) { _screen.Dispose(); _screen = null; }

        if (effect == LightingEffectKind.ScreenRegion && canCapture) _region ??= new RegionSampler();
        else if (_region is not null) { _region.Dispose(); _region = null; }

        if (effect == LightingEffectKind.Music) _audio.Start();
        else if (_audio.IsRunning) _audio.Stop();
    }

    private static TimeSpan IdleTime() => Platform.SystemStatus.IdleTime();

    public void Dispose()
    {
        _running = false;
        _wake.Set();
        _thread.Join(1000);
        _screen?.Dispose();
        _region?.Dispose();
        _audio.Dispose();
    }
}
