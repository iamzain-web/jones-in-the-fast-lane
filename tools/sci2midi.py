#!/usr/bin/env python3
"""
sci2midi.py -- convert the SCI1 sound resources of "Jones in the Fast Lane"
into Standard MIDI Files (SMF Type 1).

Source format (verified against the real bytes, see assets/audio/README.md):

  <n>.sound
      track list:
          repeat {
              u8  trackType                      # device this track is for
              repeat {                           # 6 bytes per channel entry
                  u8  unused_a                   # always 0 in this game
                  u8  unused_b                   # always 0 in this game
                  u16 dataOffset                 # LE, from start of resource
                  u16 dataSize                   # LE
              } until next byte == 0xFF          # 0xFF ends the channel list
          } until next byte == 0xFF              # 0xFF ends the track list
      then ONE pad byte (0x00) -- absent in 100.sound
      then the channel data blobs.

  Each channel blob:
      u8  channelNumber      # MIDI channel in the low nibble; high nibble is a flag
      u8  polyphony/priority # low nibble = polyphony, high nibble = priority
      then SCI-MIDI events:
          delta: while byte == 0xF8: delta += 240, read another byte
                 then delta += byte
          event: MIDI status byte, or a data byte meaning "running status"
                 0xFC in the status position = end of track
                 0xF0..0xF7 SysEx runs to a terminating 0xF7

  Device track types observed in this game:
      0x00 AdLib (all 34)      0x06 alias stub (8)    0x09 CMS/Game Blaster (all 34)
      0x0C MT-32 / MIDI (34)   0x12 PC speaker (34)   0x13 Tandy/PCjr (34)
      0x16 alias stub (8)
  0x06 and 0x16 are not real alternate arrangements: they point at the very same
  byte ranges as other tracks (a control-channel-only stub), so they are ignored.

Output timing: SMF division 60 PPQN with a tempo of 1000000 us/quarter, so one
MIDI tick == 1/60 s, which is SCI's native tick rate. No delta rescaling is done.

Standard library only. Python 3.12.
"""

from __future__ import annotations

import argparse
import glob
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

DEFAULT_IN = os.path.join(ROOT, "assets", "raw", "sound")
DEFAULT_OUT = os.path.join(ROOT, "assets", "audio", "midi")

DIVISION = 60           # ticks per quarter note
TEMPO_US = 1000000      # microseconds per quarter note -> 1 tick = 1/60 s

TRACK_MT32 = 0x0C
TRACK_ADLIB = 0x00
PREFERENCE = (TRACK_MT32, TRACK_ADLIB)

# Stub track types that alias another track's byte range rather than holding
# their own arrangement.
ALIAS_TRACK_TYPES = (0x06, 0x16)

DEVICE_NAMES = {
    0x00: "AdLib", 0x06: "alias-stub", 0x09: "CMS", 0x0C: "MT-32/MIDI",
    0x12: "PC speaker", 0x13: "Tandy/PCjr", 0x16: "alias-stub",
}

END_OF_TRACK = 0xFC
DELTA_EXTEND = 0xF8
DELTA_EXTEND_TICKS = 240


class SoundParseError(Exception):
    pass


# --------------------------------------------------------------------------- #
# resource parsing
# --------------------------------------------------------------------------- #

def parse_tracks(data: bytes):
    """Parse the track/channel table. Returns (tracks, offset_after_table)."""
    off = 0
    tracks = []
    while True:
        if off >= len(data):
            raise SoundParseError("ran off the end looking for the 0xFF track terminator")
        if data[off] == 0xFF:
            off += 1
            break
        track_type = data[off]
        off += 1
        channels = []
        while True:
            if off >= len(data):
                raise SoundParseError("ran off the end inside a channel list")
            if data[off] == 0xFF:
                off += 1
                break
            if off + 6 > len(data):
                raise SoundParseError("truncated channel entry")
            a, b, d_off, d_size = struct.unpack_from("<BBHH", data, off)
            off += 6
            channels.append({"a": a, "b": b, "offset": d_off, "size": d_size})
        tracks.append({"type": track_type, "channels": channels})
    return tracks, off


def pick_track(tracks):
    """Pick the MT-32 track, else AdLib, ignoring alias stubs."""
    for want in PREFERENCE:
        for tr in tracks:
            if tr["type"] == want and tr["channels"]:
                return tr
    for tr in tracks:
        if tr["type"] not in ALIAS_TRACK_TYPES and tr["channels"]:
            return tr
    return None


# --------------------------------------------------------------------------- #
# SCI-MIDI -> event list
# --------------------------------------------------------------------------- #

