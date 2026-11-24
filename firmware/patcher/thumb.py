"""Pure-Python ARMv7-M Thumb / Thumb-2 micro-assembler for firmware hooks."""

from __future__ import annotations

from dataclasses import dataclass
import struct

COND_CODES: dict[str, int] = {
    "eq": 0x0,
    "ne": 0x1,
    "cs": 0x2,
    "hs": 0x2,
    "cc": 0x3,
    "lo": 0x3,
    "mi": 0x4,
    "pl": 0x5,
    "vs": 0x6,
    "vc": 0x7,
    "hi": 0x8,
    "ls": 0x9,
    "ge": 0xA,
    "lt": 0xB,
    "gt": 0xC,
    "le": 0xD,
}

# Cortex-M3 / Thumb register indices
R0, R1, R2, R3, R4, R5, R6, R7 = range(8)
R8, R9, R10, R11, R12, SP, LR, PC = range(8, 16)


def _u16(val: int) -> bytes:
    return struct.pack("<H", val & 0xFFFF)


def _t2(hw1: int, hw2: int) -> bytes:
    return struct.pack("<HH", hw1 & 0xFFFF, hw2 & 0xFFFF)


def _chk_low(reg: int, name: str = "reg") -> int:
    if not (0 <= reg <= 7):
        raise ValueError(f"{name} must be r0..r7, got r{reg}")
    return reg


def _chk_reg(reg: int, name: str = "reg") -> int:
    if not (0 <= reg <= 15):
        raise ValueError(f"{name} out of range: r{reg}")
    return reg


def _chk_range(val: int, lo: int, hi: int, name: str) -> int:
    if not (lo <= val <= hi):
        raise ValueError(f"{name} out of range [{lo}, {hi}]: {val}")
    return val


def encode_bl(src: int, dst: int) -> bytes:
    """Encode 32-bit Thumb-2 BL (target may have bit 0 clear or set)."""
    target = dst & ~1
    offset = target - (src + 4)
    if (offset & 1) != 0:
        raise ValueError(f"Unaligned BL offset: 0x{offset:x}")
    if not (-(1 << 24) <= offset <= (1 << 24) - 2):
        raise ValueError(f"BL target out of +-16MB range: 0x{target:08X} from 0x{src:08X}")
    s = (offset >> 24) & 1
    i1 = (offset >> 23) & 1
    i2 = (offset >> 22) & 1
    j1 = (~(i1 ^ s)) & 1
    j2 = (~(i2 ^ s)) & 1
    imm10 = (offset >> 12) & 0x3FF
    imm11 = (offset >> 1) & 0x7FF
    hw1 = 0xF000 | (s << 10) | imm10
    hw2 = 0xD000 | (j1 << 13) | (j2 << 11) | imm11
    return _t2(hw1, hw2)


def encode_b_w(src: int, dst: int) -> bytes:
    """Encode 32-bit Thumb-2 unconditional B.W."""
    target = dst & ~1
    offset = target - (src + 4)
    if (offset & 1) != 0:
        raise ValueError(f"Unaligned B.W offset: 0x{offset:x}")
    if not (-(1 << 24) <= offset <= (1 << 24) - 2):
        raise ValueError(f"B.W target out of +-16MB range: 0x{target:08X} from 0x{src:08X}")
    s = (offset >> 24) & 1
    i1 = (offset >> 23) & 1
    i2 = (offset >> 22) & 1
    j1 = (~(i1 ^ s)) & 1
    j2 = (~(i2 ^ s)) & 1
    imm10 = (offset >> 12) & 0x3FF
    imm11 = (offset >> 1) & 0x7FF
    hw1 = 0xF000 | (s << 10) | imm10
    hw2 = 0x9000 | (j1 << 13) | (j2 << 11) | imm11
    return _t2(hw1, hw2)


