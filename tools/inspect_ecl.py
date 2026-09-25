"""Read-only structural summary of TH06-style ECL files extracted from th06ST.dat.

This intentionally does not export game instructions or repack assets. It only
prints offsets, opcode IDs, and small integer parameters for investigation.
"""

from __future__ import annotations

import argparse
import collections
import struct
from pathlib import Path


def u32(data: bytes, offset: int) -> int:
    return struct.unpack_from("<I", data, offset)[0]


def parse(path: Path, limit: int) -> None:
    data = path.read_bytes()
    if len(data) < 20:
        raise ValueError("ECL too short")
    sub_count, reserved = struct.unpack_from("<HH", data)
    timeline_offsets = [u32(data, 4 + 4 * i) for i in range(3)]
    sub_offsets = [u32(data, 16 + 4 * i) for i in range(sub_count)]
    first_timeline = min((offset for offset in timeline_offsets if offset), default=len(data))
    if reserved != 0 or sub_count > 1000 or sub_offsets != sorted(sub_offsets) or not all(16 + 4 * sub_count <= x < first_timeline for x in sub_offsets):
        raise ValueError("Header does not match expected TH06 ECL structure")
    print(f"{path.name}: {len(data)} bytes, {sub_count} subroutines, timeline offsets {[hex(x) for x in timeline_offsets]}")

    opcode_counts = collections.Counter()
    for index, start in enumerate(sub_offsets):
        end = sub_offsets[index + 1] if index + 1 < sub_count else first_timeline
        cursor = start
        instructions = 0
        last_time = 0
        while cursor + 12 <= end:
            time, opcode, size, rank_mask, param_mask = struct.unpack_from("<IHHHH", data, cursor)
            if size < 12 or cursor + size > end:
                break
            opcode_counts[opcode] += 1
            instructions += 1
            last_time = time
            cursor += size
        print(f"  Sub{index:03d} offset=0x{start:05X} size={end - start:5d} instructions={instructions:3d} last_time={last_time:5d}")
    print("  Frequent sub opcodes:", opcode_counts.most_common(15))

    for number, start in enumerate(timeline_offsets):
        if not start:
            continue
        end = min((x for x in timeline_offsets if x > start), default=len(data))
        print(f"  Timeline{number} offset=0x{start:05X}:")
        cursor = start
        instruction = 0
        while cursor + 8 <= end:
            time, arg0, opcode, size, rank = struct.unpack_from("<hhHBB", data, cursor)
            if size < 8 or cursor + size > end:
                print(f"    stop at 0x{cursor:05X}: invalid size {size}")
                break
            payload = data[cursor + 8 : cursor + size]
            params = [struct.unpack_from("<i", payload, i)[0] for i in range(0, min(len(payload), 16) // 4 * 4, 4)]
            if instruction < limit:
                print(f"    0x{cursor:05X} t={time:6d} arg0={arg0:3d} op={opcode:3d} rank=0x{rank:02X} params={params}")
            cursor += size
            instruction += 1
        print(f"    total timeline instructions: {instruction}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("ecl", type=Path)
    parser.add_argument("--limit", type=int, default=80)
    args = parser.parse_args()
    parse(args.ecl, args.limit)


if __name__ == "__main__":
    main()
