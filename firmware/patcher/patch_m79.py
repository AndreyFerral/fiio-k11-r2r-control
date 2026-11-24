"""Firmware patcher CLI & code generator for FiiO K11 R2R (FW M79)."""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
import sys

from ihex import IntelHexImage, dump_hex, load_hex, sha256_bytes, write_hex
import m79_map as m79
from thumb import (
    Asm,
    LR,
    PC,
    R0,
    R1,
    R2,
    R3,
    R4,
    R5,
    R6,
    R7,
    encode_b_w,
    encode_bl,
    encode_orr_imm8,
)

# Offsets inside unused SRAM block at m79.CUSTOM_RAM_BASE (0x2000C000)
RAM_MAGIC = 0x00            # u32: 0x5231314B ("K11R")
RAM_PKT_COUNT = 0x04        # u32: total non-empty I2C2 packets
RAM_EVENT_SEQ = 0x08        # u32: sequence number for non-heartbeat I2C2 packets
RAM_LAST_EVENT = 0x0C       # u8[12]: [0]=len, [1..8]=raw packet
RAM_LAST_UNKNOWN = 0x18     # u8[12]: [0]=len, [1..8]=raw unknown packet
RAM_UNKNOWN_SEQ = 0x24      # u32: count of unknown opcodes seen
RAM_LAST_SHOWN_SEQ = 0x28   # u32: last rendered event_seq
RAM_DIAG_START_TICK = 0x2C  # u32: tick when current popup started
RAM_DIAG_ACTIVE = 0x30      # u8: 1 while diag popup is shown
RAM_LCD_REENTRANT = 0x31    # u8: 1 inside hook's own FN_LCD_PRINT_STR call
RAM_LAST_TAG = 0x32         # u8: 'I' or 'U'
RAM_CUSTOM_LCD_EN = 0x33    # u8: 1 when custom text overlay enabled (full profile)
RAM_LCD_BUF = 0x34          # char[8]: 7 chars + NUL
RAM_LAST_PAGE = 0x3C        # u32: 0 or 1 alternating display page
RAM_CUSTOM_TEXT = 0x40      # char[8]: persistent user text (full profile)
RAM_LAST_NATIVE_TICK = 0x48 # u32: tick of last native LCD update
RAM_REVERT_DELAY_MS = 0x4C  # u32: auto-revert delay in ms (default 2500)
RAM_SHOWING_CUSTOM = 0x50   # u8: 1 when custom text is currently displayed
RAM_CUSTOM_RGB_MODE = 0x51  # u8: 0=stock, 1=solid, 2=breathing, 3=rainbow, 4=chase
RAM_RGB_DIRTY = 0x52        # u8: 1 when custom RGB settings changed
RAM_CMD_NIB_TMP = 0x53      # u8: high nibble << 4 during nibble reassembly
RAM_RGB_LEDS = 0x54         # u8[12]: 3 x [intensity, R, G, B]
RAM_RGB_PREV = 0x60         # u8[12]: last sent WS2812 DMA frame [intensity, R, G, B] x 3
RAM_RGB_LAST_TICK = 0x6C    # u32: last RGB animation update tick
RAM_RGB_PHASE = 0x70        # u32: 0..255 cyclic animation phase
RAM_KNOCK_STAGE = 0x74      # u8: 0=idle, 1=got Ch0 -62dB, 2=got Ch1 -63dB
RAM_CMD_ACTIVE = 0x75       # u8: 1 while decoding nibble payload
RAM_CMD_NIB_IDX = 0x76      # u8: nibble counter (0..31)
RAM_EXEC_PENDING = 0x77     # u8: 1 when validated packet is ready in RAM_EXEC_BUF
RAM_KNOCK_TICK = 0x78       # u32: tick of last preamble knock packet
RAM_CMD_TICK = 0x7C         # u32: tick of last payload packet
RAM_CMD_BUF = 0x80          # u8[16]: ISR nibble reassembly buffer
RAM_EXEC_BUF = 0x90         # u8[16]: validated command packet for lcdTask
RAM_EXEC_LEN = 0xA0         # u8: byte count in RAM_EXEC_BUF (incl checksum)
RAM_CMD_ERR_CNT = 0xA1      # u8: protocol framing error count
RAM_CMD_OK_CNT = 0xA2       # u8: valid command commit count
RAM_RGB_INTENSITY = 0xA3    # u8: user RGB brightness 0..255
RAM_RGB_WORK = 0xA4         # u8[12]: scratch frame in rgb_hook
RAM_HIDE_TOP_ICONS = 0xB0   # u8: 1 = blank upper status segments 0x31..0x36 during flush
RAM_CUSTOM_LCD_MODE = 0xB1  # u8: 0 = ASCII (0x02), 1 = static masks (0x07), 2 = multi-frame anim (0x08)
RAM_ANIM_FRAME_COUNT = 0xB2 # u8: number of animation frames (1..4)
RAM_ANIM_CUR_IDX = 0xB3     # u8: currently displayed animation frame index (0..3)
RAM_RGB_HW = 0xB4           # u8[12]: gamma-calibrated WS2812 frame [255, R, G, B] x 3
RAM_CUSTOM_MASKS = 0xC0     # u8[64]: 4 x 16B slots (each slot uses 13B for 49 packed 2-bit LCD cells)
RAM_ANIM_STEP_MS = 0x100    # u32: animation frame interval in ms
RAM_ANIM_LAST_TICK = 0x104  # u32: tick of last animation frame switch
RAM_SCREEN_OFF = 0x108      # u8: 1 = Screen-Off mode (blank all segments & zero backlight PWM at idle)
RAM_BL_FORCED_OFF = 0x109   # u8: 1 = backlight PWM is currently forced to 0 by Screen-Off
RAM_TOTAL_SIZE = 0x110


def _build_srgb_gamma_lut() -> bytes:
    """IEC 61966-2-1 sRGB EOTF LUT mapping perceptual 0..255 -> linear WS2812 PWM 0..255."""
    out = bytearray(256)
    for u in range(1, 256):
        s = u / 255.0
        lin = (s / 12.92) if s <= 0.04045 else (((s + 0.055) / 1.055) ** 2.4)
        out[u] = max(0, min(255, int(round(lin * 255.0))))
    return bytes(out)


RGB_GAMMA_LUT = _build_srgb_gamma_lut()


@dataclass(frozen=True)
class PatchResult:
    profile: str
    input_sha256: str
    output_bin_sha256: str
    output_size: int
    blob_start: int
    blob_end: int
    patched_sites: tuple[str, ...]
    symbols: dict[str, int]
    image: IntelHexImage


def _apply_patch_site(buf: bytearray, site: m79.PatchSite, new_bytes: bytes) -> None:
    if len(new_bytes) != len(site.expected):
        raise ValueError(
            f"Patch size mismatch at {site.name}: expected {len(site.expected)}, got {len(new_bytes)}"
        )
    off = site.addr - m79.BASE_ADDR
    cur = bytes(buf[off : off + len(site.expected)])
    if cur != site.expected:
        raise ValueError(
            f"Pre-patch verification failed at {site.name} (0x{site.addr:08X}): "
            f"found {cur.hex()}, expected {site.expected.hex()}"
        )
    buf[off : off + len(new_bytes)] = new_bytes


def _emit_copy3w(a: Asm, dst_base: int, dst_off: int, src_base: int, src_off: int, tmp: int = R0) -> None:
    """Copy 3 aligned words between structs via LDR/STR (imm 0..4095)."""
    for word_off in (0, 4, 8):
        a.ldr_imm(tmp, src_base, src_off + word_off)
        a.str_imm(tmp, dst_base, dst_off + word_off)


def _emit_i2c2_rx_hook_diag(a: Asm, transparent_vol: bool) -> None:
    """Stage 0 I2C2 packet sniffer & optional volume suppressor."""
    a.label("i2c2_rx_hook")
    a.add_sp_imm(R0, 8)
    a.push([R4, R5, R6, LR])
    a.mov_reg(R4, R0)  # r4 = &[len, b1..b8]
    a.bl("ensure_ram_init")
    a.ldr_u32(R5, m79.CUSTOM_RAM_BASE)
    a.ldrb_imm(R6, R4, 0)  # len
    a.cmp_imm(R6, 0)
    a.b_cond_w("eq", "i2c_exit")

    # pkt_count++
    a.ldr_imm(R0, R5, RAM_PKT_COUNT)
    a.adds_imm3(R0, R0, 1)
    a.str_imm(R0, R5, RAM_PKT_COUNT)

    # Scan odd offsets i = 1, 3, 5, 7 while i < len and i <= 7
    a.movs(R2, 1)
    a.movs(R3, 0)  # bit0 = known UAC2 (20/21/22/30), bit1 = unknown opcode
    a.label("i2c_scan_loop")
    a.cmp_reg(R2, R6)
    a.b_cond("hs", "i2c_scan_done")
    a.cmp_imm(R2, 7)
    a.b_cond("hi", "i2c_scan_done")
    a.ldrb_reg(R0, R4, R2)
    a.cmp_imm(R0, 0x10)
    a.b_cond("eq", "i2c_scan_next")
    a.cmp_imm(R0, 0x11)
    a.b_cond("eq", "i2c_scan_next")
    a.cmp_imm(R0, 0x1E)
    a.b_cond("eq", "i2c_scan_next")
    a.cmp_imm(R0, 0x20)
    a.b_cond("eq", "i2c_mark_uac")
    a.cmp_imm(R0, 0x21)
    a.b_cond("eq", "i2c_mark_uac")
    a.cmp_imm(R0, 0x22)
    a.b_cond("eq", "i2c_mark_uac")
    a.cmp_imm(R0, 0x30)
    a.b_cond("eq", "i2c_mark_uac")
    a.movs(R1, 2)
    a.orrs_reg(R3, R1)
    a.b("i2c_scan_next")
    a.label("i2c_mark_uac")
    a.movs(R1, 1)
    a.orrs_reg(R3, R1)
    a.label("i2c_scan_next")
    a.adds_imm3(R2, R2, 2)
    a.b("i2c_scan_loop")

    a.label("i2c_scan_done")
    a.cbz(R3, "i2c_exit")

    # Copy incoming packet into RAM_LAST_EVENT
    a.mov_reg(R0, R5)
    a.adds_imm8(R0, RAM_LAST_EVENT)
    a.mov_reg(R1, R4)
    a.bl("copy_pkt12")

    a.movs(R0, 0x49)  # 'I'
    a.strb_imm(R0, R5, RAM_LAST_TAG)

    a.movs(R1, 2)
    a.ands_reg(R1, R3)
    a.cbz(R1, "i2c_bump_seq")

    # Unknown opcode seen: also latch to RAM_LAST_UNKNOWN, tag 'U', unknown_seq++
    a.movs(R0, 0x55)  # 'U'
    a.strb_imm(R0, R5, RAM_LAST_TAG)
    a.mov_reg(R0, R5)
    a.adds_imm8(R0, RAM_LAST_UNKNOWN)
    a.mov_reg(R1, R4)
    a.bl("copy_pkt12")
    a.ldr_imm(R0, R5, RAM_UNKNOWN_SEQ)
    a.adds_imm3(R0, R0, 1)
    a.str_imm(R0, R5, RAM_UNKNOWN_SEQ)

    a.label("i2c_bump_seq")
    a.ldr_imm(R0, R5, RAM_EVENT_SEQ)
    a.adds_imm3(R0, R0, 1)
    a.str_imm(R0, R5, RAM_EVENT_SEQ)

    if transparent_vol:
        a.movs(R1, 1)
        a.ands_reg(R1, R3)
        a.cbz(R1, "i2c_exit")
        a.movs(R0, 0)
        a.strb_imm(R0, R4, 0)
        a.strb_imm(R0, R4, 1)
        a.strb_imm(R0, R4, 3)

    a.label("i2c_exit")
    a.movs(R0, 0)
    a.str_sp(R0, 4)     # set popped r5 = 0
    a.ldrb_imm(R0, R4, 1)  # r0 = pkt[1]
    a.pop([R4, R5, R6, PC])
    a.flush_pool()


