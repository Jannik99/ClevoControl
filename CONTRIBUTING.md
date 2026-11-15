# Contributing to ClevoControl

Thanks for helping! ClevoControl is a small community project, and every Clevo model is a little different –
so reports from real hardware are just as valuable as code.

* [Ways to help](#ways-to-help)
* [Building](#building)
* [How the code is organised](#how-the-code-is-organised)
* [Safety rules for EC commands](#safety-rules-for-ec-commands)
* [Adding Linux support for a feature](#adding-linux-support-for-a-feature)
* [Pull requests](#pull-requests)

## Ways to help

| You have … | Please … |
|---|---|
| a Clevo-based laptop (any brand) | open a [model report](https://github.com/Jannik99/ClevoControl/issues/new?template=model_report.yml). Partial results ("colors work, fans don't") are very welcome. |
| Linux on a Clevo laptop | try the [Linux setup](docs/LINUX.md) and file a model report with the Linux column filled in. Nobody has run the Linux backend on real hardware yet. |
| found a bug | open a [bug report](https://github.com/Jannik99/ClevoControl/issues/new?template=bug_report.yml) and attach the log (`%APPDATA%\ClevoControl\clevocontrol.log` or `~/.config/ClevoControl/clevocontrol.log`). |
| an idea | open an issue first, so we can agree on the approach before you invest time. |
| some C# time | pick an issue labelled `good first issue` / `help wanted`, or one of the ❌ items in [docs/LINUX.md](docs/LINUX.md#what-works). |

## Building

You need the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
git clone https://github.com/Jannik99/ClevoControl.git
cd ClevoControl
dotnet build
dotnet test --filter "FullyQualifiedName!~ScreenTests&FullyQualifiedName!~MonitorTests"
```

* **Windows** builds everything, including both desktop apps (WPF and Avalonia).
* **Linux / macOS** can build and test everything except the WPF app:
  `dotnet build src/ClevoControl.Desktop`, `dotnet build src/ClevoControl.Cli`, `dotnet test tests/ClevoControl.Tests --filter …`.
* UI changes: a feature added to a WPF page (`src/ClevoControl.App/Pages`) should also go into the Avalonia page with
  the same name (`src/ClevoControl.Desktop/Pages`) – both use the same `Ui` helper API, so this is mostly copy and paste.
* Render every page of the Avalonia app to PNG without hardware:
  `CLEVOCONTROL_CONFIG_DIR=/tmp/cc clevocontrol --screenshots /tmp/shots` (CI does this on Linux with `xvfb-run`).
* `ScreenTests` and `MonitorTests` need a Windows desktop session; CI skips them.
* Run without a Clevo laptop: `ClevoControl.exe --simulate` / `clevoctl --simulate status`.

Release builds (self-contained, single file):

```bash
dotnet publish src/ClevoControl.App -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -o publish
dotnet publish src/ClevoControl.Cli -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -o publish
dotnet publish src/ClevoControl.Cli -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish-linux
```

CI (`.github/workflows/build.yml`) builds and tests on Windows and Ubuntu for every pull request.

## How the code is organised

```
src/
  ClevoControl.Core/          cross-platform library (net9.0) – all logic lives here
    Hardware/                 IClevoHardware + backends
      ClevoHardware.cs          Windows: InsydeDCHU.dll
      Linux/                    Linux: tuxedo-drivers (/dev/tuxedo_io + /sys/class/leds)
      SimulatedHardware.cs      --simulate and machines without a driver
    Platform/                 OS layer: Linux implementations (sound, power, screen capture, input, processes,
                              keyd export, support check) and /proc + /sys parsers
    Services/                 fans, power, sensors, sound, battery, key hook, local API, OpenRGB server
    Lighting/                 lighting engine and effects, screen/audio sampling
    Automation/               app detection and automatic scenes
    Settings/                 settings.json model and store
    ControlCenter.cs          wires all services together (used by the app and `clevoctl daemon`)
  ClevoControl.App/           Windows desktop app (WPF), UI only
  ClevoControl.Desktop/       cross-platform desktop app (Avalonia) – the Linux app; same page structure as the WPF app
  ClevoControl.Cli/           clevoctl (Windows + Linux), including `clevoctl daemon`
tests/ClevoControl.Tests/     xUnit tests
contrib/linux/                udev rule, systemd unit, sleep hook, installer
docs/                         PROTOCOL.md (EC commands), LINUX.md, OVERCLOCKING.md
```

Guidelines:

* Logic goes into **Core**, the app only shows it. Anything the app can do should eventually be reachable from Core
  (and therefore from `clevoctl` / the API / Linux).
* All hardware access goes through **`IClevoHardware`**. Never call the DLL or `/dev/tuxedo_io` from anywhere else.
* The UI is bilingual: use `Loc.T("Deutsch", "English")` for every visible string.
* Keep dependencies minimal. New NuGet packages need a good reason.

## Safety rules for EC commands

The embedded controller runs the fans and power management. A wrong command can make a laptop overheat or misbehave.

1. **Only send commands that are verified** – on real hardware, or taken from Clevo's own software or an established
   open-source driver (tuxedo-drivers). Document every command in [docs/PROTOCOL.md](docs/PROTOCOL.md) with its source.
2. **Never probe** unknown commands or sub-commands "to see what happens".
3. **Fans must always fall back to the firmware** on exit, sleep, implausible sensor values and errors.
   Do not weaken the safety net in `FanController` (≥ 35 % from 70 °C, ≥ 70 % from 80 °C, 100 % from 88 °C).
4. Do not copy code from projects with incompatible licences. Facts (command numbers, data layouts) are fine;
   write the code yourself and note where the information came from.

## Adding Linux support for a feature

Most ❌ items in [docs/LINUX.md](docs/LINUX.md#what-works) are services in Core that use a Windows API. The pattern:

1. Find the Windows-only call (e.g. `MicrophoneMonitor`, `AppDetector.Foreground`, `CoolingService.Apply`). It is
   guarded with `if (!OperatingSystem.IsWindows()) return …;` and usually has a `TODO Linux` comment.
2. Add a Linux branch next to it (`if (OperatingSystem.IsLinux()) …`). Prefer reading files in `/proc` or `/sys`
   via `Platform.LinuxSys` (so it can be tested with a fake root) or calling a common tool (`pactl`, `wpctl`)
   over adding native dependencies.
3. Put parsing into a pure function and add a test in `tests/ClevoControl.Tests/LinuxTests.cs`.
   Tests that need Linux file names (with `:`) use `[LinuxFact]`; they run on the Ubuntu CI job.
4. Update the feature table in [docs/LINUX.md](docs/LINUX.md).
5. Things that need root (e.g. `intel_pstate`) also need an entry in `contrib/linux/99-clevocontrol.rules`.

## Pull requests

* One topic per pull request; small is good.
* `dotnet build` must have no warnings and the tests must pass.
* Say on which model and OS you tested, or "not tested on hardware".
* Update the docs (README, LINUX.md, PROTOCOL.md) when behaviour changes.
* Code, comments, commit messages and docs are in English. UI strings are German + English (`Loc.T`).

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE).
