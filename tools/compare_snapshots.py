"""Find stable .data changes between two paused game states.

Each state is captured twice. Bytes that change between the two captures of
the same state are excluded. This is an investigation aid, not a patch table.
"""

from __future__ import annotations

import argparse
from pathlib import Path

DATA_RVA = 0x323000
DATA_SIZE = 0x906660


def read(path: Path) -> bytes:
    data = path.read_bytes()
    if len(data) != DATA_SIZE:
        raise ValueError(f"{path}: expected {DATA_SIZE} bytes, found {len(data)}")
    return data


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("before1", "before2", "after1", "after2"):
        parser.add_argument(name, type=Path)
    parser.add_argument("--limit", type=int, default=300)
    args = parser.parse_args()

    before1, before2, after1, after2 = (read(getattr(args, n)) for n in ("before1", "before2", "after1", "after2"))
    stable_changes = [i for i in range(DATA_SIZE) if before1[i] == before2[i] and after1[i] == after2[i] and before1[i] != after1[i]]
    print(f"Stable changed bytes: {len(stable_changes)}")

    runs: list[tuple[int, int]] = []
    for offset in stable_changes:
        if runs and offset == runs[-1][1]:
            runs[-1] = (runs[-1][0], offset + 1)
        else:
            runs.append((offset, offset + 1))
    print(f"Changed runs: {len(runs)}")
    for start, end in runs[: args.limit]:
        before = before1[start:end]
        after = after1[start:end]
        count = end - start
        print(f"RVA 0x{DATA_RVA + start:08X}, {count:3d} bytes: {before[:16].hex(' ')} -> {after[:16].hex(' ')}")
    if len(runs) > args.limit:
        print(f"... {len(runs) - args.limit} further runs omitted")


if __name__ == "__main__":
    main()
