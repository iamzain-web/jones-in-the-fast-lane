"""Generate the spoken shopkeeper GREETINGS with Kokoro-82M (Apache-2.0), in the game's
own audio format, and stage them beside Sierra's clips rather than on top of them.

WHAT THIS IS FOR
    `MainViewModel.Speak(audioId)` plays `assets/audio/speech/<audioId>.wav` through MCI
    and drives the mouth from `assets/audio/sync/<audioId>.sync`. So a replacement clip
    is simply a file of the same name in the same format: 11025 Hz, 8-bit unsigned, mono
    PCM. Nothing in the game needs changing for new speech to be heard.

THE ONE ABSOLUTE RULE, AND HOW IT IS KEPT
    **Nothing in `assets/audio/speech` is read by this script, at any point, for any
    reason** - not as a voice reference, not for timing, not for level matching, not for
    analysis. There is no code path here that opens that directory. The only inputs are
    `assets/audio/subtitles.json` (TEXT, which is the floppy build's own printed text) and
    the Kokoro weights. Keeping that true is why durations are not matched to the
    originals and why the per-clip gain is a fixed normalisation rather than a match.

WHAT IS NOT CHANGED
    Not one line's WORDS. The text spoken is the subtitle verbatim. `tts_input()` does
    PRONUNCIATION normalisation only - rejoining words the 1990 text wrapper split with a
    hyphen, straightening quotes, dropping (R)/(TM) marks - and every one of those
    rewrites is listed by `--show-text` so it can be checked. 37 of the 533 lines name a
    shop we have since renamed; those are reported by `--renames` and are deliberately
    left saying the old name. Renaming is a separate decision.

VOICES
    One per character, chosen against the shopkeeper's own talker portrait (view 351-362)
    and the tone of that location's lines. See VOICES below for the reasoning. The three
    women get the three highest-rated female voices in the model outright; the eight men
    share the three highest-rated American male voices plus bm_george for the professor,
    separated by tempo, because Kokoro's remaining male voices are a clear step down in
    naturalness and sounding synthetic is the failure mode that was already rejected once.

USAGE
    gen_voice.py --show-text          what will be spoken, with every normalisation shown
    gen_voice.py --renames            clips naming a shop the board no longer calls that
    gen_voice.py --generate           synthesise into assets/audio/speech-kokoro
    gen_voice.py --generate --only 300,301
"""
import argparse
import json
import os
import re
import struct
import sys
import time

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SUBS = os.path.join(ROOT, "assets", "audio", "subtitles.json")
OUT = os.path.join(ROOT, "assets", "audio", "speech-kokoro")

# Kokoro v1.0 ONNX, already on disk. fp32 and NOT fp16: onnxruntime's CPU provider emits
# all-NaN from the fp16 export, which the library's own trim then hides by returning an
# empty array.
MODEL_DIR = os.environ.get("JONES_KOKORO_DIR", r"C:\Users\Zain.Cassimjee\AppData\Local\Temp"
                           r"\claude\C--AI-Accelerator"
                           r"\1709fd27-425c-4815-a187-5d85a4ff583f\scratchpad\kokoro")

GAME_RATE = 11025          # the rate every one of Sierra's clips is in


