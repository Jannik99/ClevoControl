# ClevoControl on Linux

> [!WARNING]
> **Linux support is new and has not been tested on a Clevo laptop yet.** The code is covered by unit tests and
> the desktop app is rendered on Linux in CI, and it talks to the same driver interface that TUXEDO Control Center
> uses – but nobody has run it on real Clevo hardware under Linux so far. If you try it, please
> [report your result](https://github.com/Jannik99/ClevoControl/issues/new?template=model_report.yml) –
> working or not. That is the most useful contribution right now.

On Linux, ClevoControl consists of

* **the desktop app** `clevocontrol` – the same pages as the Windows app (built with Avalonia),
* **`clevoctl`** – command-line tool, and `clevoctl daemon` as an optional **background service** for systems
  without the app (or for headless use).

Run either the app *or* the service – the app stops the service automatically while it runs.

## What works

✅ works · ⚠️ works with limits · ❌ not available

| Feature | Linux | Notes |
|---|---|---|
| Keyboard color, brightness, on/off | ✅ | 1-zone, 3-zone and white keyboards; per-key RGB (ITE 8291 / 829x via tuxedo-drivers) is experimental and only offered when the per-key LEDs are found. |
| Software lighting effects | ✅ | Static, breathing, rainbow, gradient, candle, fire, temperature, battery, Pomodoro, daylight … |
| Hardware keyboard effects (EC animations) | ❌ | Not exposed by the Linux driver – use the software effects. |
| Fan modes (auto / max), custom fan curve with safety net | ✅ | Same safety net as on Windows. |
| Fan speed in RPM | ❌ | The driver only reports duty (%) and temperature. |
| Performance profiles, AC/battery switching | ✅ | |
| System power mode | ✅ | Through power-profiles-daemon (`powerprofilesctl`), if installed. |
| Cooling modes (turbo off / CPU limit) | ✅ | Intel `intel_pstate`; AMD and others: turbo switch only. Needs the permissions from the installer. |
| Panel brightness | ✅ | Through systemd-logind (no root needed). |
| Lower refresh rate on battery | ⚠️ | X11 only (`xrandr`). |
| Battery health, capacity, cycles | ✅ | |
| Charge limit | ⚠️ | Only if the battery exposes `charge_control_*_threshold`. |
| Volume, mute, microphone mute, output device | ✅ | PulseAudio or PipeWire (`pactl`). |
| Music mode | ✅ | Records the default output's monitor with `parec`. |
| "On Air" (microphone in use) | ✅ | `pactl` |
| Ambient light, screen region, game health bars | ⚠️ | X11 ✅ · Wayland on wlroots (Sway, Hyprland) with `grim` ✅ · GNOME/KDE on Wayland ❌ (screen capture is not allowed there). |
| Automatic scenes per app | ⚠️ | X11 ✅ and KDE Wayland with [kdotool](https://github.com/jinliu/kdotool) ✅: the focused window. GNOME/other Wayland: ClevoControl recognises *running* games and apps instead (games under Steam/Proton included). |
| Reactive typing effect, scene hotkeys (Ctrl+Alt+Shift+0…9, +F) | ✅ | Reads `/dev/input` – your user must be in the group `input` (installer option `--with-input`). |
| Key mapping, Super key lock | ⚠️ | Exported as a [keyd](https://github.com/rvaiya/keyd) configuration (*Key mapping* page) instead of grabbing the keyboard. The keyboard layout is detected from GNOME, KDE, X11 or `localectl` (or chosen on the page); keys are matched by position, so the file works with any layout. Program launches are not converted – use your desktop's shortcut settings. |
| Keyboard off when idle | ⚠️ | GNOME (also Wayland) ✅, X11 with `xprintidle` ✅, KDE Wayland ❌. |
| Sensors page | ✅ | CPU per core, NVIDIA via NVML, AMD GPU load; Intel GPU load ❌. |
| OpenRGB SDK server, local HTTP API, Counter-Strike 2 | ✅ | |
| Tray icon, notifications | ✅ | Tray needs StatusNotifier support (GNOME: AppIndicator extension). Notifications via `notify-send`. |
| Sleep / resume handling | ✅ | systemd sleep hook. |
| Autostart | ✅ | `~/.config/autostart` (Settings page). |

Want to help with one of the ⚠️/❌ items? See [CONTRIBUTING.md](../CONTRIBUTING.md#adding-linux-support-for-a-feature).

## 1. Install the kernel driver

ClevoControl uses the open-source **[tuxedo-drivers](https://github.com/tuxedocomputers/tuxedo-drivers)**
kernel modules from TUXEDO Computers (who sell Clevo-based laptops). They provide:

* `clevo_acpi` / `clevo_wmi` – talk to the Clevo embedded controller (the same EC commands as Clevo's Windows driver),
* `tuxedo_io` – the `/dev/tuxedo_io` device for fans, temperatures and performance profiles,
* the keyboard backlight as `/sys/class/leds/rgb:kbd_backlight*` (or `white:kbd_backlight`).

Install it from your distribution (for example `tuxedo-drivers-dkms` from the AUR on Arch) or build it
from source as described in its README.

> [!IMPORTANT]
> **Laptops not sold by TUXEDO:** since 2023, tuxedo-drivers only load if the firmware vendor contains "TUXEDO" **or**
> the CPU is on an allow-list (Intel up to 12th gen / Alder Lake and older AMD generations – see
> `src/tuxedo_compatibility_check` in the driver). On a plain Clevo or a laptop from another brand with a **newer CPU**
> the modules refuse to load. Community builds without this check exist (for example
> `tuxedo-drivers-nocompatcheck-dkms` in the AUR). This is outside of ClevoControl's control and **at your own risk** –
> the check exists because the driver writes to the embedded controller.

Check that everything is in place:

```bash
clevoctl detect          # after installing ClevoControl – checks firmware interface and driver
ls -l /dev/tuxedo_io
ls /sys/class/leds | grep kbd_backlight
```

If **TUXEDO Control Center** is installed, disable its service – it controls the same hardware:

```bash
sudo systemctl disable --now tccd tccd-sleep
```

## 2. Install ClevoControl

Download `ClevoControl-<version>-linux-x64.tar.gz` from the
[releases](https://github.com/Jannik99/ClevoControl/releases), extract it and run the installer:

```bash
tar -xzf ClevoControl-*-linux-x64.tar.gz
cd ClevoControl-*-linux-x64
sudo ./contrib/linux/install.sh --with-input
```

The installer:

* copies `clevocontrol` (app) and `clevoctl` (tool) to `/usr/local/bin` and adds a menu entry,
* creates the group `clevocontrol` and adds you to it,
* installs a **udev rule** and a **tmpfiles rule** that give this group access to `/dev/tuxedo_io`, the keyboard LEDs,
  the battery charge thresholds and the CPU turbo switch – nothing has to run as root,
* with `--with-input` also adds you to the group `input` (reactive typing effect, scene hotkeys). Leave it out if you
  do not want programs of your user to be able to read keyboard events,
* installs a **systemd sleep hook** and the **systemd user service**.

**Log out and back in** (so the new groups apply), then start **ClevoControl** from the menu or run `clevocontrol`.
Uninstall with `sudo ./contrib/linux/install.sh --uninstall`.

The release binaries are self-contained (no .NET runtime needed). To build them yourself, see
[CONTRIBUTING.md](../CONTRIBUTING.md#building).

### Optional tools

| Tool | For | Package (Debian/Ubuntu · Fedora · Arch) |
|---|---|---|
| `pactl`, `parec` | sound, music mode, "On Air" | `pulseaudio-utils` · `pulseaudio-utils` · `libpulse` (usually installed) |
| `powerprofilesctl` | system power mode | `power-profiles-daemon` |
| `xrandr`, `xprop` | refresh rate, focused window (X11) | `x11-xserver-utils`, `x11-utils` · `xrandr`, `xprop` · `xorg-xrandr`, `xorg-xprop` |
| `xprintidle` | idle detection on X11 | `xprintidle` |
| `grim` | screen capture on Sway/Hyprland | `grim` |
| `kdotool` | focused window on KDE Wayland | from its GitHub page / AUR |
| `keyd` | key mapping | `keyd` |
| `notify-send` | notifications | `libnotify-bin` · `libnotify` · `libnotify` |

## 3. Use it

### Desktop app

Everything works as on Windows; the *About & support* page shows a support check, the driver status and conflicting
software (TUXEDO Control Center). Closing the window keeps ClevoControl running in the tray (configurable).

### Command line

```bash
clevoctl detect
clevoctl status
clevoctl color '#ff8800'
clevoctl brightness 60
clevoctl fan auto
clevoctl profile quiet
clevoctl charge 60 80
clevoctl sysinfo
```

Run `clevoctl help` for all commands. While the app or the service runs, its lighting engine owns the keyboard and may
overwrite colors set with `clevoctl color` – use the local API (below) instead.

### Background service (without the app)

`clevoctl daemon` runs the lighting effect, the fan curve, the performance profile and AC/battery switching from
`~/.config/ClevoControl/settings.json` – the same file the app writes, so set things up in the app first.

```bash
systemctl --user enable --now clevocontrol
systemctl --user reload clevocontrol         # after editing settings.json
journalctl --user -u clevocontrol -f        # service output
cat ~/.config/ClevoControl/clevocontrol.log # ClevoControl's own log
```

On a dual-boot machine you can also copy `%APPDATA%\ClevoControl\settings.json` from Windows – the format is the same.

### Local API

With *Games & integrations → Local API* enabled (or `"LocalApiEnabled": true`) ClevoControl listens on `127.0.0.1:3002`. Requests made by web pages (with an `Origin` header or a foreign `Host`) are refused; scripts and curl work:

```bash
curl http://127.0.0.1:3002/status
curl -X POST http://127.0.0.1:3002/color  -d '{"color":"#ff0000","seconds":3}'
curl -X POST http://127.0.0.1:3002/scene  -d '{"name":"Film"}'
curl -X POST http://127.0.0.1:3002/effect -d '{"effect":"Spectrum"}'
```

## Troubleshooting

| Message | Fix |
|---|---|
| `/dev/tuxedo_io not found` | tuxedo-drivers is not installed or `tuxedo_io` is not loaded (`sudo modprobe tuxedo_io`). |
| `tuxedo_io found no Clevo interface` | `clevo_acpi` / `clevo_wmi` did not load – see the note about laptops not sold by TUXEDO; `sudo dmesg \| grep -i -E 'clevo\|tuxedo'`. |
| `No permission for /dev/tuxedo_io` or `/sys/class/leds/…` | Run the installer, then log out and back in. `id` must list the group `clevocontrol`. |
| Cooling modes do nothing | The CPU driver offers no turbo switch, or the tmpfiles rule is missing (`sudo systemd-tmpfiles --create /etc/tmpfiles.d/clevocontrol.conf`). |
| Reactive typing / hotkeys do not react | Your user is not in the group `input` (`sudo usermod -aG input $USER`, log in again). |
| No tray icon on GNOME | Install the "AppIndicator and KStatusNotifierItem Support" extension. |
| `EC returned no valid sensor data` | The driver loaded, but the EC answered with nonsense. Please open a model report. |
| Fans and lighting jump around | TUXEDO Control Center's `tccd` is running – disable it (see above). |
| Colors look wrong | Adjust the white balance on the *Lighting* page. |

When you open an issue, include the output of `clevoctl detect`, `clevoctl sysinfo` and `clevoctl dump`.

## First-time tester checklist

If you are among the first to try ClevoControl on Linux, these checks tell us the most. Keep an eye on the
temperatures (`clevoctl status`) while testing the fans.

1. `clevoctl detect` and `clevoctl status` – plausible CPU/GPU temperatures, right keyboard type?
2. `clevoctl brightness 100` (the driver starts with the keyboard light off), then `clevoctl color '#ff0000'`,
   `'#00ff00'`, `'#0000ff'`, `'#ffffff'` – right colors? Is white roughly white?
3. `clevoctl brightness 20`, `clevoctl kb off`, `clevoctl kb on`.
4. `clevoctl fan max` – fans spin up? Then `clevoctl fan auto` – do they slow down again?
5. `clevoctl fan duty 60`, wait a few seconds, then **`clevoctl fan auto`** and put some load on the CPU.
   **Do the fans react to the load again?** This is the most important check: on Linux "auto" uses a different
   EC command than on Windows (see [PROTOCOL.md](PROTOCOL.md#linux-tuxedo-drivers)). If the fans stay at a fixed
   speed, reboot to get automatic control back and please report it.
6. `clevoctl profile quiet` and `clevoctl profile performance` – does the fan behaviour change?
7. Start the app: do the pages work, does the tray icon appear? Suspend and resume the laptop – does the keyboard light come back?

Then open a [model report](https://github.com/Jannik99/ClevoControl/issues/new?template=model_report.yml) with the results.

## How it works

* `src/ClevoControl.Core/Hardware/Linux/LinuxClevoHardware.cs` implements the same `IClevoHardware` interface as the
  Windows backend: keyboard → `multi_intensity` / `brightness` of the LED class devices; fans, temperatures,
  profiles → `ioctl` on `/dev/tuxedo_io`; charge limit → `charge_control_*_threshold`.
* `src/ClevoControl.Core/Platform/` contains the Linux implementations of everything else (sound, power, screen
  capture, input, processes) – mostly reading `/proc` and `/sys` or calling the standard tools above.
* `src/ClevoControl.Desktop` is the cross-platform app (Avalonia); `src/ClevoControl.App` is the Windows app (WPF).

The ioctl numbers and the driver's behaviour are documented in [PROTOCOL.md](PROTOCOL.md#linux-tuxedo-drivers).
