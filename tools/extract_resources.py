"""
Extract resources from a Sierra SCI1.1 game volume.

Verified against Jones in the Fast Lane (CD DOS 1.0). Formats, confirmed empirically
against this game's files:

  resource.map : repeating 6-byte entries
                   u16 id
                   u32 packed  -> volume = packed >> 28, offset = packed & 0x0FFFFFFF
                 id packs the resource: type = id >> 11, number = id & 0x7FF
                 (the first entry decodes to type 2 number 999 = script 999 = System)

  resource.00N : at each offset
                   u16 id            (repeats the map's id)
                   u16 compSize      (counts the four header bytes that follow it)
                   u16 decompSize
                   u16 method        (0 = stored)
                   bytes             (compSize - 4)

Usage:
    python extract_resources.py <game_dir> <out_dir> [--survey]
"""

import os
import struct
import sys
from collections import Counter

# SCI resource types by index.
TYPES = {
    0: "view", 1: "pic", 2: "script", 3: "text", 4: "sound",
    5: "memory", 6: "vocab", 7: "font", 8: "cursor", 9: "patch",
    10: "bitmap", 11: "palette", 12: "cdaudio", 13: "audio",
    14: "sync", 15: "message", 16: "map", 17: "heap",
}

COMPRESSION = {
    0: "none",
    18: "dcl", 19: "dcl", 20: "dcl",
    32: "stacpack",
}


def read_map(path):
    """Yields (restype, number, volume, offset) for every entry."""
    data = open(path, "rb").read()
    for i in range(0, len(data) - 5, 6):
        rid, packed = struct.unpack_from("<HI", data, i)
        if rid == 0xFFFF:
            break
        yield (rid >> 11), (rid & 0x7FF), (packed >> 28), (packed & 0x0FFFFFFF)


def read_entry(vol_bytes, offset):
    """Returns (number, decomp_size, method, payload) at an offset in a volume."""
    rid, comp_size, decomp_size, method = struct.unpack_from("<HHHH", vol_bytes, offset)
    payload = vol_bytes[offset + 8 : offset + 4 + comp_size]
    return (rid & 0x7FF), decomp_size, method, payload


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1

    game_dir, out_dir = sys.argv[1], sys.argv[2]
    survey_only = "--survey" in sys.argv

    entries = list(read_map(os.path.join(game_dir, "resource.map")))

    # Load each volume once.
    volumes = {}
    for _, _, vol, _ in entries:
        if vol not in volumes:
            p = os.path.join(game_dir, "resource.%03d" % vol)
            volumes[vol] = open(p, "rb").read() if os.path.exists(p) else None

    by_type = Counter()
    methods = Counter()
    written = 0

    for restype, number, vol, offset in entries:
        data = volumes.get(vol)
        if data is None or offset >= len(data):
            continue

        name = TYPES.get(restype, "type%d" % restype)
        by_type[name] += 1

        _, decomp_size, method, payload = read_entry(data, offset)
        methods[COMPRESSION.get(method, "method%d" % method)] += 1

        if survey_only:
            continue

        # Only stored resources can be written directly; compressed ones need the
        # DCL decompressor, which is a separate step.
        if method != 0:
            continue

        d = os.path.join(out_dir, name)
        os.makedirs(d, exist_ok=True)
        with open(os.path.join(d, "%d.%s" % (number, name)), "wb") as f:
            f.write(payload[:decomp_size] if decomp_size else payload)
        written += 1

    print("resources in map: %d" % len(entries))
    print("\nby type:")
    for k, v in sorted(by_type.items(), key=lambda kv: -kv[1]):
        print("  %-10s %4d" % (k, v))
    print("\ncompression:")
    for k, v in sorted(methods.items(), key=lambda kv: -kv[1]):
        print("  %-10s %4d" % (k, v))
    if not survey_only:
        print("\nwrote %d uncompressed resources to %s" % (written, out_dir))

    return 0


if __name__ == "__main__":
    sys.exit(main())
