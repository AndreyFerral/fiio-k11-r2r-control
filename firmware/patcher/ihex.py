"""Pure-Python Intel HEX reader/writer for STM32/GD32 firmware images."""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
from pathlib import Path


@dataclass(frozen=True)
class IntelHexImage:
    """Contiguous byte buffer parsed from an Intel HEX file."""

    base_addr: int
    data: bytes
    start_linear_addr: int | None = None

    @property
    def end_addr(self) -> int:
        return self.base_addr + len(self.data)


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def _format_record(rec_type: int, addr16: int, payload: bytes) -> str:
    length = len(payload)
    if length > 255:
        raise ValueError(f"Record payload too long: {length}")
    header = bytes([length, (addr16 >> 8) & 0xFF, addr16 & 0xFF, rec_type]) + payload
    csum = (-sum(header)) & 0xFF
    return ":" + header.hex().upper() + f"{csum:02X}"


def load_hex(path: Path, expected_base: int | None = None) -> IntelHexImage:
    """Parse an Intel HEX file into a contiguous byte image (0xFF-padded gaps)."""
    ext_high = 0
    mem: dict[int, int] = {}
    start_linear: int | None = None
    saw_eof = False

    for line_no, raw_line in enumerate(path.read_text(encoding="ascii").splitlines(), start=1):
        line = raw_line.strip()
        if not line:
            continue
        if not line.startswith(":"):
            raise ValueError(f"{path}:{line_no}: missing ':' prefix")
        raw = bytes.fromhex(line[1:])
        if len(raw) < 5:
            raise ValueError(f"{path}:{line_no}: record too short")
        length = raw[0]
        if len(raw) != 5 + length:
            raise ValueError(f"{path}:{line_no}: length mismatch")
        if (sum(raw) & 0xFF) != 0:
            raise ValueError(f"{path}:{line_no}: checksum error")

        addr16 = (raw[1] << 8) | raw[2]
        rec_type = raw[3]
        payload = raw[4 : 4 + length]

        if rec_type == 0x00:
            full = (ext_high << 16) | addr16
            for i, b in enumerate(payload):
                mem[full + i] = b
        elif rec_type == 0x01:
            saw_eof = True
            break
        elif rec_type == 0x04:
            if length != 2:
                raise ValueError(f"{path}:{line_no}: invalid record 04 length {length}")
            ext_high = (payload[0] << 8) | payload[1]
        elif rec_type == 0x05:
            if length != 4:
                raise ValueError(f"{path}:{line_no}: invalid record 05 length {length}")
            start_linear = int.from_bytes(payload, "big")
        else:
            raise ValueError(f"{path}:{line_no}: unsupported Intel HEX record type 0x{rec_type:02X}")

    if not saw_eof:
        raise ValueError(f"{path}: missing EOF (:01) record")
    if not mem:
        raise ValueError(f"{path}: no data records found")

    min_addr = min(mem) if expected_base is None else expected_base
    if expected_base is not None and min(mem) < expected_base:
        raise ValueError(
            f"{path}: contains address 0x{min(mem):08X} below expected base 0x{expected_base:08X}"
        )
    max_addr = max(mem)
    buf = bytearray([0xFF] * (max_addr - min_addr + 1))
    for addr, val in mem.items():
        buf[addr - min_addr] = val
    return IntelHexImage(base_addr=min_addr, data=bytes(buf), start_linear_addr=start_linear)


def dump_hex(
    image: IntelHexImage,
    bytes_per_line: int = 16,
    line_ending: str = "\r\n",
    omit_ff_tails: bool = False,
) -> str:
    """Serialize an IntelHexImage back to Intel HEX text."""
    if not (1 <= bytes_per_line <= 32):
        raise ValueError(f"Unsupported bytes_per_line: {bytes_per_line}")

    lines: list[str] = []
    cur_ext: int | None = None
    data = image.data
    total = len(data)
    off = 0

    while off < total:
        chunk_len = min(bytes_per_line, total - off)
        chunk = data[off : off + chunk_len]
        if omit_ff_tails and all(b == 0xFF for b in chunk):
            off += chunk_len
            continue
        addr = image.base_addr + off
        ext = (addr >> 16) & 0xFFFF
        low = addr & 0xFFFF
        # Split if a record crosses a 64 KB segment boundary.
        max_in_seg = 0x10000 - low
        if chunk_len > max_in_seg:
            chunk_len = max_in_seg
            chunk = data[off : off + chunk_len]
        if ext != cur_ext:
            cur_ext = ext
            lines.append(_format_record(0x04, 0, ext.to_bytes(2, "big")))
        lines.append(_format_record(0x00, low, chunk))
        off += chunk_len

    if image.start_linear_addr is not None:
        lines.append(_format_record(0x05, 0, image.start_linear_addr.to_bytes(4, "big")))
    lines.append(_format_record(0x01, 0, b""))
    return line_ending.join(lines) + line_ending


def write_hex(path: Path, image: IntelHexImage, **kwargs) -> None:
    text = dump_hex(image, **kwargs)
    path.write_bytes(text.encode("ascii"))
