"""
Build assets/audio/subtitles.json: CD speech audio id -> English line.

Why this is possible at all
---------------------------
The CD release replaced the floppy release's printed dialogue with recorded
speech.  Wherever the floppy calls

    (Print <textRes> <index> ...)          ; or proc104_1 / Format-into-Print

the CD calls, in the *same method of the same script*,

    (proc0_18 <audioId> ...)               ; and (talker init: 16 MouthSync <audioId>)

Audio ids were handed out in text-index order, one per *spoken* line, starting
from a per-script base.  Text entries that the floppy only ever used as button
or price labels (a `doFormat` method) were never recorded, so they create gaps
in the numbering.  The table below encodes that, anchor by anchor, from
literal id <-> literal index pairs read out of the two decompilations.

The strings themselves come from the FLOPPY resource volumes, because the CD
build stubbed out most of its own text resources (e.g. CD 204.text is 32 bytes,
floppy 204.text is 1867).  Floppy resources use SCI's method-2 compression
(LZW1 / "comp3", MSB-first, 9..12 bit codes); the decoder below was verified
byte-for-byte against the nine text resources the CD kept intact.

Standard library only.  Usage:  python build_subtitles.py [--check]
"""

import json
import os
import struct
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FLOPPY = os.path.join(ROOT, "original", "floppy")
CD_SRC = os.path.join(ROOT, "scripts", "jones-cd-dos-1.0", "src")
SPEECH = os.path.join(ROOT, "assets", "audio", "speech")
OUT = os.path.join(ROOT, "assets", "audio", "subtitles.json")


# --------------------------------------------------------------------------
# floppy resource reading (SCI1-early volumes)
# --------------------------------------------------------------------------

class _BitsMSB:
    __slots__ = ("d", "pos", "acc", "n")

    def __init__(self, data):
        self.d = data
        self.pos = 0
        self.acc = 0
        self.n = 0

    def get(self, k):
        while self.n < k:
            b = self.d[self.pos] if self.pos < len(self.d) else 0
            self.pos += 1
            self.acc = ((self.acc << 8) | b) & 0xFFFFFFFFFF
            self.n += 8
        self.n -= k
        return (self.acc >> self.n) & ((1 << k) - 1)


def lzw1(payload, n_unpacked):
    """SCI 'comp3' / LZW1.  Verified against the CD's own copies of text
    0, 108, 205, 209, 210, 231, 700, 764 and 999 (exact byte match)."""
    bs = _BitsMSB(payload)
    numbits, endtoken, curtoken = 9, 0x1FF, 0x102
    nxt = [0] * 0x1010
    dat = [0] * 0x1010
    out = bytearray()
    lastbits, lastchar = -1, 0
    while len(out) < n_unpacked:
        tok = bs.get(numbits)
        if tok == 0x101:
            break
        if tok == 0x100:
            numbits, endtoken, curtoken, lastbits = 9, 0x1FF, 0x102, -1
            continue
        if tok >= 0x100:
            if tok >= curtoken:              # KwKwK
                stack, cur = [lastchar], lastbits
            else:
                stack, cur = [], tok
            while cur > 0xFF:
                stack.append(dat[cur])
                cur = nxt[cur]
                if len(stack) > 0x1010:
                    raise ValueError("token chain cycle")
            stack.append(cur)
            stack.reverse()
            lastchar = stack[0]
            out += bytes(stack)
        else:
            lastchar = tok
            out.append(tok)
        if lastbits >= 0 and curtoken <= endtoken:
            nxt[curtoken] = lastbits
            dat[curtoken] = lastchar
            curtoken += 1
            if curtoken >= endtoken and numbits < 12:
                numbits += 1
                endtoken = (endtoken << 1) + 1
        lastbits = tok
    return bytes(out)


def load_floppy_text():
    """{resourceNumber: [string, ...]} for every text resource in the floppy."""
    mp = open(os.path.join(FLOPPY, "resource.map"), "rb").read()
    vols = {}
    res = {}
    for i in range(0, len(mp) - 5, 6):
        rid, packed = struct.unpack_from("<HI", mp, i)
        if rid == 0xFFFF:
            break
        rtype, num = rid >> 11, rid & 0x7FF
        if rtype != 3:                                   # 3 == text
            continue
        vol, off = packed >> 26, packed & 0x3FFFFFF
        if num in res:
            continue
        if vol not in vols:
            vols[vol] = open(os.path.join(FLOPPY, "resource.%03d" % vol), "rb").read()
        d = vols[vol]
        _, comp, decomp, meth = struct.unpack_from("<HHHH", d, off)
        payload = d[off + 8: off + 4 + comp]
        if meth == 0:
            raw = payload[:decomp] if decomp else payload
        elif meth == 2:
            raw = lzw1(payload, decomp)
        else:
            raise ValueError("text %d: unsupported compression %d" % (num, meth))
        if len(raw) != decomp:
            raise ValueError("text %d: %d bytes, expected %d" % (num, len(raw), decomp))
        parts = raw.split(b"\x00")
        if parts and parts[-1] == b"":
            parts.pop()
        res[num] = [p.decode("latin-1") for p in parts]
    return res


