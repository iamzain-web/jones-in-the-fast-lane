"""Install walk cels into the game, keeping one generation of the previous set.

THIS EXISTS BECAUSE A REVERT WENT NINE DAYS BACK.

`assets/` is gitignored by design - it is the extracted game, not source - so the installed
cels are the ONLY copy of a build the moment they land. When a duplicate shoe turned up, the
build was rolled back to the oldest set anyone could still find, which was 30 September: no
sagittal arms, hands in pockets, a squashed swing shoe. The player had been happy with the
build that was overwritten, and said so. It was recovered only because the scratchpad
directory that produced it happened not to have been cleaned up.

A scratchpad is not an archive. One generation of backup, taken automatically before the
write, turns a revert into a step rather than a search.

Three things it does that a `copy` does not:

  GATE FIRST    check_install runs over the source before anything is overwritten, so a
                build that fails cannot replace one that passes. The gate has caught black
                slabs, green slabs, an eaten blazer, shredded feet and an eroded sprite; it
                earns its place by being run before the copy rather than after it.
  BACK UP       the cels about to be replaced are copied aside, one generation deep. More
                than one is a version-control system and this is not trying to be one.
  VERIFY        every installed file is compared to its source BY CONTENT. A timestamp says
                a copy happened; it does not say what is in the file. This project has twice
                had a step report success while shipping the previous artefact, and once had
                a PowerShell filter silently match nothing while reporting no error.

  python tools/install_walk.py --src4 <dir> --src12 <dir> --views 280,281,282
  python tools/install_walk.py --restore
"""
import argparse
import datetime
import hashlib
import os
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "assets")
BACKUP = os.path.join(ASSETS, ".install-backup")
SCALES = (4, 12)


def dest_dir(scale):
    return os.path.join(ASSETS, "png%dx-cast0" % scale)


def backup_dir(scale):
    return os.path.join(BACKUP, "png%dx-cast0" % scale)


def sha(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 16), b""):
            h.update(block)
    return h.hexdigest()


def cels_for(d, views):
    out = []
    if not os.path.isdir(d):
        return out
    for name in sorted(os.listdir(d)):
        if not name.endswith(".png") or not name.startswith("view_"):
            continue
        try:
            v = int(name.split("_")[1])
        except (IndexError, ValueError):
            continue
        if v in views:
            out.append(name)
    return out


def gate(src, views):
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import check_install
    return check_install.check(src, sorted(views))


# NOT tools/samples/ - `/tools/samples/*` is in .gitignore, so a waiver written there is
# untracked, and an untracked record of a deliberate exception is indistinguishable from
# the exception never having been decided. The point of this file is that it survives.
OVERRIDE_LOG = os.path.join(ROOT, "tools", "INSTALL_OVERRIDES.md")


