# Why there is no overclocking / undervolting (yet)

**CPU** voltage offsets, turbo ratios and power limits (PL1/PL2) live in model-specific registers
(MSRs) and MMIO, which only kernel-mode code can write. Typical tools solve this with a driver:

* **WinRing0** (used by many older tools) has a well-known vulnerability (CVE-2020-14979) that lets any
  program gain kernel access. Since 2025 Microsoft Defender flags it as `HackTool:Win32/Winring0`.
  Bundling it would make ClevoControl itself look like malware, so it will not be used.
* **ThrottleStop / Intel XTU** ship their own drivers, which may not be redistributed.
* **PawnIO** is a newer, signed driver designed as a safe WinRing0 replacement (used by
  LibreHardwareMonitor). It is installed separately by the user. Supporting it is a possible future
  feature, starting read-only (checking whether undervolting / power limits are locked by the BIOS –
  on many 9th/10th-gen laptops undervolting is locked because of the "Plundervolt" fix).

**NVIDIA GPU** core/memory offsets are possible from user mode through NVAPI (what MSI Afterburner
does), without any extra driver. This is a candidate for a future, clearly marked "advanced" feature.

Until then: use ThrottleStop (CPU) and MSI Afterburner (GPU) alongside ClevoControl.
