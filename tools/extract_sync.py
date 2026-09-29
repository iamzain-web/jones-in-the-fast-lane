"""
Build assets/audio/sync.json: CD speech audio id -> [[timeMs, celIndex], ...]

Format of a .sync resource (verified against the bytes and against the game's
own code in the CD build's Sync.sc, script 976):

    repeat:
        u16  time   little-endian, in 1/60 s ticks from the start of the WAV
        u16  cue
    until time == 0xFFFF
    [optional trailer: the sync compiler's source note, ASCII, e.g.
     "audio.010" / "view.359" - not part of the cue table]

MouthSync::doit does

    (client cel: (& $000f (global558 prevCue:)))

so only the low nibble of the cue selects the cel; every talker view in this
game has exactly 11 cels (0..10) in loop 0 and every low nibble observed is
0..10.  Sync::syncCheck compares the time against (DoAudio audPOSITION), which
SCI reports in ticks, confirming the 1/60 s unit.  MouthSync::cue resets the
talker to cel 0 when the line ends, so cel 0 is the closed/rest mouth.

Standard library only.  Usage:  python extract_sync.py
"""

import json
import os
import re
import struct
import sys
import wave

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SYNC = os.path.join(ROOT, "assets", "raw", "sync")
SPEECH = os.path.join(ROOT, "assets", "audio", "speech")
OUT = os.path.join(ROOT, "assets", "audio", "sync.json")

TICK_MS = 1000.0 / 60.0
CEL_MASK = 0x000F


def parse(data):
    """-> (pairs, trailer, terminated).  pairs is [(tick, cue), ...]."""
    pairs = []
    i = 0
    terminated = False
    while i + 1 < len(data):
        (tick,) = struct.unpack_from("<H", data, i)
        if tick == 0xFFFF:
            i += 2
            terminated = True
            break
        if i + 3 >= len(data):
            break
        (cue,) = struct.unpack_from("<H", data, i + 2)
        pairs.append((tick, cue))
        i += 4
    return pairs, data[i:], terminated


def main():
    files = sorted(os.listdir(SYNC), key=lambda f: int(f.split(".")[0]))
    have_wav = set(int(f[:-4]) for f in os.listdir(SPEECH) if f.endswith(".wav"))

    out = {}
    stats = {"files": 0, "unterminated": 0, "non_monotonic": 0, "cels": set(),
             "high_bits": {}, "empty": [], "no_wav": [], "overruns": []}
    views = {}
    by_table = {}

    for name in files:
        aid = int(name.split(".")[0])
        data = open(os.path.join(SYNC, name), "rb").read()
        pairs, trailer, terminated = parse(data)
        by_table.setdefault(tuple(pairs), []).append(aid)
        stats["files"] += 1
        if not terminated:
            stats["unterminated"] += 1
        ticks = [t for t, _ in pairs]
        if ticks != sorted(ticks):
            stats["non_monotonic"] += 1
        for _, cue in pairs:
            stats["cels"].add(cue & CEL_MASK)
            hb = cue & ~CEL_MASK
            stats["high_bits"][hb] = stats["high_bits"].get(hb, 0) + 1
        if not pairs:
            stats["empty"].append(aid)
        m = re.search(rb"view\.(\d+)", trailer)
        if m:
            views[aid] = int(m.group(1))

        if aid not in have_wav:
            stats["no_wav"].append(aid)
            continue

        out[str(aid)] = [[int(round(t * TICK_MS)), cue & CEL_MASK] for t, cue in pairs]

        with wave.open(os.path.join(SPEECH, "%d.wav" % aid), "rb") as w:
            dur_ms = w.getnframes() * 1000.0 / w.getframerate()
        if pairs and pairs[-1][0] * TICK_MS > dur_ms + 50:
            stats["overruns"].append((aid, round(pairs[-1][0] * TICK_MS), round(dur_ms)))

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(out, f, separators=(",", ":"))

    print("sync resources read : %d" % stats["files"])
    print("entries written     : %d  -> %s" % (len(out), OUT))
    print("sync with no wav    : %d %s" % (len(stats["no_wav"]), sorted(stats["no_wav"])))
    print("wav with no sync    : %d %s" % (len(have_wav - set(int(k) for k in out)),
                                           sorted(have_wav - set(int(k) for k in out))))
    print("cue tables with no FFFF terminator : %d" % stats["unterminated"])
    print("cue tables out of time order       : %d" % stats["non_monotonic"])
    print("empty cue tables                   : %d %s" % (len(stats["empty"]), stats["empty"]))
    print("distinct cel indices (cue & 0x0F)  : %s" % sorted(stats["cels"]))
    print("cue high bits (cue & ~0x0F)        : %s"
          % {hex(k): v for k, v in sorted(stats["high_bits"].items())})
    print("last cue past end of wav (>50ms)   : %d %s" % (len(stats["overruns"]), stats["overruns"][:8]))
    total = sum(len(v) for v in out.values())
    print("total mouth cues                   : %d (avg %.1f per line)" % (total, total / max(len(out), 1)))
    vc = {}
    for v in views.values():
        vc[v] = vc.get(v, 0) + 1
    print("talker view named in sync trailer  : %d of %d lines; views %s"
          % (len(views), stats["files"], sorted(vc.items())))

    # A large group of resources share one identical 13-cue table.  Those lines
    # are narration shown as on-screen text with no talker on screen (weekend
    # summaries, newspaper headlines, the goal screens), so nobody ever authored
    # real lip sync for them - the cues are a canned placeholder.
    shared = max(by_table.values(), key=len)
    if len(shared) > 1:
        print("PLACEHOLDER cue table shared by    : %d resources (%d of them have a wav)"
              % (len(shared), len([a for a in shared if a in have_wav])))
        print("   ids: %s" % sorted(shared))


if __name__ == "__main__":
    sys.exit(main())