def record_override(reason, failures, srcs):
    """Write the waiver down where it is tracked, not where it is gitignored.

    `assets/` is not in git, so a note left beside the installed cels disappears the
    moment anyone cleans up - and a record of a deliberate exception that cannot be found
    later is indistinguishable from the exception never having been decided."""
    lines = ["", "## %s" % datetime.datetime.now().strftime("%Y-%m-%d %H:%M"), "",
             "**Reason:** %s" % reason.strip(), "",
             "Sources: png4x `%s`, png12x `%s`" % (srcs[4], srcs[12]), "",
             "Gate failures waived:", ""]
    for scale, name, black, green, why in failures:
        lines.append("- `png%dx/%s` - %s" % (scale, name, why))
    lines.append("")
    os.makedirs(os.path.dirname(OVERRIDE_LOG), exist_ok=True)
    new = not os.path.exists(OVERRIDE_LOG)
    with open(OVERRIDE_LOG, "a", encoding="utf-8") as f:
        if new:
            f.write("# Installs that shipped against the gate\n\n"
                    "Every entry here is a build `check_install` rejected and a person "
                    "installed anyway, with the reason they gave. The gate was not "
                    "weakened for any of them - see the comment at the override in "
                    "`tools/install_walk.py` for why that distinction is the whole point.\n")
        f.write("\n".join(lines))
    print("  override recorded in %s" % os.path.relpath(OVERRIDE_LOG, ROOT))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src4")
    ap.add_argument("--src12")
    ap.add_argument("--views", default="280,281,282")
    ap.add_argument("--restore", action="store_true")
    ap.add_argument("--no-gate", action="store_true",
                    help="skip the acceptance gate. There is no good reason to use this on "
                         "a build going in front of a player.")
    ap.add_argument("--override", metavar="REASON",
                    help="install even though the gate fails, recording REASON in "
                         "tools/INSTALL_OVERRIDES.md. The gate still RUNS and still "
                         "reports; only the decision is overridden.")
    a = ap.parse_args()
    views = {int(v) for v in a.views.split(",")}

    if a.restore:
        n = 0
        for scale in SCALES:
            b, d = backup_dir(scale), dest_dir(scale)
            if not os.path.isdir(b):
                print("  no backup for png%dx - nothing to restore" % scale)
                continue
            for name in cels_for(b, views):
                shutil.copy2(os.path.join(b, name), os.path.join(d, name))
                n += 1
            print("  png%dx restored %d cels" % (scale, len(cels_for(b, views))))
        print("restored %d file(s) from the previous generation" % n)
        return 0

    srcs = {4: a.src4, 12: a.src12}
    if not srcs[4] or not srcs[12]:
        raise SystemExit("both --src4 and --src12 are required: a half-installed set shows "
                         "new art at one scale and old at the other, because the ladder "
                         "picks by factor.")

    # GATE BEFORE ANYTHING IS OVERWRITTEN.
    #
    # OVERRIDE THE DECISION, NEVER THE INSTRUMENT.
    #
    # There is a real case for shipping a build the gate rejects: when the defect it
    # catches is smaller than the defect the build fixes. 282 shipped that way once - a
    # sheared sole on the raised foot, traded for a whole second shoe on the same leg,
    # which is what the player actually complained about.
    #
    # The wrong way to do that is to move a threshold until the build passes. A ceiling
    # raised to admit a known defect stops catching it everywhere else too: FOOT_FRAG_MAX
    # loosened for 282's sheared sole would also have stopped reporting the fragment on
    # 280, which nobody had decided to accept. That is how a gate dies - not by being
    # switched off, but by being quietly tuned until it only ever agrees.
    #
    # So --override does not touch check_install. The gate runs in full, prints every
    # failing cel and its reason, and the override is written down with WHO decided and
    # WHY, next to the thing it waived.
    failures = []
    if not a.no_gate:
        for scale in SCALES:
            print("gate png%dx:" % scale)
            bad = gate(srcs[scale], views)
            failures.extend((scale,) + tuple(b) for b in bad)
        if failures and not a.override:
            print("\nGATE FAILED - nothing was written, nothing was backed up.")
            print("If this build is meant to ship anyway, say why: --override \"<reason>\"")
            return 1
        if failures:
            print("\nGATE FAILED on %d cel(s) and is being OVERRIDDEN." % len(failures))
            record_override(a.override, failures, srcs)
        elif a.override:
            print("\n--override given but the gate passed; nothing to override.")

    # BACK UP THE SET ABOUT TO BE REPLACED.
    for scale in SCALES:
        b, d = backup_dir(scale), dest_dir(scale)
        os.makedirs(b, exist_ok=True)
        keep = cels_for(d, views)
        for name in keep:
            shutil.copy2(os.path.join(d, name), os.path.join(b, name))
        print("backed up %d cel(s) of png%dx to %s"
              % (len(keep), scale, os.path.relpath(b, ROOT)))

    # COPY, THEN VERIFY BY CONTENT.
    bad, total = [], 0
    for scale in SCALES:
        s, d = srcs[scale], dest_dir(scale)
        names = cels_for(s, views)
        if not names:
            raise SystemExit("no view_*.png for views %s in %s" % (sorted(views), s))
        for name in names:
            shutil.copy2(os.path.join(s, name), os.path.join(d, name))
            total += 1
        for name in names:
            if sha(os.path.join(s, name)) != sha(os.path.join(d, name)):
                bad.append("png%dx/%s" % (scale, name))
        print("installed %d cel(s) into png%dx-cast0" % (len(names), scale))

    print()
    if bad:
        print("CONTENT MISMATCH on %d file(s): %s" % (len(bad), bad[:5]))
        print("The copy reported success. The files are not the same. Do not trust the run.")
        return 1
    print("%d file(s) installed and verified BY CONTENT against the source." % total)
    print("Previous generation is in %s - `--restore` puts it back."
          % os.path.relpath(BACKUP, ROOT))
    return 0


if __name__ == "__main__":
    sys.exit(main())