def _emit_i2c2_rx_hook_full(a: Asm) -> None:
    """Stage 1 zero-click UAC2 knock+nibble decoder with transparent volume passthrough."""
    a.label("i2c2_rx_hook")
    a.add_sp_imm(R0, 8)
    a.push([R4, R5, R6, R7, LR])
    a.mov_reg(R4, R0)  # r4 = &[len, b1..b8]
    a.sp_sub(12)       # sp+0=len, sp+4=loop_i, sp+8=kept_any
    a.bl("ensure_ram_init")
    a.ldr_u32(R5, m79.CUSTOM_RAM_BASE)

    a.ldrb_imm(R0, R4, 0)
    a.str_sp(R0, 0)
    a.cmp_imm(R0, 0)
    a.b_cond_w("eq", "i2c_full_exit")

    # pkt_count++
    a.ldr_imm(R0, R5, RAM_PKT_COUNT)
    a.adds_imm3(R0, R0, 1)
    a.str_imm(R0, R5, RAM_PKT_COUNT)

    a.bl(m79.FN_TASK_GET_TICK_FROM_ISR)
    a.mov_reg(R7, R0)  # r7 = now_tick

    # Command active watchdog (500 ms)
    a.ldrb_imm(R0, R5, RAM_CMD_ACTIVE)
    a.cmp_imm(R0, 0)
    a.b_cond("eq", "i2c_full_chk_knock_wd")
    a.ldr_imm(R1, R5, RAM_CMD_TICK)
    a.subs_reg(R0, R7, R1)
    a.movw(R1, 500)
    a.cmp_reg(R0, R1)
    a.b_cond("lo", "i2c_full_chk_knock_wd")
    a.movs(R0, 0)
    a.strb_imm(R0, R5, RAM_CMD_ACTIVE)
    a.strb_imm(R0, R5, RAM_KNOCK_STAGE)

    # Knock preamble watchdog (200 ms)
    a.label("i2c_full_chk_knock_wd")
    a.ldrb_imm(R0, R5, RAM_KNOCK_STAGE)
    a.cmp_imm(R0, 0)
    a.b_cond("eq", "i2c_full_loop_init")
    a.ldr_imm(R1, R5, RAM_KNOCK_TICK)
    a.subs_reg(R0, R7, R1)
    a.cmp_imm(R0, 200)
    a.b_cond("lo", "i2c_full_loop_init")
    a.movs(R0, 0)
    a.strb_imm(R0, R5, RAM_KNOCK_STAGE)

    a.label("i2c_full_loop_init")
    a.movs(R0, 0)
    a.str_sp(R0, 8)  # kept_any = 0
    a.movs(R6, 1)
    a.str_sp(R6, 4)  # loop_i = 1

    a.label("i2c_full_loop")
    a.ldr_sp(R6, 4)
    a.cmp_imm(R6, 7)
    a.b_cond_w("hi", "i2c_full_loop_end")
    a.adds_imm3(R0, R6, 1)
    a.ldr_sp(R1, 0)
    a.cmp_reg(R0, R1)
    a.b_cond_w("hs", "i2c_full_loop_end")

    a.ldrb_reg(R0, R4, R6)      # op = pkt[i]
    a.adds_imm3(R2, R6, 1)
    a.ldrb_reg(R1, R4, R2)      # val = pkt[i+1]
    a.cmp_imm(R0, 0x20)
    a.b_cond("eq", "i2c_full_is_uac")
    a.cmp_imm(R0, 0x21)
    a.b_cond("eq", "i2c_full_is_uac")
    a.b_w("i2c_full_keep")

    a.label("i2c_full_is_uac")
    a.negs(R1, R1)
    a.uxtb(R1, R1)  # k = (-val) & 0xFF
    a.ldrb_imm(R2, R5, RAM_CMD_ACTIVE)
    a.cmp_imm(R2, 0)
    a.b_cond_w("eq", "i2c_full_inactive")

    # Active command stream
    a.str_imm(R7, R5, RAM_CMD_TICK)
    a.cmp_imm(R0, 0x20)
    a.b_cond("ne", "i2c_full_check_nibble")
    a.cmp_imm(R1, 60)
    a.b_cond("ne", "i2c_full_check_nibble")

    # Commit sentinel (Ch0 = -60 dB)
    a.ldrb_imm(R2, R5, RAM_CMD_NIB_IDX)
    a.and_imm8(R0, R2, 1)
    a.cmp_imm(R0, 0)
    a.b_cond_w("ne", "i2c_full_commit_err")
    a.cmp_imm(R2, 4)
    a.b_cond_w("lo", "i2c_full_commit_err")
    a.cmp_imm(R2, 32)
    a.b_cond_w("hi", "i2c_full_commit_err")
    a.lsrs_imm(R2, R2, 1)  # r2 = byte_len (2..16)

    a.movs(R3, 0)
    a.movs(R0, 0)
    a.label("i2c_full_xor_loop")
    a.mov_reg(R1, R5)
    a.adds_imm8(R1, RAM_CMD_BUF)
    a.adds_reg(R1, R1, R0)
    a.ldrb_imm(R1, R1, 0)
    a.eors_reg(R3, R1)
    a.adds_imm3(R0, R0, 1)
    a.cmp_reg(R0, R2)
    a.b_cond("lo", "i2c_full_xor_loop")

    a.cmp_imm(R3, 0xA5)
    a.b_cond_w("ne", "i2c_full_commit_err")

    a.ldrb_imm(R0, R5, RAM_EXEC_PENDING)
    a.cmp_imm(R0, 0)
    a.b_cond_w("ne", "i2c_full_commit_end")

    a.movs(R0, 0)
    a.label("i2c_full_copy_loop")
    a.mov_reg(R1, R5)
    a.adds_imm8(R1, RAM_CMD_BUF)
    a.adds_reg(R1, R1, R0)
    a.ldrb_imm(R1, R1, 0)
    a.mov_reg(R3, R5)
    a.adds_imm8(R3, RAM_EXEC_BUF)
    a.adds_reg(R3, R3, R0)
    a.strb_imm(R1, R3, 0)
    a.adds_imm3(R0, R0, 1)
    a.cmp_reg(R0, R2)
    a.b_cond("lo", "i2c_full_copy_loop")

    a.strb_imm(R2, R5, RAM_EXEC_LEN)
    a.movs(R0, 1)
    a.strb_imm(R0, R5, RAM_EXEC_PENDING)
    a.ldrb_imm(R0, R5, RAM_CMD_OK_CNT)
    a.adds_imm3(R0, R0, 1)
    a.strb_imm(R0, R5, RAM_CMD_OK_CNT)
    a.b("i2c_full_commit_end")

    a.label("i2c_full_commit_err")
    a.ldrb_imm(R0, R5, RAM_CMD_ERR_CNT)
    a.adds_imm3(R0, R0, 1)
    a.strb_imm(R0, R5, RAM_CMD_ERR_CNT)

    a.label("i2c_full_commit_end")
    a.movs(R0, 0)
    a.strb_imm(R0, R5, RAM_CMD_ACTIVE)
    a.b_w("i2c_full_consume")

    # Payload nibble (-1..-32 dB -> k=1..32)
    a.label("i2c_full_check_nibble")
    a.cmp_imm(R1, 1)
    a.b_cond("lo", "i2c_full_proto_err")
    a.cmp_imm(R1, 32)
    a.b_cond("hi", "i2c_full_proto_err")
    a.subs_imm3(R1, R1, 1)  # 0..31 = (toggle << 4) | nibble

    a.ldrb_imm(R2, R5, RAM_CMD_NIB_IDX)
    a.cmp_imm(R2, 32)
    a.b_cond("hs", "i2c_full_proto_err")

    # Verify channel parity: (op & 1) == (idx & 1)
    a.and_imm8(R0, R0, 1)
    a.and_imm8(R3, R2, 1)
    a.cmp_reg(R0, R3)
    a.b_cond("ne", "i2c_full_proto_err")

    # Verify toggle bit: (r1 >> 4) == ((idx >> 1) & 1)
    a.lsrs_imm(R0, R1, 4)
    a.lsrs_imm(R3, R2, 1)
    a.and_imm8(R3, R3, 1)
    a.cmp_reg(R0, R3)
    a.b_cond("ne", "i2c_full_proto_err")

    a.and_imm8(R1, R1, 0x0F)
    a.and_imm8(R0, R2, 1)
    a.cmp_imm(R0, 0)
    a.b_cond("ne", "i2c_full_low_nib")
    a.lsls_imm(R1, R1, 4)
    a.strb_imm(R1, R5, RAM_CMD_NIB_TMP)
    a.b("i2c_full_adv_nib")

    a.label("i2c_full_low_nib")
    a.ldrb_imm(R0, R5, RAM_CMD_NIB_TMP)
    a.orrs_reg(R1, R0)
    a.lsrs_imm(R0, R2, 1)
    a.mov_reg(R3, R5)
    a.adds_imm8(R3, RAM_CMD_BUF)
    a.adds_reg(R3, R3, R0)
    a.strb_imm(R1, R3, 0)

    a.label("i2c_full_adv_nib")
    a.adds_imm3(R2, R2, 1)
    a.strb_imm(R2, R5, RAM_CMD_NIB_IDX)
    a.b_w("i2c_full_consume")

    a.label("i2c_full_proto_err")
    a.movs(R0, 0)
    a.strb_imm(R0, R5, RAM_CMD_ACTIVE)
    a.ldrb_imm(R0, R5, RAM_CMD_ERR_CNT)
    a.adds_imm3(R0, R0, 1)
    a.strb_imm(R0, R5, RAM_CMD_ERR_CNT)
    a.b_w("i2c_full_consume")

    # Inactive knock sequence: Ch0=-62, Ch1=-63, Ch0=-61
    a.label("i2c_full_inactive")
    a.ldrb_imm(R2, R5, RAM_KNOCK_STAGE)
    a.cmp_imm(R2, 0)
    a.b_cond("ne", "i2c_full_knock_s1")
    a.cmp_imm(R0, 0x20)
    a.b_cond("ne", "i2c_full_keep")
    a.cmp_imm(R1, 62)
    a.b_cond("ne", "i2c_full_keep")
    a.movs(R0, 1)
    a.strb_imm(R0, R5, RAM_KNOCK_STAGE)
    a.str_imm(R7, R5, RAM_KNOCK_TICK)
    a.b("i2c_full_consume")

    a.label("i2c_full_knock_s1")
    a.cmp_imm(R2, 1)
    a.b_cond("ne", "i2c_full_knock_s2")
    a.cmp_imm(R0, 0x21)
    a.b_cond("ne", "i2c_full_reset_knock_keep")
    a.cmp_imm(R1, 63)
    a.b_cond("ne", "i2c_full_reset_knock_keep")
    a.movs(R0, 2)
    a.strb_imm(R0, R5, RAM_KNOCK_STAGE)
    a.str_imm(R7, R5, RAM_KNOCK_TICK)
    a.b("i2c_full_consume")

    a.label("i2c_full_knock_s2")
    a.movs(R3, 0)
    a.strb_imm(R3, R5, RAM_KNOCK_STAGE)
    a.cmp_imm(R0, 0x20)
    a.b_cond("ne", "i2c_full_keep")
    a.cmp_imm(R1, 61)
    a.b_cond("ne", "i2c_full_keep")
    a.movs(R0, 1)
    a.strb_imm(R0, R5, RAM_CMD_ACTIVE)
    a.movs(R0, 0)
    a.strb_imm(R0, R5, RAM_CMD_NIB_IDX)
    a.str_imm(R7, R5, RAM_CMD_TICK)
    a.b("i2c_full_consume")

    a.label("i2c_full_reset_knock_keep")
    a.movs(R0, 0)
    a.strb_imm(R0, R5, RAM_KNOCK_STAGE)

    a.label("i2c_full_keep")
    a.movs(R0, 1)
    a.str_sp(R0, 8)
    a.b("i2c_full_next")

    a.label("i2c_full_consume")
    a.movs(R0, 0)
    a.strb_reg(R0, R4, R6)
    a.adds_imm3(R1, R6, 1)
    a.strb_reg(R0, R4, R1)

    a.label("i2c_full_next")
    a.adds_imm3(R6, R6, 2)
    a.str_sp(R6, 4)
    a.b_w("i2c_full_loop")

    a.label("i2c_full_loop_end")
    a.ldr_sp(R0, 8)
    a.cmp_imm(R0, 0)
    a.b_cond("ne", "i2c_full_exit")
    a.movs(R0, 0)
    a.strb_imm(R0, R4, 0)  # Clear packet length so decode_get skips

    a.label("i2c_full_exit")
    a.sp_add(12)
    a.movs(R0, 0)
    a.str_sp(R0, 4)     # set popped r5 = 0
    a.ldrb_imm(R0, R4, 1)
    a.pop([R4, R5, R6, R7, PC])
    a.flush_pool()