def encode_b_cond_w(cond: str, src: int, dst: int) -> bytes:
    """Encode 32-bit Thumb-2 conditional branch B<cond>.W (+-1 MB)."""
    c = COND_CODES[cond.lower()]
    target = dst & ~1
    offset = target - (src + 4)
    if (offset & 1) != 0:
        raise ValueError(f"Unaligned B<cond>.W offset: 0x{offset:x}")
    if not (-(1 << 20) <= offset <= (1 << 20) - 2):
        raise ValueError(f"B.{cond}.W target out of +-1MB range: 0x{target:08X} from 0x{src:08X}")
    s = (offset >> 20) & 1
    j2 = (offset >> 19) & 1
    j1 = (offset >> 18) & 1
    imm6 = (offset >> 12) & 0x3F
    imm11 = (offset >> 1) & 0x7FF
    hw1 = 0xF000 | (s << 10) | (c << 6) | imm6
    hw2 = 0x8000 | (j1 << 13) | (j2 << 11) | imm11
    return _t2(hw1, hw2)


def encode_b_narrow(src: int, dst: int) -> bytes:
    offset = (dst & ~1) - (src + 4)
    if (offset & 1) != 0 or not (-2048 <= offset <= 2046):
        raise ValueError(f"Narrow B out of range: {offset}")
    return _u16(0xE000 | ((offset >> 1) & 0x7FF))


def encode_b_cond_narrow(cond: str, src: int, dst: int) -> bytes:
    c = COND_CODES[cond.lower()]
    offset = (dst & ~1) - (src + 4)
    if (offset & 1) != 0 or not (-256 <= offset <= 254):
        raise ValueError(f"Narrow B.{cond} out of range: {offset}")
    return _u16(0xD000 | (c << 8) | ((offset >> 1) & 0xFF))


def encode_orr_imm8(rd: int, rn: int, imm8: int) -> bytes:
    _chk_range(rd, 0, 12, "rd")
    _chk_range(rn, 0, 12, "rn")
    _chk_range(imm8, 0, 255, "imm8")
    return _t2(0xF040 | rn, (rd << 8) | imm8)


def encode_and_imm8(rd: int, rn: int, imm8: int) -> bytes:
    _chk_range(rd, 0, 12, "rd")
    _chk_range(rn, 0, 12, "rn")
    _chk_range(imm8, 0, 255, "imm8")
    return _t2(0xF000 | rn, (rd << 8) | imm8)


@dataclass
class _Fixup:
    offset: int
    size: int
    target: str | int
    emitter: object


