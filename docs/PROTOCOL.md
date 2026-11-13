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

Argument `0x190000VV`. Clevo's UI index → EC value (from Clevo's Fn-key app):

| Profile | UI index (`ReadAppSettings(1, 1)`) | EC value |
|---|---|---|
| Quiet | 0 | 3 |
| Power saving | 1 | 1 |
| Performance | 2 | 0 |
| Entertainment (default) | 3 | 2 |

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