def _emit_rgb_hook(a: Asm) -> None:
    """Custom WS2812 RGB controller at 5ms rgbSet cadence (Stock/Solid/Breathing/Rainbow/Chase)."""
    a.label("rgb_hook")
    a.push([R4, R5, R6, R7, LR])
    a.mov_reg(R4, R0)  # src (stock computed 12B)
    a.mov_reg(R5, R1)  # dst (0x20000D80)
    a.bl("ensure_ram_init")
    a.ldr_u32(R6, m79.CUSTOM_RAM_BASE)

    a.ldrb_imm(R0, R6, RAM_CUSTOM_RGB_MODE)
    a.cmp_imm(R0, 0)
    a.b_cond("ne", "rgb_check_standby")
    a.mov_reg(R0, R4)
    a.mov_reg(R1, R5)
    a.bl(m79.FN_RGB_REORDER_COPY)
    a.ldrb_imm(R0, R6, RAM_RGB_DIRTY)
    a.cmp_imm(R0, 0)
    a.b_cond("eq", "rgb_stock_ret")
    a.movs(R0, 0)
    a.strb_imm(R0, R6, RAM_RGB_DIRTY)
    a.mov_reg(R0, R5)
    a.bl(m79.FN_WS2812_SEND)
    a.label("rgb_stock_ret")
    a.pop([R4, R5, R6, R7, PC])

    a.label("rgb_check_standby")
    a.bl(m79.FN_GET_STANDBY_STATE)
    a.cmp_imm(R0, 0)
    a.b_cond("eq", "rgb_check_rate")
    # Power-off / standby -> target all zeros
    a.movs(R0, 0)
    a.str_imm(R0, R6, RAM_RGB_WORK + 0)
    a.str_imm(R0, R6, RAM_RGB_WORK + 4)
    a.str_imm(R0, R6, RAM_RGB_WORK + 8)
    a.b_w("rgb_commit_if_changed")

    a.label("rgb_check_rate")
    a.bl(m79.FN_TASK_GET_TICK)
    a.mov_reg(R7, R0)
    a.ldrb_imm(R0, R6, RAM_RGB_DIRTY)
    a.cmp_imm(R0, 0)
    a.b_cond("ne", "rgb_compute")
    a.ldr_imm(R1, R6, RAM_RGB_LAST_TICK)
    a.subs_reg(R0, R7, R1)
    a.cmp_imm(R0, 20)
    a.b_cond("hs", "rgb_compute")
    _emit_copy3w(a, R5, 0, R6, RAM_RGB_HW)
    a.pop([R4, R5, R6, R7, PC])

    a.label("rgb_compute")
    a.str_imm(R7, R6, RAM_RGB_LAST_TICK)
    a.ldr_imm(R7, R6, RAM_RGB_PHASE)
    a.adds_imm3(R7, R7, 2)
    a.uxtb(R7, R7)
    a.str_imm(R7, R6, RAM_RGB_PHASE)

    # Start from RAM_RGB_LEDS
    _emit_copy3w(a, R6, RAM_RGB_WORK, R6, RAM_RGB_LEDS)

    a.ldrb_imm(R0, R6, RAM_CUSTOM_RGB_MODE)
    a.cmp_imm(R0, 1)
    a.b_cond_w("eq", "rgb_commit_if_changed")
    a.cmp_imm(R0, 2)
    a.b_cond("eq", "rgb_mode_breathing")
    a.cmp_imm(R0, 3)
    a.b_cond("eq", "rgb_mode_rainbow")
    a.b_w("rgb_mode_chase")

    # Mode 2: Breathing (triangle wave scaling RAM_RGB_INTENSITY)
    a.label("rgb_mode_breathing")
    a.cmp_imm(R7, 128)
    a.b_cond("hs", "rgb_breath_down")
    a.lsls_imm(R0, R7, 1)
    a.b("rgb_breath_scale")
    a.label("rgb_breath_down")
    a.movs(R0, 255)
    a.subs_reg(R0, R0, R7)
    a.lsls_imm(R0, R0, 1)
    a.label("rgb_breath_scale")
    a.ldrb_imm(R1, R6, RAM_RGB_INTENSITY)
    a.muls_reg(R0, R1)
    a.lsrs_imm(R0, R0, 8)
    a.strb_imm(R0, R6, RAM_RGB_WORK + 0)
    a.strb_imm(R0, R6, RAM_RGB_WORK + 4)
    a.strb_imm(R0, R6, RAM_RGB_WORK + 8)
    a.b_w("rgb_commit_if_changed")

    # Mode 3: Rainbow (120-degree phase shift per LED across 3 zones, full-value HSV triangle)
    def _emit_hsv_pair(idx_tag: str) -> None:
        # Input R0 = sector pos (0..84); outputs R1 = up (0..255), R2 = down (255..0)
        a.movs(R1, 6)
        a.muls_reg(R1, R0)           # r1 = t2 = local * 6 (0..504)
        a.movs(R2, 255)
        a.adds_imm8(R2, 255)         # r2 = 510
        a.subs_reg(R2, R2, R1)       # r2 = 510 - local * 6
        a.cmp_imm(R1, 255)
        a.b_cond("ls", f"{idx_tag}_up_ok")
        a.movs(R1, 255)
        a.label(f"{idx_tag}_up_ok")
        a.cmp_imm(R2, 255)
        a.b_cond("ls", f"{idx_tag}_dn_ok")
        a.movs(R2, 255)
        a.label(f"{idx_tag}_dn_ok")

    a.label("rgb_mode_rainbow")
    a.movs(R4, 0)
    a.label("rgb_rainbow_loop")
    a.lsls_imm(R3, R4, 2)
    a.adds_reg(R3, R3, R6)  # r3 = base + led*4
    a.ldrb_imm(R0, R6, RAM_RGB_INTENSITY)
    a.strb_imm(R0, R3, RAM_RGB_WORK + 0)

    a.movs(R0, 2)
    a.subs_reg(R0, R0, R4)  # physical strip runs Right(0)->Center(1)->Left(2); convert to visual Left->Right
    a.movs(R1, 85)
    a.muls_reg(R0, R1)
    a.adds_reg(R0, R0, R7)
    a.uxtb(R0, R0)  # h = (phase + (2 - led)*85) & 0xFF

    a.cmp_imm(R0, 85)
    a.b_cond("hs", "rgb_rb_s1")
    _emit_hsv_pair("s0")
    a.strb_imm(R2, R3, RAM_RGB_WORK + 1)  # R = down
    a.strb_imm(R1, R3, RAM_RGB_WORK + 2)  # G = up
    a.movs(R2, 0)
    a.strb_imm(R2, R3, RAM_RGB_WORK + 3)  # B = 0
    a.b("rgb_rb_next")

    a.label("rgb_rb_s1")
    a.cmp_imm(R0, 170)
    a.b_cond("hs", "rgb_rb_s2")
    a.subs_imm8(R0, 85)
    _emit_hsv_pair("s1")
    a.movs(R0, 0)
    a.strb_imm(R0, R3, RAM_RGB_WORK + 1)  # R = 0
    a.strb_imm(R2, R3, RAM_RGB_WORK + 2)  # G = down
    a.strb_imm(R1, R3, RAM_RGB_WORK + 3)  # B = up
    a.b("rgb_rb_next")

    a.label("rgb_rb_s2")
    a.subs_imm8(R0, 170)
    _emit_hsv_pair("s2")
    a.strb_imm(R1, R3, RAM_RGB_WORK + 1)  # R = up
    a.movs(R0, 0)
    a.strb_imm(R0, R3, RAM_RGB_WORK + 2)  # G = 0
    a.strb_imm(R2, R3, RAM_RGB_WORK + 3)  # B = down

    a.label("rgb_rb_next")
    a.adds_imm3(R4, R4, 1)
    a.cmp_imm(R4, 3)
    a.b_cond("lo", "rgb_rainbow_loop")
    a.b("rgb_commit_if_changed")

    # Mode 4: Chase (single bright LED stepping visually Left(2)->Center(1)->Right(0) with dim background)
    a.label("rgb_mode_chase")
    a.movs(R1, 2)
    a.cmp_imm(R7, 85)
    a.b_cond("lo", "rgb_chase_apply")
    a.movs(R1, 1)
    a.cmp_imm(R7, 170)
    a.b_cond("lo", "rgb_chase_apply")
    a.movs(R1, 0)

    a.label("rgb_chase_apply")
    a.ldrb_imm(R3, R6, RAM_RGB_INTENSITY)
    a.lsrs_imm(R4, R3, 3)  # dim tail = intensity / 8
    a.movs(R2, 0)
    a.label("rgb_chase_loop")
    a.lsls_imm(R0, R2, 2)
    a.adds_reg(R0, R0, R6)
    a.cmp_reg(R2, R1)
    a.b_cond("ne", "rgb_chase_dim")
    a.strb_imm(R3, R0, RAM_RGB_WORK + 0)
    a.b("rgb_chase_next")
    a.label("rgb_chase_dim")
    a.strb_imm(R4, R0, RAM_RGB_WORK + 0)
    a.label("rgb_chase_next")
    a.adds_imm3(R2, R2, 1)
    a.cmp_imm(R2, 3)
    a.b_cond("lo", "rgb_chase_loop")

    # Commit to DMA buf and bit-bang PA9 only on change or dirty
    a.label("rgb_commit_if_changed")
    a.ldrb_imm(R0, R6, RAM_RGB_DIRTY)
    a.cmp_imm(R0, 0)
    a.b_cond("ne", "rgb_do_send")
    for w in (0, 4, 8):
        a.ldr_imm(R0, R6, RAM_RGB_WORK + w)
        a.ldr_imm(R1, R6, RAM_RGB_PREV + w)
        a.cmp_reg(R0, R1)
        a.b_cond("ne", "rgb_do_send")
    _emit_copy3w(a, R5, 0, R6, RAM_RGB_HW)
    a.pop([R4, R5, R6, R7, PC])

    a.label("rgb_do_send")
    _emit_copy3w(a, R6, RAM_RGB_PREV, R6, RAM_RGB_WORK)
    # Apply IEC 61966-2-1 sRGB gamma LUT on pre-multiplied (ch * intensity) into RAM_RGB_HW
    a.adr_label(R4, "rgb_gamma_lut")
    a.movs(R7, 0)
    a.label("rgb_cal_loop")
    a.adds_reg(R3, R6, R7)
    a.ldrb_imm(R1, R3, RAM_RGB_WORK + 0)
    a.cmp_imm(R1, 0)
    a.b_cond("eq", "rgb_cal_z")
    a.movs(R0, 255)
    a.b("rgb_cal_w0")
    a.label("rgb_cal_z")
    a.movs(R0, 0)
    a.label("rgb_cal_w0")
    a.strb_imm(R0, R3, RAM_RGB_HW + 0)
    for ch in (1, 2, 3):
        a.ldrb_imm(R0, R3, RAM_RGB_WORK + ch)
        a.muls_reg(R0, R1)
        a.adds_imm8(R0, 127)
        a.lsrs_imm(R2, R0, 8)
        a.adds_reg(R0, R0, R2)
        a.adds_imm3(R0, R0, 1)
        a.lsrs_imm(R0, R0, 8)
        a.ldrb_reg(R0, R4, R0)
        a.strb_imm(R0, R3, RAM_RGB_HW + ch)
    a.adds_imm3(R7, R7, 4)
    a.cmp_imm(R7, 12)
    a.b_cond("lo", "rgb_cal_loop")

    _emit_copy3w(a, R5, 0, R6, RAM_RGB_HW)
    a.movs(R0, 0)
    a.strb_imm(R0, R6, RAM_RGB_DIRTY)
    a.mov_reg(R0, R5)
    a.bl(m79.FN_WS2812_SEND)
    a.pop([R4, R5, R6, R7, PC])
    a.flush_pool()
    a.align4()
    a.label("rgb_gamma_lut")
    a.raw(RGB_GAMMA_LUT)


