#!/usr/bin/env python3
"""
extract_speech.py -- extract the CD speech of "Jones in the Fast Lane" (Sierra SCI1)
to plain WAV files.

Source format (verified against the real bytes, see assets/audio/README.md):

  original/cd/audio001.map   index, 5340 bytes = 533 records + 10-byte 0xFF terminator
                             each record, little-endian, 10 bytes:
                                 u16 audioId
                                 u32 packed   -> offset = packed & 0x0FFFFFFF
                                                 volume = packed >> 28   (always 2)
                                 u32 size     -> byte length of the raw sample
  original/cd/audio001.002   the sample bank named by volume 2. Headerless raw PCM:
                             unsigned 8-bit, mono, 11025 Hz. Silence is 0x80.
                             No compression, no per-clip header.

Output: assets/audio/speech/<audioId>.wav -- canonical 44-byte RIFF/WAVE header
followed by the source bytes copied verbatim (8-bit PCM is already unsigned in
both formats, so no sample conversion is needed).

Standard library only. Python 3.12.
"""

from __future__ import annotations

import argparse
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

DEFAULT_MAP = os.path.join(ROOT, "original", "cd", "audio001.map")
DEFAULT_CD_DIR = os.path.join(ROOT, "original", "cd")
DEFAULT_OUT = os.path.join(ROOT, "assets", "audio", "speech")

SAMPLE_RATE = 11025
CHANNELS = 1
BITS = 8

RECORD_SIZE = 10
TERMINATOR_SIZE = 10


class MapRecord(object):
    __slots__ = ("audio_id", "offset", "volume", "size")

    def __init__(self, audio_id: int, offset: int, volume: int, size: int) -> None:
        self.audio_id = audio_id
        self.offset = offset
        self.volume = volume
        self.size = size

    def __repr__(self) -> str:
        return "MapRecord(id=%d, off=%d, vol=%d, size=%d)" % (
            self.audio_id, self.offset, self.volume, self.size)


def read_map(path: str) -> list:
    """Parse audio001.map into MapRecords, validating the trailing 0xFF terminator."""
    with open(path, "rb") as fh:
        data = fh.read()

    if len(data) < TERMINATOR_SIZE:
        raise ValueError("%s is too small to be an SCI1 audio map" % path)

    body_len = len(data) - TERMINATOR_SIZE
    if body_len % RECORD_SIZE != 0:
        raise ValueError(
            "%s: %d bytes is not 10*N + 10; not an SCI1 audio map" % (path, len(data)))

    terminator = data[body_len:]
    if terminator != b"\xff" * TERMINATOR_SIZE:
        raise ValueError(
            "%s: expected 10 bytes of 0xFF at the end, found %s"
            % (path, terminator.hex(" ")))

    records = []
    for i in range(body_len // RECORD_SIZE):
        audio_id, packed, size = struct.unpack_from("<HII", data, i * RECORD_SIZE)
        records.append(MapRecord(audio_id, packed & 0x0FFFFFFF, packed >> 28, size))
    return records


def wav_header(data_len: int) -> bytes:
    """Canonical 44-byte RIFF/WAVE header for mono 8-bit unsigned PCM."""
    byte_rate = SAMPLE_RATE * CHANNELS * BITS // 8
    block_align = CHANNELS * BITS // 8
    return b"".join((
        b"RIFF",
        struct.pack("<I", 36 + data_len),   # size of everything after this field
        b"WAVE",
        b"fmt ",
        struct.pack("<I", 16),              # PCM fmt chunk length
        struct.pack("<H", 1),               # wFormatTag = WAVE_FORMAT_PCM
        struct.pack("<H", CHANNELS),
        struct.pack("<I", SAMPLE_RATE),
        struct.pack("<I", byte_rate),
        struct.pack("<H", block_align),
        struct.pack("<H", BITS),
        b"data",
        struct.pack("<I", data_len),
    ))


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--map", default=DEFAULT_MAP, help="path to audio001.map")
    ap.add_argument("--cd-dir", default=DEFAULT_CD_DIR,
                    help="directory holding audio001.00N sample banks")
    ap.add_argument("--out", default=DEFAULT_OUT, help="output directory for WAVs")
    args = ap.parse_args(argv)

    records = read_map(args.map)
    print("map        : %s" % args.map)
    print("records    : %d (ids %d..%d)"
          % (len(records), min(r.audio_id for r in records),
             max(r.audio_id for r in records)))

    volumes = sorted({r.volume for r in records})
    print("volumes    : %s" % volumes)

    # Open each referenced sample bank once.
    banks = {}
    try:
        for vol in volumes:
            bank_path = os.path.join(args.cd_dir, "audio001.%03d" % vol)
            if not os.path.exists(bank_path):
                raise SystemExit("missing sample bank: %s" % bank_path)
            banks[vol] = open(bank_path, "rb")
            print("bank vol %d : %s (%d bytes)"
                  % (vol, bank_path, os.path.getsize(bank_path)))

        os.makedirs(args.out, exist_ok=True)

        written = 0
        total_pcm = 0
        total_file = 0
        odd = 0
        short = []
        seen_ids = set()

        for rec in records:
            if rec.audio_id in seen_ids:
                print("WARNING: duplicate audioId %d, later record wins" % rec.audio_id)
            seen_ids.add(rec.audio_id)

            fh = banks[rec.volume]
            fh.seek(rec.offset)
            pcm = fh.read(rec.size)
            if len(pcm) != rec.size:
                short.append((rec.audio_id, rec.size, len(pcm)))

            out_path = os.path.join(args.out, "%d.wav" % rec.audio_id)
            with open(out_path, "wb") as out:
                out.write(wav_header(len(pcm)))
                out.write(pcm)
                # RIFF chunks are word-aligned: an odd-length data chunk needs a
                # pad byte that is NOT counted in the chunk size.
                if len(pcm) & 1:
                    out.write(b"\x00")
                    odd += 1

            written += 1
            total_pcm += len(pcm)
            total_file += 44 + len(pcm) + (len(pcm) & 1)
    finally:
        for fh in banks.values():
            fh.close()

    print("")
    print("wrote      : %d WAV files -> %s" % (written, args.out))
    print("pcm bytes  : %d (%.2f MiB)" % (total_pcm, total_pcm / 1048576.0))
    print("file bytes : %d (%.2f MiB)" % (total_file, total_file / 1048576.0))
    print("duration   : %.1f s total (%.1f min) at %d Hz"
          % (total_pcm / SAMPLE_RATE, total_pcm / SAMPLE_RATE / 60.0, SAMPLE_RATE))
    print("odd-length : %d clips needed a RIFF pad byte" % odd)
    if short:
        print("TRUNCATED  : %d clips read short: %s" % (len(short), short[:10]))
    else:
        print("truncated  : none -- every clip read its full declared length")
    return 0


if __name__ == "__main__":
    sys.exit(main())