# --------------------------------------------------------------------------
# audio id  <->  (text resource, index)
# --------------------------------------------------------------------------
# Each entry is (textResource, firstIndex, lastIndex, idOfFirstIndex).  Ids run
# consecutively over the index range.  Every range below is pinned by at least
# one literal id / literal index pair that appears in BOTH decompilations in the
# same instance+method; the anchors are named in the comments.

RANGES = [
    # script      res  from  to   baseId   anchors (CD id <-> floppy res/index)
    ("clothing",  209,   0,  30,     10),  # notEnoughCash 40 <-> 209/30
    ("factory",   205,   0,   7,     50),  # init (+temp1 50) <-> 205/(Random 0 7)
    ("pawnShop",  212,   0,  16,     70),  # notEnoughCash 86 <-> 212/16
    ("pawnShop",  212,  20,  25,     87),  # pawnButton 87 <-> 212/20, pawn 92 <-> 212/25
                                           # 212/17,18,19 are Format-only (localproc_3, no audio)
    ("appliance", 208,   0,  47,    100),  # notEnoughCash 147 <-> 208/47
    ("rentOffice", 201,  0,  38,    160),  # notEnoughCash 183 <-> 201/23; payGarnishment 198 <-> 201/38
    ("lowcost",   200,   0,   0,    210),  # relaxButton 210 <-> 200/0
    ("broker",    213,   6,   7,    220),  # sellButton 220 <-> 213/6, 221 <-> 213/7
    ("broker",    213,   0,   0,    222),  # notEnoughCash 222 <-> 213/0
    ("weekend",   232,   1,  60,    230),  # (proc0_18 (+ (- temp0 1) 230)) beside (Display 232 temp0)
    ("bank",      204,   0,  14,    300),  # notEnoughCash 310 <-> 204/10
    ("bank",      204,  16,  25,    315),  # loanPayment 315 <-> 204/16, seeBroker 324 <-> 204/25
                                           # 204/15 is loanPayment doFormat ("%s $%d"), no audio
    ("discount",  211,   0,  27,    330),  # notEnoughCash 357 <-> 211/27
    ("security",  202,   0,   0,    370),  # relaxButton 370 <-> 202/0
    ("university", 207,  0,  23,    380),  # UniversityDIcon 402 <-> 207/22, 403 <-> 207/23
    ("university", 207, 25,  25,    409),  # notEnoughCash 409 <-> 207/25 (out of sequence)
    ("university", 207, 26,  30,    404),  # init 404 <-> 207/26, enrollButton 408 <-> 207/30
                                           # 207/24 is localproc_4 Format-only, no audio
    ("employment", 206,  0,  13,    420),  # proc206_1 (+ global433 420) <-> (Format 206 global433)
    ("newspaper", 215,   1,  63,    460),  # (proc0_18 (+ (- global415 1) 460)) beside (Format 215 64 215 global415)
    ("market",    203,   0,  47,    530),  # notEnoughCash 577 <-> 203/47
    ("market",    203,  52,  52,    578),  # newspaper/doit 578 <-> 203/52
    ("room1",       1,   0,   0,    600),  # players/doit 600 <-> 1/0
    ("fastFood",  210,   0,  55,    610),  # notEnoughCash 665 <-> 210/55
]

# script 108 ("go to work").  The CD recorded one take per employer, so a single
# floppy string maps to many ids: (+ global400 900) etc, where global400 is the
# location you work at.  Only these nine locations are employers.
EMPLOYER_LOCATIONS = [1, 3, 4, 5, 7, 8, 9, 10, 11]
N108 = [(900, 0), (920, 1), (940, 2), (960, 3)]

# goalsDefine (script 229) has no floppy text resource; the CD script itself
# still carries the four narration strings inline, next to (DoAudio audPLAY
# (+ local0 590)).  Pulled straight out of the CD source.
GOALS_MARKERS = ["Wealth is defined", "Happiness is accumulated",
                 "Education is accumulated", "Career is achieved"]