def _build_free_flash_blob(profile: str, transparent_vol: bool) -> tuple[bytes, dict[str, int]]:
    """Assemble all hook routines placed at m79.FREE_FLASH_START (0x08037200)."""
    a = Asm(m79.FREE_FLASH_START)
    full_profile = profile == "full"

    # 1. ensure_ram_init: lazy/boot SRAM control block init (clobbers r0..r3, preserves r4..r7)
    a.label("ensure_ram_init")
    a.ldr_u32(R0, m79.CUSTOM_RAM_BASE)
    a.ldr_imm(R1, R0, RAM_MAGIC)
    a.ldr_u32(R2, m79.CUSTOM_RAM_MAGIC)
    a.cmp_reg(R1, R2)
    a.b_cond("ne", "ram_zero_init")
    a.bx(LR)

    a.label("ram_zero_init")
    a.movw(R3, RAM_TOTAL_SIZE)
    a.add_reg(R3, R0)
    a.movs(R1, 0)
    a.label("ram_zero_loop")
    a.str_imm(R1, R0, 0)
    a.adds_imm3(R0, R0, 4)
    a.cmp_reg(R0, R3)
    a.b_cond("lo", "ram_zero_loop")

    a.ldr_u32(R0, m79.CUSTOM_RAM_BASE)
    a.str_imm(R2, R0, RAM_MAGIC)
    if full_profile:
        a.movw(R1, 2500)
        a.str_imm(R1, R0, RAM_REVERT_DELAY_MS)
        a.movw(R1, 300)
        a.str_imm(R1, R0, RAM_ANIM_STEP_MS)
    a.bx(LR)
    a.flush_pool()

    # 2. write_hex2(r0=dst, r1=byte_val) -> writes 2 uppercase ASCII hex chars; clobbers r0..r3
    a.label("write_hex2")
    a.lsrs_imm(R2, R1, 4)
    a.cmp_imm(R2, 9)
    a.b_cond("hi", "hn_alpha")
    a.adds_imm8(R2, 0x30)
    a.b("hn_store")
    a.label("hn_alpha")
    a.adds_imm8(R2, 0x37)
    a.label("hn_store")
    a.strb_imm(R2, R0, 0)

    a.movs(R2, 0x0F)
    a.ands_reg(R2, R1)
    a.cmp_imm(R2, 9)
    a.b_cond("hi", "ln_alpha")
    a.adds_imm8(R2, 0x30)
    a.b("ln_store")
    a.label("ln_alpha")
    a.adds_imm8(R2, 0x37)
    a.label("ln_store")
    a.strb_imm(R2, R0, 1)
    a.bx(LR)

    # 3. copy_pkt12(r0=dst, r1=src) -> copies 12 bytes (3 words); clobbers r0..r3
    a.label("copy_pkt12")
    a.ldr_imm(R2, R1, 0)
    a.str_imm(R2, R0, 0)
    a.ldr_imm(R2, R1, 4)
    a.str_imm(R2, R0, 4)
    a.ldr_imm(R2, R1, 8)
    a.str_imm(R2, R0, 8)
    a.bx(LR)

    # 4. usb_bounce_fix: ensures PB2/bridge reset release (0x0801CBCC) before bouncing to 0x080140B0
    a.label("usb_bounce_fix")
    a.bl(m79.FN_UART_LOG_PRINT)
    a.bl(m79.FN_PB2_RELEASE)
    a.b_w(0x080140B0)

    # 5. boot_init_hook: called from 0x080142DE (replaces bl 0x08015D54; mov r4, r0)
    a.label("boot_init_hook")
    a.push([R1, LR])
    a.bl("ensure_ram_init")
    a.bl(m79.FN_GET_UI_STATE_PTR)
    a.mov_reg(R4, R0)
    a.pop([R1, PC])

    # 6. i2c2_rx_hook
    if full_profile:
        _emit_i2c2_rx_hook_full(a)
    else:
        _emit_i2c2_rx_hook_diag(a, transparent_vol=transparent_vol)

    # 7. lcd_print_hook: replaces push {r4,r5,r6,lr}; mov r5, r0 at 0x0800F814
    a.label("lcd_print_hook")
    a.push([R4, R5, R6, LR])
    a.mov_reg(R5, R0)  # r5 preserves const char *s
    a.bl("ensure_ram_init")
    a.ldr_u32(R4, m79.CUSTOM_RAM_BASE)
    a.ldrb_imm(R0, R4, RAM_LCD_REENTRANT)
    a.cbnz(R0, "lcd_hook_passthrough")
    if not full_profile:
        a.ldrb_imm(R0, R4, RAM_DIAG_ACTIVE)
        a.cbnz(R0, "lcd_hook_suppress")
    else:
        a.ldrb_imm(R0, R4, RAM_CUSTOM_LCD_EN)
        a.cbz(R0, "lcd_hook_passthrough")
        a.bl(m79.FN_TASK_GET_TICK)
        a.str_imm(R0, R4, RAM_LAST_NATIVE_TICK)
        a.movs(R0, 0)
        a.strb_imm(R0, R4, RAM_SHOWING_CUSTOM)
        a.ldrb_imm(R0, R4, RAM_BL_FORCED_OFF)
        a.cbz(R0, "lcd_hook_passthrough")
        a.movs(R0, 0)
        a.strb_imm(R0, R4, RAM_BL_FORCED_OFF)
        a.bl("restore_stock_backlight_helper")
    a.label("lcd_hook_passthrough")
    a.b_w(m79.FN_LCD_PRINT_STR + 4)

    if not full_profile:
        a.label("lcd_hook_suppress")
        a.pop([R4, R5, R6, PC])
    a.flush_pool()

    # 8. lcd_tick_hook: replaces bl xQueueReceive (0x08020518) at 0x08014514
    a.label("lcd_tick_hook")
    a.push([R4, R5, R6, LR])
    a.bl(m79.FN_QUEUE_RECEIVE)
    a.mov_reg(R4, R0)  # preserve queue status for caller
    a.bl("ensure_ram_init")
    a.ldr_u32(R5, m79.CUSTOM_RAM_BASE)
    a.bl(m79.FN_TASK_GET_TICK)
    a.mov_reg(R6, R0)  # now_tick

    if full_profile:
        # Execute validated host commands queued in RAM_EXEC_BUF (task context!)
        a.ldrb_imm(R0, R5, RAM_EXEC_PENDING)
        a.cmp_imm(R0, 0)
        a.b_cond_w("eq", "tick_check_custom")

        a.ldrb_imm(R0, R5, RAM_EXEC_BUF + 0)
        a.cmp_imm(R0, 0x01)
        a.b_cond_w("eq", "cmd_set_out")
        a.cmp_imm(R0, 0x02)
        a.b_cond_w("eq", "cmd_set_lcd")
        a.cmp_imm(R0, 0x03)
        a.b_cond_w("eq", "cmd_set_rgb")
        a.cmp_imm(R0, 0x04)
        a.b_cond_w("eq", "cmd_reboot")
        a.cmp_imm(R0, 0x05)
        a.b_cond_w("eq", "cmd_set_gain")
        a.cmp_imm(R0, 0x06)
        a.b_cond_w("eq", "cmd_set_filter")
        a.cmp_imm(R0, 0x07)
        a.b_cond_w("eq", "cmd_set_lcd_masks")
        a.cmp_imm(R0, 0x08)
        a.b_cond_w("eq", "cmd_set_lcd_anim_frame")
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_EXEC_PENDING)
        a.b_w("tick_done")

        # 0x01: Set Output Mode (PO=4, PRE=2, Toggle PO<->PRE=0xFF)
        a.label("cmd_set_out")
        a.ldrb_imm(R0, R5, RAM_EXEC_BUF + 1)
        a.movs(R1, 0)
        a.strb_imm(R1, R5, RAM_EXEC_PENDING)
        a.bl("set_out_mode_helper")
        a.b_w("tick_done")

        # 0x02: Set Custom LCD ([0x02, flags(bit0=en, bit1=hide_top_icons, bit2=screen_off), revert_ds, c0..c6, chk])
        a.label("cmd_set_lcd")
        a.ldrb_imm(R1, R5, RAM_EXEC_BUF + 1)
        a.and_imm8(R0, R1, 1)
        a.strb_imm(R0, R5, RAM_CUSTOM_LCD_EN)
        a.lsrs_imm(R0, R1, 1)
        a.and_imm8(R0, R0, 1)
        a.strb_imm(R0, R5, RAM_HIDE_TOP_ICONS)
        a.lsrs_imm(R0, R1, 2)
        a.and_imm8(R0, R0, 1)
        a.strb_imm(R0, R5, RAM_SCREEN_OFF)
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_CUSTOM_LCD_MODE)
        a.ldrb_imm(R1, R5, RAM_EXEC_BUF + 2)
        a.movs(R2, 100)
        a.muls_reg(R1, R2)
        a.str_imm(R1, R5, RAM_REVERT_DELAY_MS)
        a.movs(R2, 0)
        a.label("cmd_lcd_copy_loop")
        a.mov_reg(R1, R5)
        a.adds_imm8(R1, RAM_EXEC_BUF + 3)
        a.adds_reg(R1, R1, R2)
        a.ldrb_imm(R1, R1, 0)
        a.mov_reg(R3, R5)
        a.adds_imm8(R3, RAM_CUSTOM_TEXT)
        a.adds_reg(R3, R3, R2)
        a.strb_imm(R1, R3, 0)
        a.adds_imm3(R2, R2, 1)
        a.cmp_imm(R2, 7)
        a.b_cond("lo", "cmd_lcd_copy_loop")
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_CUSTOM_TEXT + 7)
        a.strb_imm(R0, R5, RAM_EXEC_PENDING)

        a.label("cmd_lcd_post_store")
        a.bl(m79.FN_GET_STANDBY_STATE)
        a.cmp_imm(R0, 0)
        a.b_cond("eq", "cmd_lcd_awake")
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_SHOWING_CUSTOM)
        a.str_imm(R6, R5, RAM_LAST_NATIVE_TICK)
        a.b_w("tick_done")

        a.label("cmd_lcd_awake")
        a.ldrb_imm(R0, R5, RAM_CUSTOM_LCD_EN)
        a.cmp_imm(R0, 0)
        a.b_cond("ne", "cmd_lcd_print_custom")
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_SHOWING_CUSTOM)
        a.strb_imm(R0, R5, RAM_SCREEN_OFF)
        a.ldrb_imm(R1, R5, RAM_BL_FORCED_OFF)
        a.cmp_imm(R1, 0)
        a.b_cond("eq", "cmd_lcd_redraw_native")
        a.strb_imm(R0, R5, RAM_BL_FORCED_OFF)
        a.bl("restore_stock_backlight_helper")
        a.label("cmd_lcd_redraw_native")
        a.bl(m79.FN_LCD_REDRAW_SCREEN)
        a.bl(m79.FN_RAW_LCD_FLUSH)
        a.b_w("tick_done")

        a.label("cmd_lcd_print_custom")
        a.str_imm(R6, R5, RAM_LAST_NATIVE_TICK)
        a.bl("render_custom_lcd_helper")
        a.b_w("tick_done")

        # 0x03: Set RGB ([0x03, mode, intensity, r0,g0,b0, r1,g1,b1, r2,g2,b2, chk])
        a.label("cmd_set_rgb")
        a.ldrb_imm(R0, R5, RAM_EXEC_BUF + 1)
        a.strb_imm(R0, R5, RAM_CUSTOM_RGB_MODE)
        a.ldrb_imm(R1, R5, RAM_EXEC_BUF + 2)
        a.strb_imm(R1, R5, RAM_RGB_INTENSITY)
        a.movs(R2, 0)
        a.label("cmd_rgb_led_loop")
        a.movs(R1, 3)
        a.muls_reg(R1, R2)
        a.adds_reg(R1, R1, R5)  # base + led*3
        a.lsls_imm(R3, R2, 2)
        a.adds_reg(R3, R3, R5)  # base + led*4
        a.ldrb_imm(R0, R5, RAM_RGB_INTENSITY)
        a.strb_imm(R0, R3, RAM_RGB_LEDS + 0)
        a.ldrb_imm(R0, R1, RAM_EXEC_BUF + 3)
        a.strb_imm(R0, R3, RAM_RGB_LEDS + 1)
        a.ldrb_imm(R0, R1, RAM_EXEC_BUF + 4)
        a.strb_imm(R0, R3, RAM_RGB_LEDS + 2)
        a.ldrb_imm(R0, R1, RAM_EXEC_BUF + 5)
        a.strb_imm(R0, R3, RAM_RGB_LEDS + 3)
        a.adds_imm3(R2, R2, 1)
        a.cmp_imm(R2, 3)
        a.b_cond("lo", "cmd_rgb_led_loop")
        a.movs(R0, 1)
        a.strb_imm(R0, R5, RAM_RGB_DIRTY)
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_EXEC_PENDING)
        a.b_w("tick_done")

        # 0x04: Reboot DAC
        a.label("cmd_reboot")
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_EXEC_PENDING)
        a.bl("reboot_dac_helper")
        a.b_w("tick_done")

        # 0x05: Set Gain ([0x05, val, chk]) where val: 0=L, 1=M, 2=H, 0xFF=cycle
        a.label("cmd_set_gain")
        a.ldrb_imm(R0, R5, RAM_EXEC_BUF + 1)
        a.movs(R1, 0)
        a.strb_imm(R1, R5, RAM_EXEC_PENDING)
        a.bl("set_gain_helper")
        a.b_w("tick_done")

        # 0x06: Set Sampling Filter ([0x06, val, chk]) where val: 0=NOS, !=0=OS, 0xFF=toggle
        a.label("cmd_set_filter")
        a.ldrb_imm(R0, R5, RAM_EXEC_BUF + 1)
        a.movs(R1, 0)
        a.strb_imm(R1, R5, RAM_EXEC_PENDING)
        a.bl("set_filter_helper")
        a.b_w("tick_done")

        # 0x07: Set Custom LCD 14-seg Masks ([0x07, p0..p12, revert_ds, chk])
        # p0..p12 pack 49 x 2-bit LCD cells [0..48]; p12 bit2 = enable, p12 bit3 = hide_top_icons.
        a.label("cmd_set_lcd_masks")
        a.ldrb_imm(R0, R5, RAM_EXEC_LEN)
        a.cmp_imm(R0, 15)
        a.b_cond("hs", "cmd_lcd_masks_len_ok")
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_EXEC_PENDING)
        a.b_w("tick_done")
        a.label("cmd_lcd_masks_len_ok")
        a.ldrb_imm(R1, R5, RAM_EXEC_BUF + 13)
        a.lsrs_imm(R0, R1, 2)
        a.and_imm8(R0, R0, 1)
        a.strb_imm(R0, R5, RAM_CUSTOM_LCD_EN)
        a.lsrs_imm(R0, R1, 3)
        a.and_imm8(R0, R0, 1)
        a.strb_imm(R0, R5, RAM_HIDE_TOP_ICONS)
        a.movs(R0, 1)
        a.strb_imm(R0, R5, RAM_CUSTOM_LCD_MODE)
        a.strb_imm(R0, R5, RAM_ANIM_FRAME_COUNT)
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_ANIM_CUR_IDX)
        a.strb_imm(R0, R5, RAM_SCREEN_OFF)
        a.ldrb_imm(R1, R5, RAM_EXEC_BUF + 14)
        a.movs(R2, 100)
        a.muls_reg(R1, R2)
        a.str_imm(R1, R5, RAM_REVERT_DELAY_MS)
        a.movs(R2, 0)
        a.label("cmd_lcd_masks_copy_loop")
        a.mov_reg(R1, R5)
        a.adds_imm8(R1, RAM_EXEC_BUF + 1)
        a.adds_reg(R1, R1, R2)
        a.ldrb_imm(R1, R1, 0)
        a.mov_reg(R3, R5)
        a.adds_imm8(R3, RAM_CUSTOM_MASKS)
        a.adds_reg(R3, R3, R2)
        a.strb_imm(R1, R3, 0)
        a.adds_imm3(R2, R2, 1)
        a.cmp_imm(R2, 13)
        a.b_cond("lo", "cmd_lcd_masks_copy_loop")
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_EXEC_PENDING)
        a.b_w("cmd_lcd_post_store")

        # 0x08: Set Animated LCD Frame ([0x08, p0..p12, val14, chk])
        # p12 bit2=enable, bit3=hide_top_icons, bits[5:4]=frameIdx(0..3), bits[7:6]=totalFrames-1(0..3).
        # val14: revert_ds on frame 0; step_10ms (min 5 => 50ms) on frame >= 1.
        a.label("cmd_set_lcd_anim_frame")
        a.ldrb_imm(R0, R5, RAM_EXEC_LEN)
        a.cmp_imm(R0, 15)
        a.b_cond("hs", "cmd_lcd_anim_len_ok")
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_EXEC_PENDING)
        a.b_w("tick_done")
        a.label("cmd_lcd_anim_len_ok")
        a.ldrb_imm(R1, R5, RAM_EXEC_BUF + 13)
        a.lsrs_imm(R0, R1, 4)
        a.and_imm8(R0, R0, 3)  # frameIdx (0..3)
        a.cmp_imm(R0, 0)
        a.b_cond("ne", "cmd_lcd_anim_set_step")
        a.ldrb_imm(R1, R5, RAM_EXEC_BUF + 14)
        a.movs(R2, 100)
        a.muls_reg(R1, R2)
        a.str_imm(R1, R5, RAM_REVERT_DELAY_MS)
        a.movs(R1, 1)
        a.strb_imm(R1, R5, RAM_ANIM_FRAME_COUNT)  # pause cycling until final frame finishes upload
        a.b("cmd_lcd_anim_copy_prep")
        a.label("cmd_lcd_anim_set_step")
        a.ldrb_imm(R1, R5, RAM_EXEC_BUF + 14)
        a.cmp_imm(R1, 5)
        a.b_cond("hs", "cmd_lcd_anim_step_ok")
        a.movs(R1, 5)
        a.label("cmd_lcd_anim_step_ok")
        a.movs(R2, 10)
        a.muls_reg(R1, R2)
        a.str_imm(R1, R5, RAM_ANIM_STEP_MS)

        a.label("cmd_lcd_anim_copy_prep")
        a.lsls_imm(R0, R0, 4)
        a.adds_imm8(R0, RAM_CUSTOM_MASKS)
        a.adds_reg(R0, R5, R0)  # r0 = dst slot base (RAM_CUSTOM_MASKS + frameIdx*16)
        a.movs(R2, 0)
        a.label("cmd_lcd_anim_copy_loop")
        a.mov_reg(R1, R5)
        a.adds_imm8(R1, RAM_EXEC_BUF + 1)
        a.adds_reg(R1, R1, R2)
        a.ldrb_imm(R1, R1, 0)
        a.strb_reg(R1, R0, R2)
        a.adds_imm3(R2, R2, 1)
        a.cmp_imm(R2, 13)
        a.b_cond("lo", "cmd_lcd_anim_copy_loop")

        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_EXEC_PENDING)

        a.ldrb_imm(R1, R5, RAM_EXEC_BUF + 13)
        a.lsrs_imm(R0, R1, 4)
        a.and_imm8(R0, R0, 3)  # frameIdx
        a.adds_imm3(R0, R0, 1) # frameIdx + 1
        a.lsrs_imm(R2, R1, 6)
        a.and_imm8(R2, R2, 3)
        a.adds_imm3(R2, R2, 1) # totalFrames (1..4)
        a.cmp_reg(R0, R2)
        a.b_cond("hs", "cmd_lcd_anim_commit")
        a.b_w("tick_done")

        a.label("cmd_lcd_anim_commit")
        a.lsrs_imm(R0, R1, 2)
        a.and_imm8(R0, R0, 1)
        a.strb_imm(R0, R5, RAM_CUSTOM_LCD_EN)
        a.lsrs_imm(R0, R1, 3)
        a.and_imm8(R0, R0, 1)
        a.strb_imm(R0, R5, RAM_HIDE_TOP_ICONS)
        a.strb_imm(R2, R5, RAM_ANIM_FRAME_COUNT)
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_ANIM_CUR_IDX)
        a.strb_imm(R0, R5, RAM_SCREEN_OFF)
        a.str_imm(R6, R5, RAM_ANIM_LAST_TICK)
        a.movs(R0, 1)
        a.cmp_imm(R2, 2)
        a.b_cond("lo", "cmd_lcd_anim_mode_set")
        a.movs(R0, 2)
        a.label("cmd_lcd_anim_mode_set")
        a.strb_imm(R0, R5, RAM_CUSTOM_LCD_MODE)
        a.b_w("cmd_lcd_post_store")
    else:
        # Stage 0 diagnostic hex popup
        a.ldr_imm(R0, R5, RAM_EVENT_SEQ)
        a.ldr_imm(R1, R5, RAM_LAST_SHOWN_SEQ)
        a.cmp_reg(R0, R1)
        a.b_cond("eq", "tick_check_diag")
        a.str_imm(R0, R5, RAM_LAST_SHOWN_SEQ)
        a.str_imm(R6, R5, RAM_DIAG_START_TICK)
        a.movs(R0, 1)
        a.strb_imm(R0, R5, RAM_DIAG_ACTIVE)
        a.movs(R0, 0xFF)
        a.str_imm(R0, R5, RAM_LAST_PAGE)

        a.label("tick_check_diag")
        a.ldrb_imm(R0, R5, RAM_DIAG_ACTIVE)
        a.cmp_imm(R0, 0)
        a.b_cond("eq", "tick_check_custom")

        a.ldr_imm(R1, R5, RAM_DIAG_START_TICK)
        a.subs_reg(R0, R6, R1)
        a.movw(R1, 4000)
        a.cmp_reg(R0, R1)
        a.b_cond("lo", "tick_render_diag")
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_DIAG_ACTIVE)
        a.bl(m79.FN_LCD_REDRAW_SCREEN)
        a.b("tick_done")

        a.label("tick_render_diag")
        a.lsrs_imm(R0, R6, 10)
        a.movs(R1, 1)
        a.ands_reg(R0, R1)
        a.ldr_imm(R1, R5, RAM_LAST_PAGE)
        a.cmp_reg(R0, R1)
        a.b_cond("eq", "tick_done")
        a.str_imm(R0, R5, RAM_LAST_PAGE)
        a.cbnz(R0, "diag_page1")

        # Page 0: "<b1><b2><tag><seq>"
        a.mov_reg(R0, R5)
        a.adds_imm8(R0, RAM_LCD_BUF)
        a.ldrb_imm(R1, R5, RAM_LAST_EVENT + 1)
        a.bl("write_hex2")

        a.mov_reg(R0, R5)
        a.adds_imm8(R0, RAM_LCD_BUF + 2)
        a.ldrb_imm(R1, R5, RAM_LAST_EVENT + 2)
        a.bl("write_hex2")

        a.ldrb_imm(R0, R5, RAM_LAST_TAG)
        a.strb_imm(R0, R5, RAM_LCD_BUF + 4)

        a.mov_reg(R0, R5)
        a.adds_imm8(R0, RAM_LCD_BUF + 5)
        a.ldrb_imm(R1, R5, RAM_EVENT_SEQ)
        a.bl("write_hex2")
        a.b("diag_flush_buf")

        a.label("diag_page1")
        # Page 1: "<b3><b4>L<len_nib>P"
        a.mov_reg(R0, R5)
        a.adds_imm8(R0, RAM_LCD_BUF)
        a.ldrb_imm(R1, R5, RAM_LAST_EVENT + 3)
        a.bl("write_hex2")

        a.mov_reg(R0, R5)
        a.adds_imm8(R0, RAM_LCD_BUF + 2)
        a.ldrb_imm(R1, R5, RAM_LAST_EVENT + 4)
        a.bl("write_hex2")

        a.movs(R0, 0x4C)
        a.strb_imm(R0, R5, RAM_LCD_BUF + 4)

        a.ldrb_imm(R1, R5, RAM_LAST_EVENT + 0)
        a.movs(R2, 0x0F)
        a.ands_reg(R1, R2)
        a.cmp_imm(R1, 9)
        a.b_cond("hi", "len_alpha")
        a.adds_imm8(R1, 0x30)
        a.b("len_store")
        a.label("len_alpha")
        a.adds_imm8(R1, 0x37)
        a.label("len_store")
        a.strb_imm(R1, R5, RAM_LCD_BUF + 5)

        a.movs(R0, 0x50)
        a.strb_imm(R0, R5, RAM_LCD_BUF + 6)

        a.label("diag_flush_buf")
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_LCD_BUF + 7)
        a.movs(R0, 1)
        a.strb_imm(R0, R5, RAM_LCD_REENTRANT)
        a.mov_reg(R0, R5)
        a.adds_imm8(R0, RAM_LCD_BUF)
        a.bl(m79.FN_LCD_PRINT_STR)
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_LCD_REENTRANT)
        a.b("tick_done")

    a.label("tick_check_custom")
    if full_profile:
        a.ldrb_imm(R0, R5, RAM_CUSTOM_LCD_EN)
        a.cmp_imm(R0, 0)
        a.b_cond("eq", "tick_done")
        a.ldrb_imm(R0, R5, RAM_SHOWING_CUSTOM)
        a.cmp_imm(R0, 0)
        a.b_cond("eq", "tick_wait_revert")

        # Already showing custom overlay: flip multi-frame animations cleanly at RAM_ANIM_STEP_MS
        a.ldrb_imm(R0, R5, RAM_CUSTOM_LCD_MODE)
        a.cmp_imm(R0, 2)
        a.b_cond("ne", "tick_done")
        a.ldrb_imm(R1, R5, RAM_ANIM_FRAME_COUNT)
        a.cmp_imm(R1, 2)
        a.b_cond("lo", "tick_done")
        a.bl(m79.FN_GET_STANDBY_STATE)
        a.cmp_imm(R0, 0)
        a.b_cond("eq", "tick_chk_anim_timer")
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_SHOWING_CUSTOM)
        a.str_imm(R6, R5, RAM_LAST_NATIVE_TICK)
        a.b("tick_done")

        a.label("tick_chk_anim_timer")
        a.ldr_imm(R1, R5, RAM_ANIM_LAST_TICK)
        a.subs_reg(R0, R6, R1)
        a.ldr_imm(R1, R5, RAM_ANIM_STEP_MS)
        a.cmp_reg(R0, R1)
        a.b_cond("lo", "tick_done")
        a.str_imm(R6, R5, RAM_ANIM_LAST_TICK)
        a.ldrb_imm(R0, R5, RAM_ANIM_CUR_IDX)
        a.adds_imm3(R0, R0, 1)
        a.ldrb_imm(R1, R5, RAM_ANIM_FRAME_COUNT)
        a.cmp_reg(R0, R1)
        a.b_cond("lo", "tick_anim_store_idx")
        a.movs(R0, 0)
        a.label("tick_anim_store_idx")
        a.strb_imm(R0, R5, RAM_ANIM_CUR_IDX)
        a.bl("render_custom_lcd_helper")
        a.b("tick_done")

        a.label("tick_wait_revert")
        a.bl(m79.FN_GET_STANDBY_STATE)
        a.cmp_imm(R0, 0)
        a.b_cond("eq", "tick_chk_custom_timer")
        a.str_imm(R6, R5, RAM_LAST_NATIVE_TICK)
        a.b("tick_done")
        a.label("tick_chk_custom_timer")
        a.ldr_imm(R1, R5, RAM_LAST_NATIVE_TICK)
        a.subs_reg(R0, R6, R1)
        a.ldr_imm(R1, R5, RAM_REVERT_DELAY_MS)
        a.cmp_reg(R0, R1)
        a.b_cond("lo", "tick_done")
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_ANIM_CUR_IDX)
        a.str_imm(R6, R5, RAM_ANIM_LAST_TICK)
        a.bl("render_custom_lcd_helper")

    a.label("tick_done")
    a.mov_reg(R0, R4)
    a.pop([R4, R5, R6, PC])
    a.flush_pool()

    # 9. Full-profile helpers (output mode toggle, RGB effect engine, DAC reboot)
    if full_profile:
        _emit_rgb_hook(a)

        # helper set_out_mode(r0 = 4 for PO, 2 for PRE, 0xFF for hardware toggle PO<->PRE)
        a.label("set_out_mode_helper")
        a.push([R4, LR])
        a.sp_sub(8)
        a.ldr_u32(R4, m79.DATA_OUT_STATE_STRUCT)
        a.ldrb_imm(R2, R4, 7)
        a.cmp_imm(R0, 0xFF)
        a.b_cond("ne", "out_explicit")
        # Hardware toggle uses actual DAC state so knob changes never desync PC hotkey/toggle.
        a.movs(R1, 4)
        a.and_imm8(R3, R2, 4)
        a.cmp_imm(R3, 0)
        a.b_cond("eq", "out_apply_mode")
        a.movs(R1, 2)
        a.b("out_apply_mode")

        a.label("out_explicit")
        a.movs(R1, 4)
        a.cmp_imm(R0, 2)
        a.b_cond("ne", "out_apply_mode")
        a.movs(R1, 2)

        a.label("out_apply_mode")
        a.and_imm8(R0, R2, 0x10)
        a.orrs_reg(R0, R1)
        a.strb_imm(R0, R4, 7)
        a.bl(m79.FN_OUT_MODE_APPLY)
        a.ldr_u32(R0, 0x2000033C)
        a.ldr_imm(R0, R0, 0)
        a.bl(m79.FN_SEMAPHORE_GIVE)
        a.movs(R0, 0)
        a.str_sp(R0, 0)
        a.movs(R3, 0)
        a.movs(R2, 0)
        a.add_sp_imm(R1, 0)
        a.ldr_u32(R0, 0x200005E8)
        a.ldr_imm(R0, R0, 0)
        a.bl(m79.FN_QUEUE_SEND)
        a.ldr_u32(R0, 0x200005EC)
        a.ldr_imm(R0, R0, 0)
        a.bl(m79.FN_QUEUE_NOTIFY)
        a.sp_add(8)
        a.pop([R4, PC])
        a.flush_pool()

        # helper set_gain_helper(r0 = 0(L), 1(M), 2(H), 0xFF(cycle L->M->H->L))
        a.label("set_gain_helper")
        a.push([R4, LR])
        a.sp_sub(8)
        a.ldr_u32(R4, m79.DATA_OUT_STATE_STRUCT)
        a.ldrb_imm(R1, R4, 4)
        a.cmp_imm(R0, 0xFF)
        a.b_cond("ne", "gain_explicit")
        a.adds_imm3(R0, R1, 1)
        a.cmp_imm(R0, 3)
        a.b_cond("lo", "gain_apply")
        a.movs(R0, 0)
        a.b("gain_apply")

        a.label("gain_explicit")
        a.cmp_imm(R0, 2)
        a.b_cond("ls", "gain_apply")
        a.movs(R0, 2)

        a.label("gain_apply")
        a.strb_imm(R0, R4, 4)
        a.movs(R0, 0)
        a.str_sp(R0, 0)
        a.movs(R3, 0)
        a.movs(R2, 0)
        a.add_sp_imm(R1, 0)
        a.ldr_u32(R0, m79.DATA_VOL_QUEUE_PTR)
        a.ldr_imm(R0, R0, 0)
        a.cbz(R0, "gain_skip_q")
        a.bl(m79.FN_QUEUE_SEND)
        a.label("gain_skip_q")
        a.ldr_u32(R0, m79.DATA_VOL_TASK_PTR)
        a.ldr_imm(R0, R0, 0)
        a.cbz(R0, "gain_done")
        a.bl(m79.FN_QUEUE_NOTIFY)
        a.label("gain_done")
        a.sp_add(8)
        a.pop([R4, PC])
        a.flush_pool()

        # helper set_filter_helper(r0 = 0(NOS), !=0(OS), 0xFF(toggle NOS<->OS))
        a.label("set_filter_helper")
        a.ldr_u32(R2, m79.DATA_OUT_STATE_STRUCT)
        a.ldrb_imm(R1, R2, 0x0F)
        a.cmp_imm(R0, 0xFF)
        a.b_cond("ne", "filter_explicit")
        a.cmp_imm(R1, 0)
        a.b_cond("eq", "filter_set_os")
        a.b("filter_set_nos")

        a.label("filter_explicit")
        a.cmp_imm(R0, 0)
        a.b_cond("ne", "filter_set_os")

        a.label("filter_set_nos")
        a.movs(R0, 0)
        a.b("filter_store")

        a.label("filter_set_os")
        a.movs(R0, 0x0C)

        a.label("filter_store")
        a.strb_imm(R0, R2, 0x0F)
        a.bx(LR)
        a.flush_pool()

        # helper reboot_dac() -> NVIC_SystemReset
        a.label("reboot_dac_helper")
        a.b_w(m79.FN_NVIC_SYSTEM_RESET)

        # backlight_hook: intercepts FN_SET_LCD_BACKLIGHT(r0 = float duty)
        a.label("backlight_hook")
        a.push([R4, R5, R6, LR])
        a.mov_reg(R4, R0)
        a.bl("ensure_ram_init")
        a.ldr_u32(R1, m79.CUSTOM_RAM_BASE)
        a.ldrb_imm(R0, R1, RAM_SCREEN_OFF)
        a.cmp_imm(R0, 0)
        a.b_cond("eq", "bl_hook_pass")
        a.ldrb_imm(R0, R1, RAM_CUSTOM_LCD_EN)
        a.cmp_imm(R0, 0)
        a.b_cond("eq", "bl_hook_pass")
        a.ldrb_imm(R0, R1, RAM_SHOWING_CUSTOM)
        a.cmp_imm(R0, 0)
        a.b_cond("eq", "bl_hook_pass")
        a.movs(R4, 0)
        a.label("bl_hook_pass")
        a.mov_reg(R0, R4)
        a.b_w(m79.FN_SET_LCD_BACKLIGHT + 4)

        # set_hw_backlight_raw(r0 = float duty): writes TIMER7 PWM via FN_SET_LCD_BACKLIGHT body
        a.label("set_hw_backlight_raw")
        a.push([R4, R5, R6, LR])
        a.mov_reg(R4, R0)
        a.b_w(m79.FN_SET_LCD_BACKLIGHT + 4)

        # restore_stock_backlight_helper(): re-applies DAC's LCD-B duty when awake and not timed out
        a.label("restore_stock_backlight_helper")
        a.push([R4, LR])
        a.bl(m79.FN_GET_STANDBY_STATE)
        a.cmp_imm(R0, 0)
        a.b_cond("ne", "restore_bl_done")
        a.ldr_u32(R1, m79.DATA_OUT_STATE_STRUCT)
        a.ldrb_imm(R0, R1, 0x13)
        a.cmp_imm(R0, 1)
        a.b_cond("ne", "restore_bl_done")
        a.ldrb_imm(R2, R1, 0x14)
        a.ldr_u32(R0, 0x3E4CCCCD)  # LCD-B 1 -> 0.2f
        a.cmp_imm(R2, 0)
        a.b_cond("eq", "restore_bl_set")
        a.ldr_u32(R0, 0x3F19999A)  # LCD-B 2 -> 0.6f
        a.cmp_imm(R2, 1)
        a.b_cond("eq", "restore_bl_set")
        a.ldr_u32(R0, 0x3F7D70A4)  # LCD-B 3 -> 0.99f
        a.label("restore_bl_set")
        a.bl("set_hw_backlight_raw")
        a.label("restore_bl_done")
        a.pop([R4, PC])
        a.flush_pool()

        # raw_lcd_write_hook: masks upper status icons [0x31..0x36] when RAM_HIDE_TOP_ICONS != 0 or Screen-Off idle
        a.label("raw_lcd_write_hook")
        a.cmp_imm(R4, 0x31)
        a.b_cond("lo", "raw_lcd_write_pass")
        a.ldr_u32(R1, m79.CUSTOM_RAM_BASE)
        a.ldr_imm(R2, R1, RAM_MAGIC)
        a.ldr_u32(R3, m79.CUSTOM_RAM_MAGIC)
        a.cmp_reg(R2, R3)
        a.b_cond("ne", "raw_lcd_write_pass")
        a.ldrb_imm(R2, R1, RAM_HIDE_TOP_ICONS)
        a.cmp_imm(R2, 0)
        a.b_cond("ne", "raw_lcd_write_zero")
        a.ldrb_imm(R2, R1, RAM_SCREEN_OFF)
        a.cmp_imm(R2, 0)
        a.b_cond("eq", "raw_lcd_write_pass")
        a.ldrb_imm(R2, R1, RAM_CUSTOM_LCD_EN)
        a.cmp_imm(R2, 0)
        a.b_cond("eq", "raw_lcd_write_pass")
        a.ldrb_imm(R2, R1, RAM_SHOWING_CUSTOM)
        a.cmp_imm(R2, 0)
        a.b_cond("eq", "raw_lcd_write_pass")
        a.label("raw_lcd_write_zero")
        a.movs(R0, 0)
        a.label("raw_lcd_write_pass")
        a.b_w(m79.FN_RAW_LCD_DATA_BYTE)
        a.flush_pool()

        # helper render_custom_lcd_helper() -> renders ASCII text (mode 0), static masks (mode 1), or anim slot cur_idx (mode 2)
        a.label("render_custom_lcd_helper")
        a.push([R4, R5, R6, LR])
        a.ldr_u32(R5, m79.CUSTOM_RAM_BASE)
        a.movs(R0, 1)
        a.strb_imm(R0, R5, RAM_SHOWING_CUSTOM)
        a.strb_imm(R0, R5, RAM_LCD_REENTRANT)
        a.ldrb_imm(R0, R5, RAM_CUSTOM_LCD_MODE)
        a.cmp_imm(R0, 0)
        a.b_cond("ne", "render_custom_masks")
        a.mov_reg(R0, R5)
        a.adds_imm8(R0, RAM_CUSTOM_TEXT)
        a.bl(m79.FN_LCD_PRINT_STR)
        a.b("render_custom_done")

        a.label("render_custom_masks")
        a.movs(R6, 0)
        a.cmp_imm(R0, 2)
        a.b_cond("ne", "render_masks_ptr")
        a.ldrb_imm(R6, R5, RAM_ANIM_CUR_IDX)
        a.and_imm8(R6, R6, 3)
        a.lsls_imm(R6, R6, 4)
        a.label("render_masks_ptr")
        a.adds_imm8(R6, RAM_CUSTOM_MASKS)
        a.adds_reg(R6, R5, R6)

        a.bl(m79.FN_RAW_LCD_BEGIN)
        a.ldr_u32(R4, m79.DATA_LCD_BUF)
        a.movs(R1, 0)
        a.label("render_masks_loop")
        a.lsrs_imm(R2, R1, 2)
        a.ldrb_reg(R2, R6, R2)
        a.and_imm8(R3, R1, 3)
        a.cmp_imm(R3, 0)
        a.b_cond("eq", "render_masks_store")
        a.lsls_imm(R3, R3, 1)
        a.lsrs_reg(R2, R3)
        a.label("render_masks_store")
        a.and_imm8(R2, R2, 3)
        a.strb_reg(R2, R4, R1)
        a.adds_imm3(R1, R1, 1)
        a.cmp_imm(R1, 49)
        a.b_cond("lo", "render_masks_loop")
        a.bl(m79.FN_RAW_LCD_FLUSH)

        a.label("render_custom_done")
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_LCD_REENTRANT)
        a.ldrb_imm(R0, R5, RAM_SCREEN_OFF)
        a.cmp_imm(R0, 0)
        a.b_cond("eq", "render_custom_chk_bl_wake")
        a.ldrb_imm(R0, R5, RAM_BL_FORCED_OFF)
        a.cmp_imm(R0, 0)
        a.b_cond("ne", "render_custom_exit")
        a.movs(R0, 1)
        a.strb_imm(R0, R5, RAM_BL_FORCED_OFF)
        a.movs(R0, 0)
        a.bl("set_hw_backlight_raw")
        a.b("render_custom_exit")
        a.label("render_custom_chk_bl_wake")
        a.ldrb_imm(R0, R5, RAM_BL_FORCED_OFF)
        a.cmp_imm(R0, 0)
        a.b_cond("eq", "render_custom_exit")
        a.movs(R0, 0)
        a.strb_imm(R0, R5, RAM_BL_FORCED_OFF)
        a.bl("restore_stock_backlight_helper")
        a.label("render_custom_exit")
        a.pop([R4, R5, R6, PC])
        a.flush_pool()

    blob = a.build()
    symbols = {
        k: a.addr_of(k)
        for k in (
            "ensure_ram_init",
            "write_hex2",
            "copy_pkt12",
            "usb_bounce_fix",
            "boot_init_hook",
            "i2c2_rx_hook",
            "lcd_print_hook",
            "lcd_tick_hook",
        )
    }
    if full_profile:
        for k in (
            "rgb_hook",
            "rgb_gamma_lut",
            "set_out_mode_helper",
            "set_gain_helper",
            "set_filter_helper",
            "reboot_dac_helper",
            "raw_lcd_write_hook",
            "render_custom_lcd_helper",
            "backlight_hook",
            "set_hw_backlight_raw",
            "restore_stock_backlight_helper",
        ):
            symbols[k] = a.addr_of(k)
    return blob, symbols


