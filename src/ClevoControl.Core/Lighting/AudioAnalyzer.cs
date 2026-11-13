using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ClevoControl.Core.Lighting;

/// <summary>
/// Captures whatever is playing (WASAPI loopback) and splits it into bass / mid / treble
/// energy with simple IIR filters. Values are normalised with an automatic gain so quiet and
/// loud music both use the full range.
/// </summary>
public sealed class AudioAnalyzer : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private readonly object _sync = new();
    private double _lpBass, _lpMid, _hpPrev, _hpOut;
    private double _accBass, _accMid, _accHigh, _accAll;
    private int _accCount;
    private double _peak = 0.05;

    public bool IsRunning => _capture is not null;

    public void Start()
    {
        if (_capture is not null) return;
        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += (_, _) => { };
            _capture.StartRecording();
        }
        catch
        {
            _capture?.Dispose();
            _capture = null;
        }
    }

    public void Stop()
    {
        var c = _capture;
        _capture = null;
        if (c is null) return;
        try { c.StopRecording(); } catch { }
        c.Dispose();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var cap = _capture;
        if (cap is null || e.BytesRecorded == 0) return;
        var fmt = cap.WaveFormat;
        if (fmt.Encoding != WaveFormatEncoding.IeeeFloat && fmt.Encoding != WaveFormatEncoding.Extensible) return;
        var channels = fmt.Channels;
        var rate = fmt.SampleRate;
        // Filter coefficients (one-pole). Bass < ~200 Hz, mid < ~2.5 kHz, treble above.
        var aBass = 1 - Math.Exp(-2 * Math.PI * 200 / rate);
        var aMid = 1 - Math.Exp(-2 * Math.PI * 2500 / rate);
        var hpA = Math.Exp(-2 * Math.PI * 2500 / rate);

        var samples = e.BytesRecorded / 4 / channels;
        lock (_sync)
        {
            for (var i = 0; i < samples; i++)
            {
                double s = 0;
                for (var c = 0; c < channels; c++) s += BitConverter.ToSingle(e.Buffer, (i * channels + c) * 4);
                s /= channels;
                _lpBass += aBass * (s - _lpBass);
                _lpMid += aMid * (s - _lpMid);
                var mid = _lpMid - _lpBass;
                _hpOut = hpA * (_hpOut + s - _hpPrev);
                _hpPrev = s;
                _accBass += _lpBass * _lpBass;
                _accMid += mid * mid;
                _accHigh += _hpOut * _hpOut;
                _accAll += s * s;
                _accCount++;
            }
        }
    }

    /// <summary>Returns normalised (0..1) levels since the last call.</summary>
    public (double Level, double Bass, double Mid, double Treble) Read(double sensitivity)
    {
        double bass, mid, high, all;
        lock (_sync)
        {
            if (_accCount == 0) return (0, 0, 0, 0);
            bass = Math.Sqrt(_accBass / _accCount);
            mid = Math.Sqrt(_accMid / _accCount);
            high = Math.Sqrt(_accHigh / _accCount);
            all = Math.Sqrt(_accAll / _accCount);
            _accBass = _accMid = _accHigh = _accAll = 0;
            _accCount = 0;
        }
        // Automatic gain: track the peak, decay slowly.
        _peak = Math.Max(all, _peak * 0.995);
        _peak = Math.Max(_peak, 0.01);
        var g = sensitivity / _peak;
        static double N(double v) => Math.Clamp(v, 0, 1);
        return (N(all * g), N(bass * g * 1.1), N(mid * g * 2.0), N(high * g * 4.0));
    }

    public void Dispose() => Stop();
}