class Asm:
    """Two-pass Thumb-2 micro-assembler producing position-pinned machine code."""

    def __init__(self, base_addr: int) -> None:
        if base_addr & 1:
            raise ValueError(f"Thumb code base must be even, got 0x{base_addr:08X}")
        self.base_addr = base_addr
        self._buf = bytearray()
        self._labels: dict[str, int] = {}
        self._fixups: list[_Fixup] = []
        self._words_pool: list[tuple[str, bytes]] = []
        self._pool_counter = 0

    @property
    def pc(self) -> int:
        return self.base_addr + len(self._buf)

    def label(self, name: str) -> str:
        if name in self._labels:
            raise ValueError(f"Duplicate label: {name}")
        self._labels[name] = self.pc
        return name

    def addr_of(self, name: str) -> int:
        return self._labels[name]

    def _emit(self, data: bytes) -> None:
        self._buf.extend(data)

    def raw(self, data: bytes) -> None:
        self._emit(data)

    def nop(self) -> None:
        self._emit(_u16(0xBF00))

    def align4(self, pad: int = 0x00) -> None:
        while self.pc & 3:
            self._buf.append(pad & 0xFF)

    def word(self, val: int) -> None:
        self.align4()
        self._emit(struct.pack("<I", val & 0xFFFFFFFF))

    def ascii(self, text: str, nul: bool = True) -> None:
        b = text.encode("ascii") + (b"\x00" if nul else b"")
        self._emit(b)

    def push(self, regs: list[int]) -> None:
        mask = 0
        for r in regs:
            if r == LR:
                mask |= 1 << 8
            else:
                mask |= 1 << _chk_low(r, "push reg")
        self._emit(_u16(0xB400 | mask))

    def pop(self, regs: list[int]) -> None:
        mask = 0
        for r in regs:
            if r == PC:
                mask |= 1 << 8
            else:
                mask |= 1 << _chk_low(r, "pop reg")
        self._emit(_u16(0xBC00 | mask))

    def movs(self, rd: int, imm8: int) -> None:
        self._emit(_u16(0x2000 | (_chk_low(rd) << 8) | _chk_range(imm8, 0, 255, "imm8")))

    def mov_reg(self, rd: int, rm: int) -> None:
        rd = _chk_reg(rd, "rd")
        rm = _chk_reg(rm, "rm")
        self._emit(_u16(0x4600 | ((rd >> 3) << 7) | (rm << 3) | (rd & 7)))

    def movw(self, rd: int, imm16: int) -> None:
        _chk_range(rd, 0, 12, "rd")
        _chk_range(imm16, 0, 0xFFFF, "imm16")
        imm4 = (imm16 >> 12) & 0xF
        i = (imm16 >> 11) & 1
        imm3 = (imm16 >> 8) & 0x7
        imm8 = imm16 & 0xFF
        self._emit(_t2(0xF240 | (i << 10) | imm4, (imm3 << 12) | (rd << 8) | imm8))

    def movt(self, rd: int, imm16: int) -> None:
        _chk_range(rd, 0, 12, "rd")
        _chk_range(imm16, 0, 0xFFFF, "imm16")
        imm4 = (imm16 >> 12) & 0xF
        i = (imm16 >> 11) & 1
        imm3 = (imm16 >> 8) & 0x7
        imm8 = imm16 & 0xFF
        self._emit(_t2(0xF2C0 | (i << 10) | imm4, (imm3 << 12) | (rd << 8) | imm8))

    def mov32(self, rd: int, imm32: int) -> None:
        val = imm32 & 0xFFFFFFFF
        self.movw(rd, val & 0xFFFF)
        self.movt(rd, (val >> 16) & 0xFFFF)

    def adds_imm3(self, rd: int, rn: int, imm3: int) -> None:
        self._emit(_u16(0x1C00 | (_chk_range(imm3, 0, 7, "imm3") << 6) | (_chk_low(rn) << 3) | _chk_low(rd)))

    def subs_imm3(self, rd: int, rn: int, imm3: int) -> None:
        self._emit(_u16(0x1E00 | (_chk_range(imm3, 0, 7, "imm3") << 6) | (_chk_low(rn) << 3) | _chk_low(rd)))

    def adds_imm8(self, rdn: int, imm8: int) -> None:
        self._emit(_u16(0x3000 | (_chk_low(rdn) << 8) | _chk_range(imm8, 0, 255, "imm8")))

    def subs_imm8(self, rdn: int, imm8: int) -> None:
        self._emit(_u16(0x3800 | (_chk_low(rdn) << 8) | _chk_range(imm8, 0, 255, "imm8")))

    def adds_reg(self, rd: int, rn: int, rm: int) -> None:
        self._emit(_u16(0x1800 | (_chk_low(rm) << 6) | (_chk_low(rn) << 3) | _chk_low(rd)))

    def subs_reg(self, rd: int, rn: int, rm: int) -> None:
        self._emit(_u16(0x1A00 | (_chk_low(rm) << 6) | (_chk_low(rn) << 3) | _chk_low(rd)))

    def add_reg(self, rdn: int, rm: int) -> None:
        rdn = _chk_reg(rdn, "rdn")
        rm = _chk_reg(rm, "rm")
        self._emit(_u16(0x4400 | ((rdn >> 3) << 7) | (rm << 3) | (rdn & 7)))

    def add_sp_imm(self, rd: int, imm: int) -> None:
        _chk_range(imm, 0, 1020, "imm")
        if imm & 3:
            raise ValueError(f"imm must be word-aligned: {imm}")
        self._emit(_u16(0xA800 | (_chk_low(rd) << 8) | (imm >> 2)))

    def sp_sub(self, imm: int) -> None:
        _chk_range(imm, 0, 508, "imm")
        if imm & 3:
            raise ValueError(f"imm must be word-aligned: {imm}")
        self._emit(_u16(0xB080 | (imm >> 2)))

    def sp_add(self, imm: int) -> None:
        _chk_range(imm, 0, 508, "imm")
        if imm & 3:
            raise ValueError(f"imm must be word-aligned: {imm}")
        self._emit(_u16(0xB000 | (imm >> 2)))

    def cmp_imm(self, rn: int, imm8: int) -> None:
        self._emit(_u16(0x2800 | (_chk_low(rn) << 8) | _chk_range(imm8, 0, 255, "imm8")))

    def cmp_reg(self, rn: int, rm: int) -> None:
        if 0 <= rn <= 7 and 0 <= rm <= 7:
            self._emit(_u16(0x4280 | (rm << 3) | rn))
        else:
            self._emit(_u16(0x4500 | ((_chk_reg(rn) >> 3) << 7) | (_chk_reg(rm) << 3) | (rn & 7)))

    def and_imm8(self, rd: int, rn: int, imm8: int) -> None:
        self._emit(encode_and_imm8(rd, rn, imm8))

    def orr_imm8(self, rd: int, rn: int, imm8: int) -> None:
        self._emit(encode_orr_imm8(rd, rn, imm8))

    def ands_reg(self, rdn: int, rm: int) -> None:
        self._emit(_u16(0x4000 | (_chk_low(rm) << 3) | _chk_low(rdn)))

    def eors_reg(self, rdn: int, rm: int) -> None:
        self._emit(_u16(0x4040 | (_chk_low(rm) << 3) | _chk_low(rdn)))

    def orrs_reg(self, rdn: int, rm: int) -> None:
        self._emit(_u16(0x4300 | (_chk_low(rm) << 3) | _chk_low(rdn)))

    def muls_reg(self, rdm: int, rn: int) -> None:
        self._emit(_u16(0x4340 | (_chk_low(rn) << 3) | _chk_low(rdm)))

    def negs(self, rd: int, rm: int) -> None:
        self._emit(_u16(0x4240 | (_chk_low(rm) << 3) | _chk_low(rd)))

    def lsls_imm(self, rd: int, rm: int, shift: int) -> None:
        self._emit(_u16(0x0000 | (_chk_range(shift, 0, 31, "shift") << 6) | (_chk_low(rm) << 3) | _chk_low(rd)))

    def lsrs_imm(self, rd: int, rm: int, shift: int) -> None:
        _chk_range(shift, 1, 31, "shift")
        self._emit(_u16(0x0800 | (shift << 6) | (_chk_low(rm) << 3) | _chk_low(rd)))

    def lsrs_reg(self, rdn: int, rm: int) -> None:
        self._emit(_u16(0x40C0 | (_chk_low(rm) << 3) | _chk_low(rdn)))

    def uxtb(self, rd: int, rm: int) -> None:
        self._emit(_u16(0xB2C0 | (_chk_low(rm) << 3) | _chk_low(rd)))

    def ldr_imm(self, rt: int, rn: int, imm: int = 0) -> None:
        _chk_range(imm, 0, 4095, "imm")
        if 0 <= rt <= 7 and 0 <= rn <= 7 and imm <= 124 and (imm & 3) == 0:
            self._emit(_u16(0x6800 | ((imm >> 2) << 6) | (rn << 3) | rt))
        else:
            _chk_range(rt, 0, 12, "rt")
            _chk_range(rn, 0, 13, "rn")
            self._emit(_t2(0xF8D0 | rn, (rt << 12) | imm))

    def str_imm(self, rt: int, rn: int, imm: int = 0) -> None:
        _chk_range(imm, 0, 4095, "imm")
        if 0 <= rt <= 7 and 0 <= rn <= 7 and imm <= 124 and (imm & 3) == 0:
            self._emit(_u16(0x6000 | ((imm >> 2) << 6) | (rn << 3) | rt))
        else:
            _chk_range(rt, 0, 12, "rt")
            _chk_range(rn, 0, 13, "rn")
            self._emit(_t2(0xF8C0 | rn, (rt << 12) | imm))

    def ldrb_imm(self, rt: int, rn: int, imm: int = 0) -> None:
        _chk_range(imm, 0, 4095, "imm")
        if 0 <= rt <= 7 and 0 <= rn <= 7 and imm <= 31:
            self._emit(_u16(0x7800 | (imm << 6) | (rn << 3) | rt))
        else:
            _chk_range(rt, 0, 12, "rt")
            _chk_range(rn, 0, 13, "rn")
            self._emit(_t2(0xF890 | rn, (rt << 12) | imm))

    def strb_imm(self, rt: int, rn: int, imm: int = 0) -> None:
        _chk_range(imm, 0, 4095, "imm")
        if 0 <= rt <= 7 and 0 <= rn <= 7 and imm <= 31:
            self._emit(_u16(0x7000 | (imm << 6) | (rn << 3) | rt))
        else:
            _chk_range(rt, 0, 12, "rt")
            _chk_range(rn, 0, 13, "rn")
            self._emit(_t2(0xF880 | rn, (rt << 12) | imm))

    def ldrh_imm(self, rt: int, rn: int, imm: int = 0) -> None:
        _chk_range(imm, 0, 62, "imm")
        if imm & 1:
            raise ValueError(f"LDRH offset must be even: {imm}")
        self._emit(_u16(0x8800 | ((imm >> 1) << 6) | (_chk_low(rn) << 3) | _chk_low(rt)))

    def strh_imm(self, rt: int, rn: int, imm: int = 0) -> None:
        _chk_range(imm, 0, 62, "imm")
        if imm & 1:
            raise ValueError(f"STRH offset must be even: {imm}")
        self._emit(_u16(0x8000 | ((imm >> 1) << 6) | (_chk_low(rn) << 3) | _chk_low(rt)))

    def ldr_sp(self, rt: int, imm: int = 0) -> None:
        _chk_range(imm, 0, 1020, "imm")
        if imm & 3:
            raise ValueError(f"SP offset must be multiple of 4: {imm}")
        self._emit(_u16(0x9800 | (_chk_low(rt) << 8) | (imm >> 2)))

    def str_sp(self, rt: int, imm: int = 0) -> None:
        _chk_range(imm, 0, 1020, "imm")
        if imm & 3:
            raise ValueError(f"SP offset must be multiple of 4: {imm}")
        self._emit(_u16(0x9000 | (_chk_low(rt) << 8) | (imm >> 2)))

    def ldrb_reg(self, rt: int, rn: int, rm: int) -> None:
        self._emit(_u16(0x5C00 | (_chk_low(rm) << 6) | (_chk_low(rn) << 3) | _chk_low(rt)))

    def strb_reg(self, rt: int, rn: int, rm: int) -> None:
        self._emit(_u16(0x5400 | (_chk_low(rm) << 6) | (_chk_low(rn) << 3) | _chk_low(rt)))

    def bx(self, rm: int) -> None:
        self._emit(_u16(0x4700 | (_chk_reg(rm) << 3)))

    def blx(self, rm: int) -> None:
        self._emit(_u16(0x4780 | (_chk_reg(rm) << 3)))

    def _add_fixup(self, target: str | int, size: int, emitter: object) -> None:
        off = len(self._buf)
        self._fixups.append(_Fixup(offset=off, size=size, target=target, emitter=emitter))
        self._emit(b"\x00" * size)

    def bl(self, target: str | int) -> None:
        self._add_fixup(target, 4, encode_bl)

    def b_w(self, target: str | int) -> None:
        self._add_fixup(target, 4, encode_b_w)

    def b_cond_w(self, cond: str, target: str | int) -> None:
        c = cond
        self._add_fixup(target, 4, lambda s, d: encode_b_cond_w(c, s, d))

    def b(self, target: str | int) -> None:
        self._add_fixup(target, 2, encode_b_narrow)

    def b_cond(self, cond: str, target: str | int) -> None:
        c = cond
        self._add_fixup(target, 2, lambda s, d: encode_b_cond_narrow(c, s, d))

    def cbz(self, rn: int, target: str | int) -> None:
        _chk_low(rn, "cbz reg")
        self._add_fixup(target, 2, lambda s, d: _encode_cbnz(0, rn, s, d))

    def cbnz(self, rn: int, target: str | int) -> None:
        _chk_low(rn, "cbnz reg")
        self._add_fixup(target, 2, lambda s, d: _encode_cbnz(1, rn, s, d))

    def adr_label(self, rd: int, target: str) -> None:
        _chk_low(rd, "adr reg")
        self._add_fixup(target, 2, lambda s, d: _encode_adr(rd, s, d))

    def ldr_pool(self, rt: int, target: str) -> None:
        _chk_low(rt, "ldr pool reg")
        self._add_fixup(target, 2, lambda s, d: _encode_ldr_lit(rt, s, d))

    def pool_u32(self, value: int) -> str:
        name = f"__pool_{self._pool_counter}"
        self._pool_counter += 1
        self._words_pool.append((name, struct.pack("<I", value & 0xFFFFFFFF)))
        return name

    def ldr_u32(self, rt: int, value: int) -> None:
        name = self.pool_u32(value)
        self.ldr_pool(rt, name)

    def flush_pool(self) -> None:
        if not self._words_pool:
            return
        self.align4()
        for name, raw in self._words_pool:
            self.label(name)
            self._emit(raw)
        self._words_pool.clear()

    def build(self) -> bytes:
        self.flush_pool()
        out = bytearray(self._buf)
        for fx in self._fixups:
            src = self.base_addr + fx.offset
            if isinstance(fx.target, str):
                if fx.target not in self._labels:
                    raise KeyError(f"Unresolved label: {fx.target}")
                dst = self._labels[fx.target]
            else:
                dst = fx.target
            encoded = fx.emitter(src, dst)
            if len(encoded) != fx.size:
                raise AssertionError(f"Fixup length mismatch at 0x{src:08X}")
            out[fx.offset : fx.offset + fx.size] = encoded
        return bytes(out)


