# AGENTS.md — FiiO K11 R2R Hardware Safety & Engineering Guide

This repository develops **Variant A** control software for the **FiiO K11 R2R** DAC/Amp (`FW M79` on `GD32F303RE` + `SA9312L` USB Audio Bridge):
- `firmware/patcher/` — pure-Python `3.12+` firmware patcher (zero third-party runtime dependencies) targeting official `k11R2R_app1_m79.hex`.
- `tray/` — C# / `.NET 8` (`net8.0-windows`) Windows tray application and USB/Audio diagnostics.

Every agent and developer working in this repository MUST follow the hardware safety rules below to avoid wedging the `SA9312L` USB bridge, soft-bricking the `GD32F303RE` MCU, or outputting unsafe full-scale line-level volume.

---

## 1. Critical USB & Hardware Safety Rules (Mandatory)

### 1.1 NEVER Poll High USB String Descriptors (`Str[>=7]` Crash Incident)
- **Incident Root Cause (`16:27:16.018`):** Probing `VID_2972&PID_0099` with `IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION` across USB String Descriptor indices `0..15` caused `Str[7]` to return malformed zeroes (`'00000000000000000\x00...'`) and crashed/reset the `SA9312L` EP0 control state machine on `Str[8..15]`. Windows logged `Microsoft-Windows-Kernel-PnP/Device Management` Event `ID=1010` (`surprise removed`, `Count of devices removed: 6`) at `16:27:16.018` and tore down the audio endpoints (`16:27:16.046..060`).
- Because stock `M79` lacks a `PB2` watchdog to reset `SA9312L`, an EP0 wedge leaves the DAC invisible to the host PC until the USB cable or mains power is physically cycled (or the DAC is moved to another USB port/controller).
- **Strict Rule:**
  - **NEVER** send brute-force or passive `IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION` / `GET_DESCRIPTOR (String)` sweeps for string indices `>= 7` (or unrequested descriptor indices) to `SA9312L`.
  - Enumerate the DAC exclusively via passive OS APIs: CoreAudio `MMDeviceEnumerator` (`IAudioEndpointVolume`, FriendlyName/DeviceId), Windows PnP / Device Manager properties, and standard HID opening by `VID_2972&PID_0099` (`HidD_GetAttributes` / `HidD_GetPreparsedData`).

### 1.2 NEVER Fuzz or Blind-Write `SA9312L` HID Collections (`COL01` / `COL02`)
- `VID_2972&PID_0099` exposes:
  - UAC2 Audio (`usbaudio2.sys`), Feature Unit `id=10` (Ch1/Ch2 Volume `-63..0 dB`, `1 dB` step; Master Mute), Interrupt/Isochronous USB Audio (`EP 0x81 IN` present on interrupt pipe).
  - HID `COL01` (`UsagePage=0x000c`, `ReportID=1`, `In=32B`, `Out=16B`).
  - HID `COL02` (`UsagePage=0xff00`, `ReportID=2`, `Feat=65B`).
- `SA9312L` runs closed vendor firmware and may interpret vendor-specific HID reports internally (including SPI flash / EEPROM / bridge configuration operations) before or without forwarding to the `GD32F303RE`.
- **Strict Rule:**
  - Do **NOT** send arbitrary or fuzzed `WriteFile` (`COL01` Out) or `HidD_SetFeature` (`COL02` Feature) reports in production tools.
  - Only run controlled single-shot HID tests during `Stage 0` with live `GD32` `I2C2` capture active so forwarded packets can be distinguished from `SA9312L`-consumed reports.
  - **Verified Stage 0 Live Finding:** HID `COL01` (`WriteFile` 16B) and `COL02` (`HidD_SetFeature` 65B) reports are absorbed by `SA9312L` and never reach `GD32` `I2C2`. By contrast, UAC2 `IAudioEndpointVolume` (`Ch0`, `Ch1`, `Mute`) reliably forwards `[len=3, 0x20/0x21/0x22, val]` over `I2C2` (`0x40005800`, 7-bit `0x6b` / 8-bit `0xd6`), AND `SA9312L` passes PCM transparently over I2S with zero bridge attenuation when `GD32` suppresses those `I2C2` packets (`stage0-diag-transparent`).
  - Therefore `full` profile uses a zero-click UAC2 escape preamble (`Ch0=-62 dB`, `Ch1=-63 dB`, `Ch0=-61 dB`) + alternating `Ch0`/`Ch1` 4-bit toggle-encoded nibbles (`-1..-32 dB`) + XOR-`0xA5` + `Ch0=-60 dB` commit to tunnel RAM commands cleanly without audio drops.