def build_patched_image(
    official_hex: Path,
    profile: str = "stage0-diag",
    transparent_vol: bool = False,
) -> PatchResult:
    if profile not in ("no-lo", "stage0-diag", "full"):
        raise ValueError(f"Unsupported profile: {profile}")

    src_img = load_hex(official_hex, expected_base=m79.BASE_ADDR)
    m79.verify_m79_baseline(src_img.data)
    in_sha = sha256_bytes(src_img.data)

    buf = bytearray(src_img.data)
    patched: list[str] = []

    # 1. Remove LO from double-press and Settings OUT menu (PO <-> PRE only)
    _apply_patch_site(buf, m79.SITE_DOUBLE_PRESS_LO, encode_orr_imm8(R0, R0, 4))
    _apply_patch_site(buf, m79.SITE_MENU_OUT_LO_1, encode_orr_imm8(R0, R0, 4))
    _apply_patch_site(buf, m79.SITE_MENU_OUT_LO_2, encode_orr_imm8(R0, R0, 2))
    patched.extend(["double_press_lo", "menu_out_lo_1", "menu_out_lo_2"])

    if profile == "no-lo":
        out_img = IntelHexImage(
            base_addr=m79.BASE_ADDR,
            data=bytes(buf),
            start_linear_addr=src_img.start_linear_addr,
        )
        return PatchResult(
            profile=profile,
            input_sha256=in_sha,
            output_bin_sha256=sha256_bytes(out_img.data),
            output_size=len(out_img.data),
            blob_start=0,
            blob_end=0,
            patched_sites=tuple(patched),
            symbols={},
            image=out_img,
        )

    # Pad gap from end of M79 binary (0x08037190) up to FREE_FLASH_START (0x08037200) with 0xFF
    gap = m79.FREE_FLASH_START - (m79.BASE_ADDR + len(buf))
    if gap < 0:
        raise ValueError("FREE_FLASH_START overlaps base binary")
    buf.extend(b"\xFF" * gap)

    blob, symbols = _build_free_flash_blob(profile, transparent_vol=transparent_vol)
    blob_start = m79.BASE_ADDR + len(buf)
    buf.extend(blob)
    blob_end = m79.BASE_ADDR + len(buf) - 1

    # 2. Stability fix: decode_usbDetTask PB2 bounce recovery
    _apply_patch_site(
        buf,
        m79.SITE_DECODE_USBDET_BOUNCE,
        encode_b_w(m79.SITE_DECODE_USBDET_BOUNCE.addr, symbols["usb_bounce_fix"])
        + b"\x00\xBF",  # nop
    )
    patched.append("decode_usbDetTask_bounce")

    # 3. Hook lcdTask init, I2C2 RX packet sniffer, lcd_print_str, and lcdTask tick
    _apply_patch_site(
        buf,
        m79.SITE_LCD_TASK_INIT,
        encode_bl(m79.SITE_LCD_TASK_INIT.addr, symbols["boot_init_hook"]) + b"\x00\xBF",
    )
    _apply_patch_site(
        buf,
        m79.SITE_I2C2_RX_PACKET,
        encode_bl(m79.SITE_I2C2_RX_PACKET.addr, symbols["i2c2_rx_hook"]) + b"\x00\xBF",
    )
    _apply_patch_site(
        buf,
        m79.SITE_LCD_PRINT_STR,
        encode_b_w(m79.SITE_LCD_PRINT_STR.addr, symbols["lcd_print_hook"]),
    )
    _apply_patch_site(
        buf,
        m79.SITE_LCD_TASK_TICK,
        encode_bl(m79.SITE_LCD_TASK_TICK.addr, symbols["lcd_tick_hook"]),
    )
    patched.extend(
        ["lcdTask_boot_init", "i2c2_rx_callback_packet", "lcd_print_str_entry", "lcdTask_queue_poll"]
    )

    if profile == "full":
        _apply_patch_site(
            buf,
            m79.SITE_RAW_LCD_DATA_WRITE,
            encode_bl(m79.SITE_RAW_LCD_DATA_WRITE.addr, symbols["raw_lcd_write_hook"]),
        )
        _apply_patch_site(
            buf,
            m79.SITE_RGB_SET_COPY,
            encode_bl(m79.SITE_RGB_SET_COPY.addr, symbols["rgb_hook"]),
        )
        _apply_patch_site(
            buf,
            m79.SITE_SET_LCD_BACKLIGHT,
            encode_b_w(m79.SITE_SET_LCD_BACKLIGHT.addr, symbols["backlight_hook"]),
        )
        patched.extend(["raw_lcd_flush_data_write", "rgbSet_copy_call", "set_lcd_backlight_entry"])

    out_img = IntelHexImage(
        base_addr=m79.BASE_ADDR,
        data=bytes(buf),
        start_linear_addr=src_img.start_linear_addr,
    )
    return PatchResult(
        profile=profile,
        input_sha256=in_sha,
        output_bin_sha256=sha256_bytes(out_img.data),
        output_size=len(out_img.data),
        blob_start=blob_start,
        blob_end=blob_end,
        patched_sites=tuple(patched),
        symbols=symbols,
        image=out_img,
    )


