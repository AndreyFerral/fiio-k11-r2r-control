"""Offline verification tests for FiiO K11 R2R M79 patcher."""

from __future__ import annotations

from pathlib import Path
import sys
import tempfile
import unittest

PATCHER_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = PATCHER_DIR.parents[1]
OFFICIAL_HEX = REPO_ROOT / "firmware" / "official" / "K11 R2R M79 Firmware" / "k11R2R_app1_m79.hex"

sys.path.insert(0, str(PATCHER_DIR))

from ihex import dump_hex, load_hex, sha256_bytes, sha256_file  # noqa: E402
import m79_map as m79  # noqa: E402
from patch_m79 import build_patched_image, main as patch_cli_main  # noqa: E402
from thumb import Asm, LR, PC, R0, R1, R4, encode_b_w, encode_bl  # noqa: E402


@unittest.skipUnless(
    OFFICIAL_HEX.exists(),
    "Requires official k11R2R_app1_m79.hex under firmware/official/",
)
class TestIntelHex(unittest.TestCase):
    def test_official_m79_roundtrip_and_sha256(self) -> None:
        self.assertTrue(OFFICIAL_HEX.exists(), f"Missing official HEX: {OFFICIAL_HEX}")
        self.assertEqual(sha256_file(OFFICIAL_HEX), m79.OFFICIAL_HEX_SHA256)
        img = load_hex(OFFICIAL_HEX, expected_base=m79.BASE_ADDR)
        self.assertEqual(img.base_addr, m79.BASE_ADDR)
        self.assertEqual(len(img.data), m79.BINARY_SIZE)
        self.assertEqual(img.start_linear_addr, m79.START_LINEAR_ADDR)
        self.assertEqual(sha256_bytes(img.data), m79.OFFICIAL_BIN_SHA256)

        with tempfile.TemporaryDirectory() as tmp:
            rt_path = Path(tmp) / "roundtrip.hex"
            rt_path.write_bytes(dump_hex(img, bytes_per_line=16, line_ending="\r\n").encode("ascii"))
            img2 = load_hex(rt_path, expected_base=m79.BASE_ADDR)
            self.assertEqual(img2, img)
            # Confirm 100% byte-for-byte identity with official HEX!
            self.assertEqual(rt_path.read_bytes(), OFFICIAL_HEX.read_bytes())


class TestThumbAssembler(unittest.TestCase):
    def test_known_m79_encodings_exact(self) -> None:
        # Matches verified opcodes in k11R2R_app1_m79.hex
        self.assertEqual(encode_bl(0x08017D14, 0x08020470).hex(), "08f0acfb")
        self.assertEqual(encode_b_w(0x08008AC4, 0x08008930).hex(), "fff734bf")

        a = Asm(0x08037200)
        a.push([R4, LR])
        a.mov32(R0, 0x2000C000)
        a.ldr_u32(R1, 0x5231314B)
        a.eors_reg(R0, R1)
        a.muls_reg(R0, R1)
        a.negs(R1, R0)
        a.ldr_imm(R0, R1, 0xA4)
        a.str_imm(R0, R1, 0xA8)
        a.lsrs_reg(R0, R1)
        a.pop([R4, PC])
        code = a.build()
        self.assertEqual(code[:2].hex(), "10b5")  # push {r4, lr}
        self.assertIn(bytes.fromhex("c840"), code)  # lsrs r0, r1

        try:
            from capstone import CS_ARCH_ARM, CS_MODE_THUMB, Cs
        except ImportError:
            return

        md = Cs(CS_ARCH_ARM, CS_MODE_THUMB)
        insns = list(md.disasm(code, 0x08037200))
        mnems = [f"{i.mnemonic} {i.op_str}".strip() for i in insns]
        self.assertGreaterEqual(len(mnems), 10)
        self.assertEqual(mnems[0], "push {r4, lr}")
        self.assertIn("movw r0, #0xc000", mnems[1])
        self.assertIn("movt r0, #0x2000", mnems[2])
        self.assertTrue(any("eors" in m for m in mnems))
        self.assertTrue(any("muls" in m for m in mnems))
        self.assertTrue(any("rsbs" in m or "negs" in m for m in mnems))
        self.assertTrue(any("ldr" in m and "#0xa4" in m for m in mnems))
        self.assertTrue(any("str" in m and "#0xa8" in m for m in mnems))