### 1.3 Bootloader & Flash Update Safety (`GD32_MCU_HID_IAP_Programmer.exe`)
- Official `M79` application lives at `0x08008000..0x0803718f` (`192,912` bytes).
- **NEVER** overwrite or erase `< 0x08008000` (protected `32 KB` HID IAP bootloader).
- **NEVER** send IAP command `0x06` in bootloader mode; it wedges the K11 R2R USB OUT pipe until the user re-enters `UPDATE -> Y` on the hardware menu.
- Free flash begins at **`0x08037200`** (GD32F303RE flash page size is `2048` bytes; the bootloader erases `ceil(len / 2048)` pages starting at `0x08008000`).
- **NEVER** invoke flashing tools autonomously from agent scripts; flashing firmware MUST remain an explicit manual/user-confirmed step.

### 1.4 NEVER Write Custom User Configs to MCU Flash
- **Strict Rule:** Do **NOT** write custom LCD text, RGB themes/colors, or tray settings to MCU internal flash (`0x0807f000` or custom extra flash pages).
- All user preferences live in PC JSON (`tray` app config) and are synced into `GD32` **RAM** on tray startup and USB reconnect.

### 1.5 NEVER Re-Enable `LO` Mode by Default
- `LO` (fixed line-out, state value `1` in `[0x20000398 + 7]`) bypasses attenuation and sends full-scale signal to outputs.
- Output selection in patched firmware and tray UI MUST restrict toggling to **`PO` (`4`) <-> `PRE` (`2`)** only, in both double-press and the `OUT` settings menu.

---

## 2. Host USB Topology Notes (`MSI B450-A PRO`)

Observed on the target Windows host:
- **`Hub_#0001` (`USB\ROOT_HUB30\5&4087d53&0&0`)** -> `PCI\VEN_1022&DEV_43D5` (AMD B450 Chipset xHCI controller; shares ports with keyboard/other chipset peripherals).
- **`Hub_#0002` (`USB\ROOT_HUB30\5&310f9c93&0&0`)** -> `PCI\VEN_1022&DEV_149C` (AMD Ryzen CPU-direct xHCI controller; noticeably more stable under sensitive USB devices).
- **Power Plan Setting:** `AMD Ryzen™ Balanced` had `USB Selective Suspend` (`48e6b7a6-50f5-4782-a5d4-53bb8f07e226`) enabled (`0x00000001`, `IdleInWorkingState = 1`).

**Operational guidance:** Recommend plugging K11 R2R into a rear port wired to the CPU-direct `DEV_149C` (`Hub_#0002`) controller and keeping USB Selective Suspend disabled on the active power plan when troubleshooting intermittent drops.

---

## 3. Verified `FW M79` Memory & Symbol Map (`0x08008000` Base)

All addresses below were directly verified from `firmware/official/K11 R2R M79 Firmware/k11R2R_app1_m79.hex`:

- **Binary Bounds & Stack:**
  - App start / Vector table: `0x08008000` (`vec[0]` initial MSP = `0x2000b410`)
  - Last non-`0xFF` byte: `0x0803718f` (`192,912` bytes total)
  - Free flash hook area: `0x08037200+`
- **Output Mode (`PO=4`, `PRE=2`, `LO=1`):**
  - Mode byte: `[0x20000398 + 7]`
  - Double-press `LO` opcode: `0x0800a050` (`40 f0 01 00` = `orr r0, r0, #1`; patch to `#4` for `PO`)
  - Settings `OUT` menu opcodes: `0x0800aed6` (`orr r0, r0, #1` -> `#4`) and `0x0800af12` (`orr r0, r0, #1` -> `#2`)
  - Apply output / notify UI: `0x080150b8` (`0x08014974`), `xSemaphoreGive([0x2000033c])` (`0x0802061c`), queues `0x200005e8` / `0x200005ec` (`0x0801f8b0`)