def _default_official_hex() -> Path:
    repo_root = Path(__file__).resolve().parents[2]
    return repo_root / "firmware" / "official" / "K11 R2R M79 Firmware" / "k11R2R_app1_m79.hex"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="FiiO K11 R2R M79 Firmware Patcher")
    parser.add_argument(
        "--input",
        type=Path,
        default=_default_official_hex(),
        help="Path to official k11R2R_app1_m79.hex",
    )
    parser.add_argument(
        "--profile",
        choices=("no-lo", "stage0-diag", "full"),
        default="stage0-diag",
        help="Patch profile to build",
    )
    parser.add_argument(
        "--transparent-vol",
        action="store_true",
        help="In stage0-diag: drop UAC2 volume writes in GD32 to test SA9312L PCM transparency",
    )
    parser.add_argument(
        "--out-dir",
        type=Path,
        default=Path(__file__).resolve().parent / "out",
        help="Directory for output .hex and .bin files",
    )
    args = parser.parse_args(argv)

    res = build_patched_image(
        official_hex=args.input,
        profile=args.profile,
        transparent_vol=args.transparent_vol,
    )
    args.out_dir.mkdir(parents=True, exist_ok=True)
    suffix = f"{args.profile}{'-transparent' if args.transparent_vol else ''}"
    hex_out = args.out_dir / f"k11R2R_m79_{suffix}.hex"
    bin_out = args.out_dir / f"k11R2R_m79_{suffix}.bin"

    write_hex(hex_out, res.image, bytes_per_line=16, line_ending="\r\n", omit_ff_tails=True)
    bin_out.write_bytes(res.image.data)

    pages = (res.output_size + m79.FLASH_PAGE_SIZE - 1) // m79.FLASH_PAGE_SIZE
    print(f"Profile      : {res.profile}")
    print(f"Input BIN    : sha256={res.input_sha256}")
    print(f"Output HEX   : {hex_out}")
    print(f"Output BIN   : {bin_out} ({res.output_size} bytes, {pages} flash pages, sha256={res.output_bin_sha256})")
    if res.blob_start:
        print(f"Injected blob: 0x{res.blob_start:08X}..0x{res.blob_end:08X} ({res.blob_end - res.blob_start + 1} bytes)")
    print(f"Patched sites: {', '.join(res.patched_sites)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