# --------------------------------------------------------------------------- cast
# audioId bands are SpeechBands.For() in Audio/IAudioPlayer.cs, which reads each script's
# own greeting call. They are inclusive and the offsets are NOT uniform - the Employment
# Office greets from 426, six above its base, because 420-425 are application outcomes.
#
# "portrait" is the location's `theTalker` view, from `(instance theTalker of Talker)` in
# the CD scripts. The voice was picked by looking at that portrait.
VOICES = [
    dict(loc="QtClothing",      first=10,  last=18,  portrait=359, voice="am_puck",    speed=1.04,
         who="young fashionable man, tan jacket",
         why="bright and young; brisk boutique patter"),
    dict(loc="Factory",         first=50,  last=57,  portrait=355, voice="af_nicole",  speed=1.02,
         who="woman, big red hair, broad grin",
         why="B-grade, light and smiling - the cheer against 'we will overwork you' IS the joke"),
    dict(loc="PawnShop",        first=70,  last=77,  portrait=362, voice="am_fenrir",  speed=0.88,
         who="bearded man in an eyeshade",
         why="the fullest male voice, slowed hard - 'a heart of stone' needs weight, not pace"),
    dict(loc="SocketCity",      first=100, last=115, portrait=358, voice="am_michael", speed=1.10,
         who="salesman, shirt and tie",
         why="warm and fast - Home of High Pressure Sales talks over you"),
    dict(loc="RentOffice",      first=160, last=167, portrait=351, voice="af_bella",   speed=0.95,
         who="middle-aged woman, severe glasses",
         why="A- grade, the most expressive female voice, slowed flat for 'Don't snivel'"),
    dict(loc="Bank",            first=300, last=309, portrait=354, voice="af_heart",   speed=0.90,
         who="older prim woman, glasses, bouffant",
         why="the model's best voice (A), slowed: ten lines open with the full P.I.G.G.Y. name "
             "and it has to be delivered straight to land"),
    dict(loc="ZMart",           first=330, last=340, portrait=361, voice="am_michael", speed=0.96,
         who="young man, cream jacket",
         why="same voice as Socket City at a markedly slower, flatter tempo - deadpan "
             "discount-store announcer, and the two shops are far apart on the board"),
    dict(loc="HiTechU",         first=380, last=401, portrait=357, voice="bm_george",  speed=0.95,
         who="bearded man in a mortarboard",
         why="the one place a British voice is the trope rather than an oddity; slow reads "
             "as professorial"),
    dict(loc="EmploymentOffice", first=426, last=432, portrait=356, voice="am_puck",   speed=0.98,
         who="man, moustache, mullet, tie",
         why="same voice as QT Clothing, slower and more level - an agency man reciting a "
             "line he has said all week"),
    dict(loc="BlacksMarket",    first=530, last=543, portrait=353, voice="am_fenrir",  speed=1.02,
         extra=(545,),
         who="plain young man, grocer",
         why="same voice as the pawnbroker but at normal pace, which lifts it entirely; the "
             "puns ('unherd of', 'chew the fat') need clear consonants"),
    dict(loc="MonolithBurgers", first=610, last=629, portrait=360, voice="am_puck",    speed=1.12,
         extra=(657,),
         who="teenager in a paper hat",
         why="fastest setting of the youngest voice - bored counter staff reading the script"),
]

# `extra` IS NOT A WIDER BAND, AND THE DIFFERENCE MATTERS.
#
# `first`/`last` is the GREETING band - `SpeechBands.For()` in Audio/IAudioPlayer.cs, the
# range a location draws from when you walk in. The shops also speak when you BUY, out of
# `ShopSpeech.For()`, and those bands are much bigger: Black's Market purchases run
# 545..576 and the burger shop's run 630..664. Generating all of them is 66 more clips and
# nobody has asked for them, so the bands stay as they are.
#
# What `extra` holds is the handful of ids OUTSIDE the greeting band that the renaming of a
# building forced us to touch, each one named individually:
#
#     545   market.sc's FIRST purchase line, and it is Bare Necessities' - Black's Market is
#           what the board now calls Bare Necessities, so this clerk's "I usedta work at
#           Monolith Burger" had to move with the burger shop's rename. Same shop, same
#           talker (353), so it takes BlacksMarket's own voice and speed, not a new one.
#     657   fastFood.sc's purchase line "if it's a 6-lb. Beefburger, it must be a Monolith" -
#           the rename is the whole joke, so it could not be left alone either. The burger
#           shop's own voice and speed.
#
# Both are NEW coverage: neither existed in speech-kokoro before, so until they do the
# player hears Sierra saying the old name under a balloon showing the new one.
EXTRA_WHY = {545: "BlacksMarket purchase line, renamed with the burger shop",
             657: "MonolithBurgers purchase line, the rename is the joke"}


def band_for(audio_id):
    for b in VOICES:
        if b["first"] <= audio_id <= b["last"] or audio_id in b.get("extra", ()):
            return b
    return None


def all_ids():
    ids = []
    for b in VOICES:
        ids.extend(range(b["first"], b["last"] + 1))
        ids.extend(b.get("extra", ()))
    return sorted(ids)