- **Segment LCD Driver:**
  - Flag/handle ref: `[0x20000398 + 0x48]`
  - String printer: `0x0800f814` (`void lcd_print_str(const char *s)`)
  - Helper / glyph mapper: `0x0800da64`, `0x0800db68(pos 0..6, ch)` (`43` supported characters: `" -/0123456789<ABCDEFGHIJKLMNOPQRSTUVWXYZ\\di"`)
  - **Verified Layout:** All `pos 0..6` write `pos * 7` bytes into `0x20000dd8[0..48]` on the single 7-character main LCD row (lower/primary row); bytes `[49..54]` (`0x31..0x36`) drive fixed status glass icons (`OUT PRE` sets `[0x35]=1` `LO/SE` + `[0x36]=2` `PRE`; `OUT PO` sets `[0x35]=2`), flushed via `0x0800db20`.
  - UI formatting loop: `0x08014b8c..0x08014d32`
- **WS2812 RGB Driver (`PA9` bitband `0x422101a4`, `24`-bit GRB on wire):**
  - Task `rgbSet`: `0x08018c91` -> `0x080113e0` -> `0x08010da4` -> `0x080111f0(src=r5, dst=0x20000d80)` (`src` presets use `[intensity, B, G, R]`, reordered by `0x080111f0` into DMA buf `0x20000d80` `[intensity, R, G, B]`; `0x08020bec` scales each channel by `intensity / 255.0` and transmits `G, R, B` MSB-first on `PA9`). Standby getter: `0x08015dac` (non-zero in power-off/standby).
- **`I2C2` (`0x40005800`, slave `0xd6` / `0x6b`) & Volume Engine:**
  - `I2C2` RX ISR/callback: `0x08017c68` (buffer `0x20000d98`, queues `0x20000628` / `0x2000062c`)
  - Command parser `decode_get`: `0x0801302d` (`0x20` -> `0x20000678+7`, `0x21` -> `+9`, `0x22` -> `+0xb`, `0x30` -> `+0xd`)
  - Volume compute & apply: `0x0801faf4` (`vol_calc`) -> `0x08012db8` -> `0x0801ac9c` + `0x0801cc50`
- **Reset & USB Detect / `SA9312L` Reset Pins:**
  - `NVIC_SystemReset`: `0x0800eb1c..0x0800eb38` (`SCB->AIRCR = 0x05fa0004`)
  - `decode_getTask`: `0x080135e4`
  - `decode_usbDetTask`: `0x0801402c` (`0x080140ec..0x0801410e` `PC15` bounce -> `0x080140b0` -> `0x080140c6` task delete/lockup)
  - `PB2` control (`SA9312L` reset): `0x0801c984` (LOW) / `0x0801cbcc` (HIGH)

---

## 4. Build, Tooling & Verification Conventions

- **Available Toolchain:**
  - `python` (`3.12.0`) — `firmware/patcher` MUST have zero third-party runtime dependencies (unit/disassembly tests may optionally use `capstone` if installed).
  - `dotnet` (`8.0.417` SDK, `Microsoft.WindowsDesktop.App 8.0.23` runtime) — `tray/` targets `net8.0-windows`.
  - `arm-none-eabi-gcc` is **NOT** installed; `firmware/patcher/thumb.py` provides a pure-Python Thumb/Thumb-2 micro-assembler whose emitted machine code is cross-checked by tests.
- **PowerShell 5.1 Warning:** Inline double-quoted strings expand `$` variables. Keep Python/diagnostic scripts in `.py` / `.ps1` files rather than complex inline quotes.
- **Never fabricate verification results:** After any edit to `firmware/patcher/` or `tray/`, run the exact test suite and `dotnet build` and verify machine code disassembly before finishing.