# employment: the floppy built this message at run time from text 206/13
# ("Sorry. You didn't get the job for the following reasons:") plus a StrCat'd
# literal reason.  The CD dropped the composition and plays 434..437 instead.
# Those four WAVs are only 0.9-1.3s, far too short for the preamble, so each
# recording is the reason clause alone.  The branch order comes from the CD's
# `[global325 (Random 0 2)]`, i.e. global325 = education, global326 = work
# history, global327 = experience (matching the floppy's StrCat order).
EMPLOY_COMPOSED = {
    434: "Not enough education.",
    435: "Poor work history.",
    436: "Not enough experience.",
    437: "No openings.",
}

# The CD moved the closing question of these two lines into its own text
# resource (CD 201/0 "Rent Low-Cost Apartment?", CD 201/1 "Rent Security
# Apartment?") and shows it on the Yes/No button box, so the recording only
# covers the first sentence.  The floppy string separates the two with \n.
FIRST_LINE_ONLY = {193, 197}


def goals_strings():
    txt = open(os.path.join(CD_SRC, "goalsDefine.sc"), encoding="latin-1").read()
    out = []
    for marker in GOALS_MARKERS:
        i = txt.index(marker)
        a = txt.rindex("{", 0, i)
        b = txt.index("}", i)
        out.append(" ".join(txt[a + 1:b].split()))
    return out


def load_cd_text():
    """{resourceNumber: [string, ...]} for the text resources the CD kept."""
    d = os.path.join(ROOT, "assets", "raw", "text")
    res = {}
    for name in os.listdir(d):
        if not name.endswith(".text"):
            continue
        parts = open(os.path.join(d, name), "rb").read().split(b"\x00")
        if parts and parts[-1] == b"":
            parts.pop()
        res[int(name[:-5])] = [p.decode("latin-1") for p in parts]
    return res


def merge_text(floppy, cd):
    """Prefer the CD's own wording where the CD still ships the whole resource.

    Most CD text resources were stubbed down to a couple of printf templates,
    but the ones the CD still draws on screen (108, 205, 209, 210, 215, 232, ...)
    survived intact, sometimes with small editorial fixes.  Those are the words
    the CD actually speaks, so they win when the string count matches."""
    out = dict(floppy)
    for num, strings in cd.items():
        if num in floppy and len(strings) == len(floppy[num]):
            out[num] = strings
    return out


def build():
    floppy = load_floppy_text()
    text = merge_text(floppy, load_cd_text())
    subs = {}
    provenance = {}

    def put(aid, s, how):
        s = s.replace("\r\n", "\n").strip()
        if aid in FIRST_LINE_ONLY:
            s = s.split("\n")[0]
            how += " (first sentence only)"
        s = " ".join(s.split())
        if aid in subs and subs[aid] != s:
            raise ValueError("conflicting subtitle for %d" % aid)
        subs[aid] = s
        provenance[aid] = how

    for script, res, lo, hi, base in RANGES:
        strings = text[res]
        for idx in range(lo, hi + 1):
            put(base + idx - lo, strings[idx], "%s text %d/%d" % (script, res, idx))

    # script 108
    s108 = text[108]
    for base, idx in N108:
        for loc in EMPLOYER_LOCATIONS:
            put(base + loc, s108[idx], "n108 text 108/%d (loc %d)" % (idx, loc))
    for loc in EMPLOYER_LOCATIONS:
        # worksAt 1 is the rent office itself -> "I'm sorry. I had to garnish",
        # everyone else -> "Your Landlord garnished".
        idx = 4 if loc == 1 else 5
        put(980 + loc, s108[idx], "n108 text 108/%d (loc %d)" % (idx, loc))

    # goalsDefine narration
    for i, s in enumerate(goals_strings()):
        put(590 + i, s, "goalsDefine inline string %d" % i)

    # employment: the four job-rejection reasons the CD recorded on their own
    for aid, tail in EMPLOY_COMPOSED.items():
        put(aid, tail, "employment reason clause (reconstructed)")

    return subs, provenance


def main():
    subs, prov = build()
    have = set(int(f[:-4]) for f in os.listdir(SPEECH) if f.endswith(".wav"))
    missing = sorted(have - set(subs))
    extra = sorted(set(subs) - have)

    out = {str(k): subs[k] for k in sorted(subs)}
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1, ensure_ascii=False, sort_keys=False)

    print("wav files          : %d" % len(have))
    print("subtitles written  : %d  -> %s" % (len(out), OUT))
    print("wavs with no line  : %d %s" % (len(missing), missing[:20]))
    print("lines with no wav  : %d %s" % (len(extra), extra[:20]))
    fmt = [k for k, v in subs.items() if "%" in v]
    print("lines still holding a printf placeholder: %d %s" % (len(fmt), sorted(fmt)))
    if "--check" in sys.argv:
        for k in sorted(subs):
            print("%4d  %-44s  %s" % (k, prov[k], subs[k][:70]))


if __name__ == "__main__":
    main()