# ------------------------------------------------------------------- text for the TTS
# Every entry is a PRONUNCIATION fix, never a change to the line. The subtitle the player
# reads in the balloon is untouched - it comes from subtitles.json at runtime and this
# script never writes that file.
def tts_input(text):
    """The subtitle, respelled for espeak. Returns (string, [notes])."""
    notes = []
    out = text

    for a, b, what in [("\u2018", "'", "curly quote"), ("\u2019", "'", "curly quote"),
                       ("\u201c", "", "curly quote"), ("\u201d", "", "curly quote")]:
        if a in out:
            out = out.replace(a, b)
            notes.append(what)

    # The 1990 text was hard-wrapped at a fixed column and the break was written INTO the
    # string: "stoop- shouldered", "coast- to-coast". Spoken as-is that is two words with a
    # gap in the middle of one of them.
    joined = re.sub(r"(?<=[A-Za-z])- (?=[A-Za-z])", "-", out)
    if joined != out:
        notes.append("rejoined a wrapped hyphen")
        out = joined

    # The bank's acronym. Spelled out letter by letter it is still correct, but line 306 is
    # "This little P.I.G.G.Y. plays the market" and that joke only exists if it is a word.
    if "P.I.G.G.Y." in out:
        out = out.replace("-P.I.G.G.Y.-", "Piggy").replace("P.I.G.G.Y.", "Piggy")
        notes.append("P.I.G.G.Y. -> Piggy")

    # Trademark marks are printed, not spoken.
    marked = out.replace("(R)", "").replace("(TM)", "")
    if marked != out:
        notes.append("dropped a (R)/(TM) mark")
        out = marked

    # espeak reads a run of dots literally slowly; a comma is the pause the line wants.
    if "..." in out:
        out = out.replace("...", ", ")
        notes.append("... -> pause")

    # A YEAR IS NOT A NUMBER. espeak reads "1987" as "nineteen HUNDRED eighty seven" -
    # eight syllables for a four-syllable word, and it is wrong English as well as long.
    # Measured on clip 610, whose only number this is: the line came out at 5.70 s, the
    # longest greeting in the burger shop by 0.68 s and 2.42 s past its own sync table.
    # Spelling the year out costs nothing and the subtitle the player READS is untouched -
    # this function has never changed a word, only how espeak is told to say it.
    #
    # 19xx AND 20xx ONLY, which is narrower than "a four-digit number" on purpose. The
    # first version matched 1000..2099 and caught the Bank's "fixed-rate CDs spin at 1500
    # RPM" (clip 305), turning "one thousand five hundred" into "fifteen hundred". That may
    # even be better English, but 305 is a clip that was generated, listened to and
    # installed, and a pronunciation rule written for one line is not a licence to redub
    # another. 1500 is a rev count, not a year, and the rule now says so.
    def _year(m):
        v = int(m.group())
        tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy",
                "eighty", "ninety"]
        ones = ["", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
                "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen",
                "seventeen", "eighteen", "nineteen"]
        def two(n):
            if n == 0:
                return "hundred"            # "nineteen hundred", which IS right for 1900
            if n < 20:
                return ("oh " + ones[n]) if n < 10 else ones[n]
            return (tens[n // 10] + " " + ones[n % 10]).strip()
        return "%s %s" % (ones[v // 100], two(v % 100))
    spelled = re.sub(r"\b(19|20)\d\d\b", _year, out)
    if spelled != out:
        notes.append("a year spelled out")
        out = spelled

    return " ".join(out.split()), notes


# --------------------------------------------------------- names the board has changed
# Reported, never rewritten. BUILDINGS.md's own table is the source: the four buildings
# whose board sign has been replaced. Its standing decision is that the spoken clips keep
# the old name, because a subtitle disagreeing with its own audio is worse than a stale
# name - this report exists so the cost of that decision is visible, not to overturn it.
#
# MONOLITH BURGERS IS THE ONE THAT WENT THE OTHER WAY, and it is not in this table because
# there is nothing left to report: Zain asked for the burger shop to become Bun Believable
# and the 22 lines that named it were REWRITTEN in subtitles.json, not renamed in place -
# "Monny the Burger Clown" and "it must be a Monolith" are jokes built on the old name and
# a find-and-replace would have left nonsense. Those 22 ids are then regenerated here so
# the voice says what the balloon says, which is the condition this table's own decision
# turns on. A rename only reaches this table while the AUDIO still disagrees with the text.
#
# The spoken forms differ from the board's names: the clips say "Hi-Tech U", never
# "Hi-Tech University". Both spellings are matched.
#   board  - whether the board sign has actually been changed yet (BUILDINGS.md's table)
#   pun    - words in the LINE that only work because of the old name. A line with one of
#            these cannot be renamed by swapping the name; it has to be rewritten, which
#            is a different and larger decision.
RENAMED = [
    dict(old="Hi-Tech U",       new="Open Door University",      board="done",
         pun=()),
    dict(old="ACNE Employment", new="Hire Ground",               board="done",
         pun=("blemish", "zits", "skills are", "rest.")),
    dict(old="the Factory",     new="The Industrial Revolution", board="done",
         pun=()),
    dict(old="Socket City",     new="Watt a Bargain",            board="in progress",
         pun=()),
]


def renames_report(subs):
    """Clips whose WORDS name a building the board no longer calls that."""
    hits = []
    for sid, line in subs.items():
        for r in RENAMED:
            # Case-SENSITIVE, and that is not fussiness. Clip 109 is a Socket City line
            # containing "direct from the factory to the jobber"; a case-insensitive match
            # files it under the Factory's rename and inflates the count by one.
            if r["old"] in line:
                pun = any(p.lower() in line.lower() for p in r["pun"])
                hits.append((int(sid), r, line, pun))
                break
    return sorted(hits, key=lambda h: h[0]), RENAMED


# ------------------------------------------------------------------------- wav writing
def _lowpass(x, cutoff_hz, fs, taps=193):
    fc = cutoff_hz / float(fs)
    n = np.arange(taps) - (taps - 1) / 2.0
    h = np.sinc(2 * fc * n) * np.hamming(taps)
    h /= h.sum()
    return np.convolve(x, h, mode="same")


def resample_to(x, src, dst):
    """Anti-aliased resample: low-pass below the new Nyquist, then interpolate."""
    x = _lowpass(x, 0.45 * dst, src)
    n_out = int(round(len(x) * dst / float(src)))
    return np.interp(np.linspace(0, len(x) - 1, n_out), np.arange(len(x), dtype=np.float64), x)


def trim(x, rate, thr=0.012, pad=0.06):
    idx = np.where(np.abs(x) > thr)[0]
    if len(idx) == 0:
        return x
    p = int(rate * pad)
    return x[max(0, idx[0] - p): min(len(x), idx[-1] + p)]


def write_wav8(path, x, rate):
    """11025 Hz, 8-bit UNSIGNED, mono - the format every clip in assets/audio/speech is in.

    That format is stated in WindowsAudioPlayer's own header comment; it is not measured
    off the clips, because this script does not open them.
    """
    d = np.clip(x, -1.0, 1.0)
    d = np.clip(np.round(d * 127.0) + 128, 0, 255).astype(np.uint8).tobytes()
    hdr = (b"RIFF" + struct.pack("<I", 36 + len(d)) + b"WAVEfmt " +
           struct.pack("<IHHIIHH", 16, 1, 1, rate, rate, 1, 8) +
           b"data" + struct.pack("<I", len(d)))
    with open(path, "wb") as f:
        f.write(hdr + d)


# ------------------------------------------------------------------------------ main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--show-text", action="store_true")
    ap.add_argument("--renames", action="store_true")
    ap.add_argument("--generate", action="store_true")
    ap.add_argument("--only", default="", help="comma-separated audio ids")
    args = ap.parse_args()

    subs = json.load(open(SUBS, encoding="utf-8"))

    ids = all_ids()
    if args.only:
        wanted = {int(s) for s in args.only.split(",") if s.strip()}
        ids = [i for i in ids if i in wanted]

    if args.renames:
        hits, rules = renames_report(subs)
        greet = set(all_ids())
        for r in rules:
            mine = [h for h in hits if h[1] is r]
            print("\n=== %s  ->  %s   (board sign: %s)   %d clips, %d of them greetings, "
                  "%d need REWRITING not renaming ===" %
                  (r["old"], r["new"], r["board"], len(mine),
                   sum(1 for h in mine if h[0] in greet), sum(1 for h in mine if h[3])))
            for sid, _, line, pun in mine:
                print("%4d %-6s %-8s %s" % (sid, "GREET" if sid in greet else "",
                                            "REWRITE" if pun else "", line))
        done = [h for h in hits if h[1]["board"] == "done"]
        print("\n%d of %d subtitled clips name a building the board no longer calls that." % (len(hits), len(subs)))
        print("%d of those are for signs ALREADY changed on the board; %d of the %d need the "
              "LINE rewritten, not just the name swapped." %
              (len(done), sum(1 for h in done if h[3]), len(done)))
        print("Nothing here has been changed. BUILDINGS.md's standing decision is that spoken "
              "clips keep the old name; this list is what that decision costs.")
        return 0

    if args.show_text:
        for i in ids:
            line = subs.get(str(i))
            if line is None:
                print("%4d  <NO SUBTITLE - skipped>" % i)
                continue
            b = band_for(i)
            spoken, notes = tts_input(line)
            flag = ("   [%s]" % "; ".join(sorted(set(notes)))) if notes else ""
            print("%4d  %-16s %-10s x%.2f  %s%s" % (i, b["loc"], b["voice"], b["speed"], spoken, flag))
        return 0

    if not args.generate:
        print(__doc__)
        return 0

    model = os.path.join(MODEL_DIR, "model.onnx")
    voices = os.path.join(MODEL_DIR, "voices-v1.0.bin")
    for p in (model, voices):
        if not os.path.exists(p):
            print("MISSING: %s\nSet JONES_KOKORO_DIR. Nothing is downloaded by this script." % p)
            return 2

    os.makedirs(OUT, exist_ok=True)
    from kokoro_onnx import Kokoro

    print("loading Kokoro (CPU)...")
    t0 = time.time()
    k = Kokoro(model, voices)
    print("loaded in %.1fs\n" % (time.time() - t0))

    manifest, total_audio, total_gen, done = [], 0.0, 0.0, 0
    for i in ids:
        line = subs.get(str(i))
        if line is None:
            print("%4d  skipped - no subtitle, so there are no words to speak" % i)
            continue
        b = band_for(i)
        spoken, notes = tts_input(line)

        t0 = time.time()
        # trim=False: the library's own trim returns an empty array when the model emits
        # NaN, which turns a broken model into a silent success.
        audio, sr = k.create(spoken, voice=b["voice"], speed=b["speed"], lang="en-us", trim=False)
        gen = time.time() - t0

        audio = np.asarray(audio, dtype=np.float64)
        if len(audio) == 0 or not np.isfinite(audio).all():
            raise RuntimeError("id %d: %d samples, finite=%s" % (i, len(audio), np.isfinite(audio).all()))

        audio = trim(audio, sr)
        peak = float(np.max(np.abs(audio))) or 1.0
        audio = audio * (0.89 / peak)      # 8 bits has 48 dB to work with; use it

        path = os.path.join(OUT, "%d.wav" % i)
        write_wav8(path, resample_to(audio, sr, GAME_RATE), GAME_RATE)

        dur = len(audio) / float(sr)
        total_audio += dur
        total_gen += gen
        done += 1
        print("%4d  %-16s %-10s %5.2fs  (%4.1fx rt)%s" %
              (i, b["loc"], b["voice"], dur, dur / gen if gen else 0,
               "  [%s]" % "; ".join(sorted(set(notes))) if notes else ""))
        manifest.append(dict(id=i, location=b["loc"], portrait=b["portrait"], voice=b["voice"],
                             speed=b["speed"], text=line, spoken=spoken, notes=sorted(set(notes)),
                             seconds=round(dur, 2), format="11025 Hz 8-bit unsigned mono PCM"))

    # --only MUST NOT TRUNCATE THE MANIFEST. It used to: the manifest was written from the
    # clips this run happened to make, so regenerating two ids left a two-clip manifest
    # describing a directory of 135 wavs, and the file that is supposed to say what every
    # clip is would have been the least reliable thing in the folder. Entries for ids this
    # run did not touch are carried over from the manifest already on disk.
    mp = os.path.join(OUT, "MANIFEST.json")
    if args.only and os.path.exists(mp):
        prev = json.load(open(mp, encoding="utf-8")).get("clips", [])
        fresh = {c["id"] for c in manifest}
        kept = [c for c in prev if c["id"] not in fresh]
        manifest = sorted(manifest + kept, key=lambda c: c["id"])
        print("manifest: %d regenerated, %d carried over from the previous run"
              % (len(fresh), len(kept)))

    json.dump(dict(engine="Kokoro-82M v1.0 ONNX fp32 (Apache-2.0)",
                   note="Generated from subtitles.json text only. No file in "
                        "assets/audio/speech was read, referenced or analysed.",
                   cast=[{kk: vv for kk, vv in b.items()} for b in VOICES],
                   clips=manifest),
              open(os.path.join(OUT, "MANIFEST.json"), "w", encoding="utf-8"),
              indent=2, ensure_ascii=False)

    print("\n%d clips | %.1fs audio | %.1fs generate | %.1fx realtime" %
          (done, total_audio, total_gen, total_audio / total_gen if total_gen else 0))
    print("written to %s" % OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