def decode_channel(blob: bytes, label: str):
    """
    Decode one SCI channel blob.

    Returns dict with:
        events     list of (delta_ticks, bytes)   -- raw MIDI messages
        total      total ticks
        note_on    count of note-ons  (velocity > 0)
        note_off   count of note-offs (0x80, or 0x90 with velocity 0)
        ended      True if a 0xFC end-of-track byte was reached
        channel    MIDI channel from the blob header
        warnings   list of strings
    """
    out = {"events": [], "total": 0, "note_on": 0, "note_off": 0,
           "ended": False, "channel": None, "poly": None, "warnings": []}

    if len(blob) < 2:
        out["warnings"].append("%s: blob shorter than its 2-byte header" % label)
        return out

    out["channel"] = blob[0] & 0x0F
    out["poly"] = blob[1] & 0x0F

    i = 2
    status = 0
    delta_acc = 0        # ticks not yet attached to an emitted event

    while i < len(blob):
        # ---- delta time, with the 0xF8 "+240 ticks, read another" extension ----
        delta = 0
        while i < len(blob) and blob[i] == DELTA_EXTEND:
            delta += DELTA_EXTEND_TICKS
            i += 1
        if i >= len(blob):
            out["warnings"].append("%s: data ends inside a delta time" % label)
            break
        if blob[i] >= 0xF8:
            out["warnings"].append(
                "%s: unexpected byte 0x%02X in the delta position at +%d"
                % (label, blob[i], i))
            break
        delta += blob[i]
        i += 1
        delta_acc += delta
        out["total"] += delta

        if i >= len(blob):
            out["warnings"].append("%s: data ends after a delta time" % label)
            break

        # ---- status byte, or running status ----
        b = blob[i]
        if b & 0x80:
            status = b
            i += 1
        elif status == 0:
            out["warnings"].append(
                "%s: running status used before any status byte at +%d" % (label, i))
            break

        if status == END_OF_TRACK:
            out["ended"] = True
            break

        high = status & 0xF0

        if high in (0x80, 0x90, 0xA0, 0xB0, 0xE0):
            if i + 2 > len(blob):
                out["warnings"].append("%s: truncated 2-byte event at +%d" % (label, i))
                break
            d1, d2 = blob[i], blob[i + 1]
            i += 2
            if high == 0x90:
                if d2 == 0:
                    out["note_off"] += 1
                else:
                    out["note_on"] += 1
            elif high == 0x80:
                out["note_off"] += 1
            out["events"].append((delta_acc, bytes((status, d1, d2))))
            delta_acc = 0

        elif high in (0xC0, 0xD0):
            if i + 1 > len(blob):
                out["warnings"].append("%s: truncated 1-byte event at +%d" % (label, i))
                break
            d1 = blob[i]
            i += 1
            out["events"].append((delta_acc, bytes((status, d1))))
            delta_acc = 0

        elif status == 0xF0:
            j = i
            while j < len(blob) and blob[j] != 0xF7:
                j += 1
            if j >= len(blob):
                out["warnings"].append("%s: unterminated SysEx at +%d" % (label, i))
                break
            payload = blob[i:j]
            i = j + 1
            # SMF stores SysEx as F0 <varlen> <payload> F7
            out["events"].append(
                (delta_acc, b"\xf0" + write_varlen(len(payload) + 1) + payload + b"\xf7"))
            delta_acc = 0
            status = 0   # SysEx cancels running status

        else:
            out["warnings"].append(
                "%s: unhandled status 0x%02X at +%d" % (label, status, i))
            break

    if not out["ended"]:
        out["warnings"].append("%s: no 0xFC end-of-track marker" % label)

    return out


# --------------------------------------------------------------------------- #
# SMF writing
# --------------------------------------------------------------------------- #

def write_varlen(value: int) -> bytes:
    if value < 0:
        raise ValueError("negative varlen")
    buf = bytearray([value & 0x7F])
    value >>= 7
    while value:
        buf.append((value & 0x7F) | 0x80)
        value >>= 7
    return bytes(reversed(buf))


def meta(kind: int, payload: bytes) -> bytes:
    return b"\xff" + bytes((kind,)) + write_varlen(len(payload)) + payload


def make_mtrk(events) -> bytes:
    """events: list of (delta, raw_message_bytes). Appends the required EOT meta."""
    body = bytearray()
    for delta, msg in events:
        body += write_varlen(delta)
        body += msg
    body += write_varlen(0) + meta(0x2F, b"")
    return b"MTrk" + struct.pack(">I", len(body)) + bytes(body)


def make_smf(tracks) -> bytes:
    header = b"MThd" + struct.pack(">IHHH", 6, 1, len(tracks), DIVISION)
    return header + b"".join(tracks)


# --------------------------------------------------------------------------- #
# driver
# --------------------------------------------------------------------------- #