def _encode_cbnz(op: int, rn: int, src: int, dst: int) -> bytes:
    offset = (dst & ~1) - (src + 4)
    if (offset & 1) != 0 or not (0 <= offset <= 126):
        raise ValueError(f"CBZ/CBNZ target out of [0, 126] range: {offset} (0x{src:08X} -> 0x{dst:08X})")
    i = (offset >> 6) & 1
    imm5 = (offset >> 1) & 0x1F
    return _u16(0xB100 | (op << 11) | (i << 9) | (imm5 << 3) | rn)


def _encode_adr(rd: int, src: int, dst: int) -> bytes:
    pc_al = (src + 4) & ~3
    delta = dst - pc_al
    if (delta & 3) != 0 or not (0 <= delta <= 1020):
        raise ValueError(f"ADR out of aligned [0, 1020] range: {delta} (0x{src:08X} -> 0x{dst:08X})")
    return _u16(0xA000 | (rd << 8) | (delta >> 2))


def _encode_ldr_lit(rt: int, src: int, dst: int) -> bytes:
    pc_al = (src + 4) & ~3
    delta = dst - pc_al
    if (delta & 3) != 0 or not (0 <= delta <= 1020):
        raise ValueError(f"LDR literal out of aligned [0, 1020] range: {delta} (0x{src:08X} -> 0x{dst:08X})")
    return _u16(0x4800 | (rt << 8) | (delta >> 2))