@unittest.skipUnless(
    OFFICIAL_HEX.exists(),
    "Requires official k11R2R_app1_m79.hex under firmware/official/",
)
class TestM79Patcher(unittest.TestCase):
    def test_profiles_build_and_stay_above_bootloader(self) -> None:
        for profile, transparent in (
            ("no-lo", False),
            ("stage0-diag", False),
            ("stage0-diag", True),
            ("full", False),
        ):
            with self.subTest(profile=profile, transparent=transparent):
                res = build_patched_image(
                    OFFICIAL_HEX,
                    profile=profile,
                    transparent_vol=transparent,
                )
                self.assertEqual(res.image.base_addr, m79.BASE_ADDR)
                self.assertGreaterEqual(res.output_size, m79.BINARY_SIZE)
                # Double-check LO opcodes eliminated
                for addr, expected_hex in (
                    (m79.SITE_DOUBLE_PRESS_LO.addr, "40f00400"),
                    (m79.SITE_MENU_OUT_LO_1.addr, "40f00400"),
                    (m79.SITE_MENU_OUT_LO_2.addr, "40f00200"),
                ):
                    off = addr - m79.BASE_ADDR
                    self.assertEqual(res.image.data[off : off + 4].hex(), expected_hex)

                if res.blob_start:
                    self.assertEqual(res.blob_start, m79.FREE_FLASH_START)
                    blob_off = res.blob_start - m79.BASE_ADDR
                    blob_len = res.blob_end - res.blob_start + 1
                    blob = res.image.data[blob_off : blob_off + blob_len]
                    self.assertGreater(len(blob), 128)
                    try:
                        from capstone import CS_ARCH_ARM, CS_MODE_THUMB, Cs
                    except ImportError:
                        continue
                    md = Cs(CS_ARCH_ARM, CS_MODE_THUMB)
                    ordered = sorted(res.symbols.items(), key=lambda item: item[1])
                    for idx, (name, addr) in enumerate(ordered):
                        if name == "rgb_gamma_lut":
                            continue
                        off = addr - m79.BASE_ADDR
                        next_addr = ordered[idx + 1][1] if idx + 1 < len(ordered) else res.blob_end + 1
                        span = max(16, next_addr - addr)
                        insns = list(md.disasm(res.image.data[off : off + span], addr))
                        self.assertGreater(len(insns), 0, f"Failed to disassemble symbol {name}")
                        if name == "i2c2_rx_hook" and profile == "full":
                            mnems = [f"{i.mnemonic} {i.op_str}".strip() for i in insns]
                            self.assertTrue(
                                any("pop" in m and "pc" in m for m in mnems),
                                "i2c2_rx_hook(full) must cleanly disassemble through pop {..., pc}",
                            )
                        elif name == "rgb_hook":
                            mnems = [f"{i.mnemonic} {i.op_str}".strip() for i in insns]
                            self.assertTrue(
                                any("pop" in m and "pc" in m for m in mnems),
                                "rgb_hook must cleanly disassemble through pop {..., pc}",
                            )

                        elif name == "set_out_mode_helper":
                            mnems = [f"{i.mnemonic} {i.op_str}".strip() for i in insns]
                            self.assertTrue(
                                any("cmp" in m and "#0xff" in m for m in mnems),
                                "set_out_mode_helper must handle 0xFF hardware toggle",
                            )
                            self.assertTrue(
                                any("pop" in m and "pc" in m for m in mnems),
                                "set_out_mode_helper must cleanly disassemble through pop {..., pc}",
                            )
                        elif name == "set_gain_helper":
                            mnems = [f"{i.mnemonic} {i.op_str}".strip() for i in insns]
                            self.assertTrue(any("cmp" in m and "#0xff" in m for m in mnems))
                            self.assertTrue(any("pop" in m and "pc" in m for m in mnems))
                        elif name == "set_filter_helper":
                            mnems = [f"{i.mnemonic} {i.op_str}".strip() for i in insns]
                            self.assertTrue(any("cmp" in m and "#0xff" in m for m in mnems))
                            self.assertTrue(any("bx" in m and "lr" in m for m in mnems))
                        elif name == "raw_lcd_write_hook":
                            mnems = [f"{i.mnemonic} {i.op_str}".strip() for i in insns]
                            self.assertTrue(
                                any("b.w" in m and hex(m79.FN_RAW_LCD_DATA_BYTE) in m for m in mnems),
                                "raw_lcd_write_hook must tail-branch to FN_RAW_LCD_DATA_BYTE",
                            )
                        elif name == "render_custom_lcd_helper":
                            mnems = [f"{i.mnemonic} {i.op_str}".strip() for i in insns]
                            self.assertTrue(any("bl" in m and hex(m79.FN_RAW_LCD_BEGIN) in m for m in mnems))
                            self.assertTrue(any("bl" in m and hex(m79.FN_RAW_LCD_FLUSH) in m for m in mnems))
                            self.assertTrue(any("pop" in m and "pc" in m for m in mnems))
                        elif name == "backlight_hook":
                            mnems = [f"{i.mnemonic} {i.op_str}".strip() for i in insns]
                            self.assertTrue(any("b.w" in m and hex(m79.FN_SET_LCD_BACKLIGHT + 4) in m for m in mnems))

                if profile == "full":
                    for req in (
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
                        self.assertIn(req, res.symbols)
                    self.assertIn("raw_lcd_flush_data_write", res.patched_sites)
                    self.assertIn("rgbSet_copy_call", res.patched_sites)
                    self.assertIn("set_lcd_backlight_entry", res.patched_sites)
                    lut_off = res.symbols["rgb_gamma_lut"] - m79.BASE_ADDR
                    lut_bytes = res.image.data[lut_off : lut_off + 256]
                    self.assertEqual(lut_bytes[0], 0)
                    self.assertEqual(lut_bytes[85], 23)
                    self.assertEqual(lut_bytes[128], 55)
                    self.assertEqual(lut_bytes[255], 255)
                    self.assertLess(res.blob_end, 0x08039000)

    def test_cli_outputs_valid_hex_and_bin(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            out_dir = Path(tmp) / "out"
            for prof in ("stage0-diag", "full"):
                rc = patch_cli_main(
                    ["--input", str(OFFICIAL_HEX), "--profile", prof, "--out-dir", str(out_dir)]
                )
                self.assertEqual(rc, 0)
                hex_path = out_dir / f"k11R2R_m79_{prof}.hex"
                bin_path = out_dir / f"k11R2R_m79_{prof}.bin"
                self.assertTrue(hex_path.exists())
                self.assertTrue(bin_path.exists())
                parsed = load_hex(hex_path, expected_base=m79.BASE_ADDR)
                self.assertEqual(parsed.data, bin_path.read_bytes())


if __name__ == "__main__":
    unittest.main()
