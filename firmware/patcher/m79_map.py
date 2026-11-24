"""Verified addresses, hashes, and pre-patch assertions for FiiO K11 R2R FW M79."""

from __future__ import annotations

from dataclasses import dataclass

OFFICIAL_HEX_SHA256 = "ee72f45a9afa37c126722acbb3248e3be5ca83f2b1e27af29b040db03c973c2e"
OFFICIAL_BIN_SHA256 = "9e57219906df6cc5008d376dfa3c4119322495efbe901e059db711cf5e300e5b"

BASE_ADDR = 0x08008000
BINARY_SIZE = 192912
START_LINEAR_ADDR = 0x08008131
INITIAL_MSP = 0x2000B410
FLASH_PAGE_SIZE = 2048

# Unused flash after k11R2R_app1_m79.hex end (0x0803718F)
FREE_FLASH_START = 0x08037200

# Unused SRAM above Keil scatterload end & initial MSP (0x2000B410 .. 0x2000FFFF)
CUSTOM_RAM_BASE = 0x2000C000
CUSTOM_RAM_MAGIC = 0x5231314B  # "K11R" little-endian

# Existing firmware functions / data
FN_RAW_LCD_BEGIN = 0x0800DA64
FN_RAW_LCD_CHAR = 0x0800DB68
FN_RAW_LCD_FLUSH = 0x0800DB20
FN_RAW_LCD_DATA_BYTE = 0x0801D110
FN_LCD_PRINT_STR = 0x0800F814
FN_LCD_REDRAW_SCREEN = 0x08014974
FN_SET_LCD_BACKLIGHT = 0x0801CC80
FN_OUT_MODE_APPLY = 0x080150B8
FN_RGB_REORDER_COPY = 0x080111F0
FN_WS2812_SEND = 0x08020BEC
FN_FILTER_APPLY = 0x0801CC24
FN_UART_LOG_PRINT = 0x0800FF6C
FN_PB2_RELEASE = 0x0801CBCC
FN_NVIC_SYSTEM_RESET = 0x0800EB1C
FN_QUEUE_RECEIVE = 0x08020518
FN_QUEUE_SEND = 0x08020328
FN_SEMAPHORE_GIVE = 0x0802061C
FN_QUEUE_NOTIFY = 0x0801F8B0
FN_TASK_GET_TICK = 0x0802079C
FN_TASK_GET_TICK_FROM_ISR = 0x080207A8
FN_GET_UI_STATE_PTR = 0x08015D54
FN_GET_STANDBY_STATE = 0x08015DAC

DATA_OUT_STATE_STRUCT = 0x20000398  # +4: Gain(0/1/2), +7: PO=4/PRE=2/LO=1, +15(0xF): SAM(0=NOS,12=OS)
DATA_VOL_QUEUE_PTR = 0x200005E8     # FreeRTOS QueueHandle_t for volume_set task
DATA_VOL_TASK_PTR = 0x200005EC      # FreeRTOS TaskHandle_t for volume_set task
DATA_RGB_DMA_BUF = 0x20000D80       # 3 LEDs x 4 bytes
DATA_LCD_BUF = 0x20000DD8           # 55-byte VA LCD framebuffer (49 char segment bytes + 6 top icon bytes)


@dataclass(frozen=True)
class PatchSite:
    name: str
    addr: int
    expected: bytes


# Verified instruction bytes at hook/patch points in official M79
SITE_DOUBLE_PRESS_LO = PatchSite(
    name="double_press_lo",
    addr=0x0800A050,
    expected=bytes.fromhex("40F00100"),  # orr r0, r0, #1
)
SITE_MENU_OUT_LO_1 = PatchSite(
    name="menu_out_lo_1",
    addr=0x0800AED6,
    expected=bytes.fromhex("40F00100"),  # orr r0, r0, #1
)
SITE_MENU_OUT_LO_2 = PatchSite(
    name="menu_out_lo_2",
    addr=0x0800AF12,
    expected=bytes.fromhex("40F00100"),  # orr r0, r0, #1
)
SITE_DECODE_USBDET_BOUNCE = PatchSite(
    name="decode_usbDetTask_bounce",
    addr=0x0801410A,
    expected=bytes.fromhex("FBF72FFFCFE7"),  # bl 0x0800ff6c; b 0x080140b0
)
SITE_I2C2_RX_PACKET = PatchSite(
    name="i2c2_rx_callback_packet",
    addr=0x08017C94,
    expected=bytes.fromhex("00259DF80900"),  # movs r5, #0; ldrb.w r0, [sp, #9]
)
SITE_LCD_TASK_INIT = PatchSite(
    name="lcdTask_boot_init",
    addr=0x080142DE,
    expected=bytes.fromhex("01F039FD0446"),  # bl 0x08015d54; mov r4, r0
)
SITE_LCD_TASK_TICK = PatchSite(
    name="lcdTask_queue_poll",
    addr=0x08014514,
    expected=bytes.fromhex("0CF000F8"),  # bl 0x08020518 (xQueueReceive)
)
SITE_LCD_PRINT_STR = PatchSite(
    name="lcd_print_str_entry",
    addr=0x0800F814,
    expected=bytes.fromhex("70B50546"),  # push {r4, r5, r6, lr}; mov r5, r0
)
SITE_RAW_LCD_DATA_WRITE = PatchSite(
    name="raw_lcd_flush_data_write",
    addr=0x0800DB4A,
    expected=bytes.fromhex("0FF0E1FA"),  # bl 0x0801d110
)
SITE_RGB_SET_COPY = PatchSite(
    name="rgbSet_copy_call",
    addr=0x08018D2A,
    expected=bytes.fromhex("F8F761FA"),  # bl 0x080111f0
)
SITE_SET_LCD_BACKLIGHT = PatchSite(
    name="set_lcd_backlight_entry",
    addr=0x0801CC80,
    expected=bytes.fromhex("70B50446"),  # push {r4, r5, r6, lr}; mov r4, r0
)

ALL_VERIFIED_SITES = (
    SITE_DOUBLE_PRESS_LO,
    SITE_MENU_OUT_LO_1,
    SITE_MENU_OUT_LO_2,
    SITE_DECODE_USBDET_BOUNCE,
    SITE_I2C2_RX_PACKET,
    SITE_LCD_TASK_INIT,
    SITE_LCD_TASK_TICK,
    SITE_LCD_PRINT_STR,
    SITE_RAW_LCD_DATA_WRITE,
    SITE_RGB_SET_COPY,
    SITE_SET_LCD_BACKLIGHT,
)


def verify_m79_baseline(buf: bytes) -> None:
    """Verify official M79 layout & exact expected bytes before patching."""
    if len(buf) != BINARY_SIZE:
        raise ValueError(f"Expected {BINARY_SIZE} bytes for M79, got {len(buf)}")
    msp = int.from_bytes(buf[0:4], "little")
    if msp != INITIAL_MSP:
        raise ValueError(f"Unexpected initial MSP 0x{msp:08X} (expected 0x{INITIAL_MSP:08X})")
    for site in ALL_VERIFIED_SITES:
        off = site.addr - BASE_ADDR
        actual = buf[off : off + len(site.expected)]
        if actual != site.expected:
            raise ValueError(
                f"Mismatch at {site.name} (0x{site.addr:08X}): expected {site.expected.hex()}, got {actual.hex()}"
            )
