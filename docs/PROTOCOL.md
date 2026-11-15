# Clevo DCHU protocol notes

How ClevoControl talks to the embedded controller (EC). Everything here was either taken from the
P/Invoke metadata / call sites of Clevo's own Control Center apps or verified on a Clevo N9x0TD_TF.
Nothing was copied from other projects' source code; command numbers that match the Linux
`tuxedo-keyboard` driver are noted because they confirm each other.

## The DLL

`InsydeDCHU.dll` is installed by Clevo's driver package into
`C:\Windows\System32\DriverStore\FileRepository\acpibridge*.inf_amd64_*\`. It works without admin rights.

```c
// stdcall/cdecl are identical on x64
int SetDCHU_Data(int command, uint8_t* data, int length);       // returns command on success
int GetDCHU_Data_Buffer(int command, uint8_t* buffer /*256*/);
int GetDCHU_Data_Integer(int command, int* value);
int ReadAppSettings(int page, int offset, int length, uint8_t* buffer);   // Clevo app settings store
int WriteAppSettings(int page, int offset, int length, uint8_t* buffer);
int SetDCHU_DataEx(int command, uint8_t* data, int length, uint8_t* out);
```

Most setters take a 32-bit little-endian argument (`length = 4`).

## Read commands

| Command | Buffer contents |
|---|---|
| `0x0C` fan info | `[2..3]` CPU fan period (big endian), `[4..5]` GPU fan, `[6..7]` GPU2 fan · RPM = `2156250 / period` · `[16]` CPU duty (0–255), `[18]` CPU °C, `[19]` GPU duty, `[21]` GPU °C, `[22]` GPU2 duty, `[24]` GPU2 °C |
| `0x0D` specs | `[0x0F]` keyboard type: `0x00` none, `0x01` white, `0x02` 3-zone RGB, `0x06` 1-zone RGB, `0xF3` per-key RGB |

## Keyboard – command `0x67` (103)

| Argument | Meaning |
|---|---|
| `0xF0BBRRGG` | color zone 0 (single zone / left). Note the byte order **B, R, G** |
| `0xF1BBRRGG`, `0xF2BBRRGG` | zone 1 (center), zone 2 (right) on 3-zone keyboards |
| `0xF40000LL` | brightness `LL` = 0–255 |
| `0xE007F001` / `0xE0003001` | backlight on / off |
| `0x1002A000` | hardware effect: breathe |
| `0x33010000` | cycle |
| `0xB0000000` | wave |
| `0x80000000` | dance |
| `0xA0000000` | flash |
| `0x70000000` | random color |
| `0x90000000` | tempo |

Sending a color after a hardware effect returns to a static color.
Single-zone LEDs are red/blue heavy; ClevoControl applies a configurable gain (default R 71 %, G 100 %, B 78 %).

## Fans

| Command | Argument | Meaning |
|---|---|---|
| `0x79` (121) | `0x01000000` | fan mode auto |
| `0x79` | `0x01000001` | fan mode max |
| `0x68` (104) | `0x00GGGGCC` | fixed duty per fan (CPU, GPU1, GPU2 bytes, 0–255) |

A duty written with `0x68` is **not** cancelled by "auto" while the EC already thinks it is in auto mode.
Switching to max and back to auto (`0x01000001` → `0x01000000`) resets it reliably. `0x69` alone did not.

## Performance profile – command `0x79`, sub-command `0x19`

Argument `0x190000VV`, where `VV` is simply Clevo's UI index (verified from the IL of Clevo's Fn-key app):

| Profile | UI index (`ReadAppSettings(1, 1)`) = EC value |
|---|---|
| Quiet | 0 |
| Power saving | 1 |
| Performance | 2 (Clevo adds `0x80` when "turbo fan" is enabled, `0x40` for an unknown model option) |
| Entertainment | 3 |

> ClevoControl 0.1.0 used a wrong, permuted mapping (Performance was sent as Quiet). Fixed in 0.1.1.

Clevo's apps also store the UI index with `WriteAppSettings(1, 1, 1, …)`; ClevoControl does the same so both stay in sync.

## Other `0x79` sub-commands seen in Clevo's apps (not used)

`0x18`, `0x1A` (airplane mode), `0x22`, `0x27`, `0x28`, `0x29`, `0x2A`, `0x2C`, `0x2D`. Their meaning is unknown; the strings in Clevo's apps are encrypted.
They are deliberately **not** probed – writing unknown EC commands can misconfigure the hardware.

## App settings store (read only in ClevoControl)

| Page, offset | Meaning |
|---|---|
| 1, 1 | performance profile (UI index) |
| 2, 32 | keyboard mode in Clevo's app |
| 2, 35 | keyboard brightness level in Clevo's app |
| 4, 5 | fan mode in Clevo's app |

## SetDCHU_DataEx – command `4` (request/response, 256 bytes each)

Only exported by the InsydeDCHU.dll copies inside Clevo's Store apps (not by the driver-store copy).
ClevoControl copies such a DLL into `%LOCALAPPDATA%\ClevoControl\dchu` on first use.
The first request byte is a sub-command (seen in Clevo's Control Center):

| Sub | Direction | Meaning |
|---|---|---|
| `30` | read | FlexiCharger: `[0]` enabled, `[1]` start %, `[2]` stop %, `[16..31]` allowed start values, `[32..47]` allowed stop values (all zero = not supported) |
| `31` | write | FlexiCharger: request `[31, enabled, start, stop]` |
| `7`, `8`, `21`, `22`, `24`, `25`, `26` | ? | other Control Center features, not used |

On the N9x0TD_TF the FlexiCharger response is all zero (not supported via software; the BIOS option is the only way there).

## Cooling modes (no EC involved)

Written to the active Windows power plan for AC and DC:
`PERFBOOSTMODE` (`be337238-0d82-4146-a960-4f3749d470c7`, 0 = off) and
`PROCTHROTTLEMAX` (`bc5038f7-23e0-4960-96da-33abaf5935ec`, percent). The original values are saved and restored by “Normal”.

## Linux (tuxedo-drivers)

On Linux there is no `InsydeDCHU.dll`. The open-source [tuxedo-drivers](https://github.com/tuxedocomputers/tuxedo-drivers)
modules `clevo_acpi` / `clevo_wmi` send the same EC commands as Clevo's Windows driver and expose them in two places.
ClevoControl's implementation is `src/ClevoControl.Core/Hardware/Linux/`.

### `/dev/tuxedo_io` (ioctl)

Requests use the kernel's `_IOR` / `_IOW` encoding. The argument is always a pointer to a 32-bit value
(or a string buffer), so the encoded size is 8 bytes on 64-bit systems.

| Request | Number | Data | Underlying EC command |
|---|---|---|---|
| HW check Clevo | `_IOR(0xEC, 0x05)` = `0x8008EC05` | out: `1` if a Clevo interface was found | – |
| Module version | `_IOR(0xEC, 0x00)` = `0x8008EC00` | out: string | – |
| Interface name | `_IOR(0xED, 0x00)` = `0x8008ED00` | out: `clevo_acpi` / `clevo_wmi` | – |
| Fan info 1/2/3 | `_IOR(0xED, 0x10…0x12)` | out: bits 0–7 duty (0–255), bits 16–23 temperature °C (signed) | `0x63`, `0x64`, `0x6E` |
| Fan speed | `_IOW(0xEE, 0x10)` = `0x4008EE10` | in: fan 0 + fan 1 × 256 + fan 2 × 65536 (one byte per fan, 0–255) | `0x68` |
| Fans auto | `_IOW(0xEE, 0x11)` = `0x4008EE11` | in: bit mask of fans, `0x0F` = all | `0x69` |
| Performance profile | `_IOW(0xEE, 0x15)` = `0x4008EE15` | in: profile value, same as on Windows (0–3) | `0x79`, sub-command `0x19` |

Driver behaviour worth knowing:

* **Fan speed:** duties below 31 become 0 (fan off), 31–62 become 63.
* **No RPM:** only duty and temperature are reported.
* **No profile getter:** the active performance profile cannot be read back.
* **Fan 3** reports a temperature ≤ 1 °C when it does not exist.
* **"Fans auto" only sends `0x69`.** On Windows (N9x0TD_TF), `0x69` alone did not reliably cancel a duty written
  with `0x68`, which is why the Windows backend goes through option `0x79`/`0x01` (max → auto) instead.
  `tuxedo_io` does not expose that option, so on Linux this still has to be verified: after leaving a custom
  curve, check that the fans react to load again.
* **Fans auto uses `0x69` only.** On Windows (N9x0TD_TF) `0x69` alone did not reliably cancel a duty written with
  `0x68`, which is why the Windows backend goes through option `0x79`/`0x01` instead. `tuxedo_io` does not expose
  option `0x01`, so on Linux this still has to be verified: after leaving a custom curve, check that the fans react
  to load again.

### Keyboard (LED class)

| Keyboard | Device(s) | Attributes |
|---|---|---|
| 1-zone RGB | `/sys/class/leds/rgb:kbd_backlight` | `multi_intensity` (order given by `multi_index`), `brightness` 0–255 |
| 3-zone RGB | `rgb:kbd_backlight`, `rgb:kbd_backlight_1`, `rgb:kbd_backlight_2` | same, one device per zone |
| White | `/sys/class/leds/white:kbd_backlight` | `brightness` 0–`max_brightness` (2 or 5) |

* The driver detects the keyboard type from byte `0x0F` of the spec buffer (`0x0D`), like ClevoControl on Windows.
* Color goes to EC command `0x67` with the same `0xF0BBRRGG` encoding; brightness uses the firmware brightness (`0xF4…`).
* On **1-zone** keyboards the driver dims red to 180/255 and blue to 200/255. ClevoControl applies its own
  (adjustable) white balance and therefore reverses the driver's factors before writing.
* The EC's own animations (`0x67` effect arguments) are not exposed.

### Charge limit

Standard power-supply attributes, if the battery driver offers them:
`/sys/class/power_supply/BAT*/charge_control_start_threshold` and `charge_control_end_threshold`.