def convert(path: str, out_dir: str, verbose: bool = False):
    name = os.path.splitext(os.path.basename(path))[0]
    with open(path, "rb") as fh:
        data = fh.read()

    tracks, table_end = parse_tracks(data)
    chosen = pick_track(tracks)
    if chosen is None:
        raise SoundParseError("%s: no usable device track" % name)

    first_blob = min(c["offset"] for t in tracks for c in t["channels"])
    pad = first_blob - table_end   # 1 normally, 0 for 100.sound

    warnings = []
    if pad not in (0, 1):
        warnings.append("unexpected %d-byte gap between table and first blob" % pad)

    # Tempo / conductor track.
    smf_tracks = [make_mtrk([
        (0, meta(0x03, ("jones %s (%s)" % (name, DEVICE_NAMES.get(chosen["type"],
                                                                  "0x%02X" % chosen["type"]))
                        ).encode("ascii", "replace"))),
        (0, meta(0x51, struct.pack(">I", TEMPO_US)[1:])),
    ])]

    note_on = note_off = 0
    max_ticks = 0

    for idx, ch in enumerate(chosen["channels"]):
        blob = data[ch["offset"]:ch["offset"] + ch["size"]]
        if len(blob) != ch["size"]:
            warnings.append("channel %d blob truncated by the file" % idx)
        label = "%s ch#%d" % (name, idx)
        dec = decode_channel(blob, label)
        warnings.extend(dec["warnings"])
        note_on += dec["note_on"]
        note_off += dec["note_off"]
        max_ticks = max(max_ticks, dec["total"])

        evs = [(0, meta(0x03, ("ch%02d" % (dec["channel"] if dec["channel"] is not None
                                           else idx)).encode("ascii")))]
        evs.extend(dec["events"])
        smf_tracks.append(make_mtrk(evs))

    out_path = os.path.join(out_dir, "%s.mid" % name)
    with open(out_path, "wb") as fh:
        fh.write(make_smf(smf_tracks))

    return {
        "name": name,
        "out": out_path,
        "device": chosen["type"],
        "channels": len(chosen["channels"]),
        "note_on": note_on,
        "note_off": note_off,
        "ticks": max_ticks,
        "seconds": max_ticks / float(DIVISION),
        "bytes": os.path.getsize(out_path),
        "pad": pad,
        "warnings": warnings,
        "track_types": [t["type"] for t in tracks],
    }


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--in-dir", default=DEFAULT_IN)
    ap.add_argument("--out", default=DEFAULT_OUT)
    args = ap.parse_args(argv)

    files = glob.glob(os.path.join(args.in_dir, "*.sound"))
    if not files:
        raise SystemExit("no *.sound files under %s" % args.in_dir)

    def key(p):
        base = os.path.splitext(os.path.basename(p))[0]
        return (0, int(base)) if base.isdigit() else (1, base)
    files.sort(key=key)

    os.makedirs(args.out, exist_ok=True)

    print("%-10s %-12s %5s %7s %8s %9s %10s" %
          ("resource", "device", "chans", "note-on", "note-off", "duration", "mid bytes"))
    print("-" * 72)

    results = []
    all_warnings = []
    mismatches = []
    for p in files:
        try:
            r = convert(p, args.out)
        except SoundParseError as exc:
            print("ERROR %s" % exc)
            all_warnings.append(str(exc))
            continue
        results.append(r)
        ok = "" if r["note_on"] == r["note_off"] else "  <-- MISMATCH"
        if ok:
            mismatches.append(r["name"])
        print("%-10s %-12s %5d %7d %8d %8.2fs %10d%s" %
              (r["name"], DEVICE_NAMES.get(r["device"], "0x%02X" % r["device"]),
               r["channels"], r["note_on"], r["note_off"], r["seconds"],
               r["bytes"], ok))
        for w in r["warnings"]:
            all_warnings.append("%s: %s" % (r["name"], w))

    print("-" * 72)
    print("converted  : %d/%d resources -> %s" % (len(results), len(files), args.out))
    print("note-on    : %d total" % sum(r["note_on"] for r in results))
    print("note-off   : %d total" % sum(r["note_off"] for r in results))
    print("balanced   : %s" % ("YES, every resource matches" if not mismatches
                               else "NO -- " + ", ".join(mismatches)))
    print("total music: %.1f s (%.1f min) if played end to end"
          % (sum(r["seconds"] for r in results),
             sum(r["seconds"] for r in results) / 60.0))
    print("pad byte   : present in %d, absent in %s"
          % (sum(1 for r in results if r["pad"] == 1),
             [r["name"] for r in results if r["pad"] != 1] or "none"))
    devices = {}
    for r in results:
        devices[r["device"]] = devices.get(r["device"], 0) + 1
    print("devices    : %s"
          % {DEVICE_NAMES.get(k, hex(k)): v for k, v in sorted(devices.items())})
    if all_warnings:
        print("\nwarnings (%d):" % len(all_warnings))
        for w in all_warnings:
            print("  " + w)
    else:
        print("warnings   : none")
    return 0


if __name__ == "__main__":
    sys.exit(main())
