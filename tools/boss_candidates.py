"""Print metadata-only candidate boss event groups from extracted TH06-style ECL.

The source game files are read only. No ECL instructions or assets are exported.
"""

from __future__ import annotations

import argparse
import struct
from pathlib import Path


def timeline(path: Path) -> list[tuple[int, int, int, int]]:
    data = path.read_bytes()
    start = struct.unpack_from("<I", data, 4)[0]
    result: list[tuple[int, int, int, int]] = []
    cursor = start
    while cursor + 8 <= len(data):
        time, arg, opcode, size, _rank = struct.unpack_from("<hhHBB", data, cursor)
        if size < 8 or cursor + size > len(data):
            break
        result.append((time, arg, opcode, cursor))
        cursor += size
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    for stage in range(1, 8):
        path = args.directory / f"ecldata{stage}.ecl"
        events = timeline(path)
        groups = []
        for index, (time, arg, opcode, offset) in enumerate(events):
            if opcode == 8:
                nearby = events[index:index + 8]
                groups.append((time, [(t, a, op) for t, a, op, _ in nearby]))
        print(f"Stage {stage}: {len(events)} timeline commands")
        for time, group in groups:
            print(f"  t={time}: {group}")


if __name__ == "__main__":
    main()
