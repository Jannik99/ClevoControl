using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Runtime.InteropServices;
using ClevoControl.Core.Platform;

namespace ClevoControl.Core.Services;

public sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>
/// Master volume, mute, microphone mute and default output device switching.
/// Windows: Core Audio (WASAPI). Linux: pactl (PulseAudio / PipeWire).
/// </summary>
public sealed class SoundService : IDisposable
{
    private readonly MMDeviceEnumerator? _enumerator = OperatingSystem.IsWindows() ? new() : null;
    private static bool Linux => OperatingSystem.IsLinux();

    private MMDevice? Default(DataFlow flow)
    {
        try { return _enumerator?.GetDefaultAudioEndpoint(flow, Role.Multimedia); }
        catch { return null; }
    }

    public int? Volume
    {
        get
        {
            if (Linux) return LinuxAudio.GetVolume();
            using var d = Default(DataFlow.Render);
            return d is null ? null : (int)Math.Round(d.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
        }
        set
        {
            if (value is not { } v) return;
            if (Linux) { LinuxAudio.SetVolume(v); return; }
            using var d = Default(DataFlow.Render);
            if (d is not null) d.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(v, 0, 100) / 100f;
        }
    }

    public bool Muted
    {
        get
        {
            if (Linux) return LinuxAudio.GetMute() ?? false;
            using var d = Default(DataFlow.Render);
            return d?.AudioEndpointVolume.Mute ?? false;
        }
        set
        {
            if (Linux) { LinuxAudio.SetMute(value); return; }
            using var d = Default(DataFlow.Render);
            if (d is not null) d.AudioEndpointVolume.Mute = value;
        }
    }

    public bool MicrophoneMuted
    {
        get
        {
            if (Linux) return LinuxAudio.GetMute(input: true) ?? false;
            using var d = Default(DataFlow.Capture);
            return d?.AudioEndpointVolume.Mute ?? false;
        }
        set
        {
            if (Linux) { LinuxAudio.SetMute(value, input: true); return; }
            using var d = Default(DataFlow.Capture);
            if (d is not null) d.AudioEndpointVolume.Mute = value;
        }
    }

    public bool HasMicrophone
    {
        get
        {
            if (Linux) return LinuxAudio.GetMute(input: true) is not null;
            using var d = Default(DataFlow.Capture);
            return d is not null;
        }
    }

    public List<AudioDevice> GetOutputDevices()
    {
        if (Linux)
        {
            var defaultSink = LinuxAudio.DefaultSink();
            return LinuxAudio.Sinks().Select(s => new AudioDevice(s.Name, s.Description, s.Name == defaultSink)).ToList();
        }
        var def = Default(DataFlow.Render)?.ID;
        var list = new List<AudioDevice>();
        try
        {
            if (_enumerator is null) return list;
            foreach (var d in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                list.Add(new AudioDevice(d.ID, d.FriendlyName, d.ID == def));
                d.Dispose();
            }
        }
        catch { }
        return list;
    }

    /// <summary>Sets the default output device (uses the undocumented but stable IPolicyConfig interface).</summary>
    public static bool SetDefaultOutput(string deviceId)
    {
        if (Linux) return LinuxAudio.SetDefaultSink(deviceId);
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                policy.SetDefaultEndpoint(deviceId, role);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => _enumerator?.Dispose();

    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat(string id, bool def, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(string id);
        [PreserveSig] int SetDeviceFormat(string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string id, bool def, IntPtr defaultPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod(string id, IntPtr period);
        [PreserveSig] int GetShareMode(string id, IntPtr mode);
        [PreserveSig] int SetShareMode(string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string id, bool fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(string id, bool fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role);
        [PreserveSig] int SetEndpointVisibility(string id, bool visible);
    }
}
