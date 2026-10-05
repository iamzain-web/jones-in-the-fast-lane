# Proves that NOTHING in this project reads the screen. Exits 1 if anything does.
#
# WHAT CHANGED, AND WHY THIS IS NOW A BAN RATHER THAN A PROXIMITY CHECK
# ---------------------------------------------------------------------
# This script used to check that every `CopyFromScreen` had a guard within a few lines above
# it, because the first incident - a capture that wrote the user's WhatsApp window to disk -
# was caused by unguarded copies. It reported PASS for months, correctly, against the rule it
# was given.
#
# On 5 Oct 2026 a capture that was guarded, in place, by both the ownership check and the
# foreground check, before AND after the copy, came back containing another application's
# window. `CopyFromScreen` copies THE SCREEN; a window can be the foreground window and still
# have a toast, a tooltip, an always-on-top panel or a higher-z-order window drawn over it,
# and no check that runs before and after the copy can see what happened during it.
#
# So the rule is no longer "is it guarded". The rule is that THE SCREEN IS NEVER READ.
# `SafeCapture.ps1` renders windows with `PrintWindow` + PW_RENDERFULLCONTENT instead, which
# asks the window to draw itself into a device context we own and never touches the screen.
#
# A proximity check would now be worse than nothing: it would go on passing a call that is
# guarded exactly the way the one that failed was guarded.
param([string[]]$Roots = @('C:\AI-Accelerator\jones'))

$banned = @()
$viaModule = @()
$rendering = @()

foreach ($root in $Roots) {
    Get-ChildItem $root -Recurse -Include *.ps1, *.cs, *.py -File -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|\\\.git\\' } |
      Where-Object { $_.Name -ne 'verify_capture_guards.ps1' } |
      ForEach-Object {
        $file = $_.FullName
        # @(...) IS LOad-BEARING. Get-Content on a ONE-LINE file returns a bare string, and
        # indexing a string gives CHARACTERS - so the loop below scanned such a file one
        # letter at a time and matched nothing. Found by planting a one-line probe file and
        # watching this script pass; a checker that cannot fail is the thing it exists to
        # prevent.
        $lines = @(Get-Content $file -ErrorAction SilentlyContinue)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            # A comment may NAME the banned API - this file and SafeCapture's header both do,
            # at length, and that record is the point. Only live code counts.
            if ($line -match '^\s*(#|//|///)') { continue }
            if ($line -match 'CopyFromScreen') {
                $banned += "{0}:{1}  {2}" -f $file, ($i + 1), $line.Trim()
            }
        }

        $t = ($lines -join "`n")
        if ($t -match 'Save-SafeWindowCapture|Get-SafeWindowCapture') { $viaModule += $file }
        if ($t -match 'PrintWindow\(') { $rendering += $file }
      }
}

Write-Output "RENDERS WINDOWS ($($rendering.Count)) - PrintWindow, which never reads the screen:"
$rendering | ForEach-Object { "  $_" }
Write-Output ""
Write-Output "CAPTURE VIA THE MODULE ($($viaModule.Count)):"
$viaModule | ForEach-Object { "  $_" }
Write-Output ""

if ($banned.Count) {
    Write-Output "SCREEN READS ($($banned.Count)) - BANNED:"
    $banned | ForEach-Object { "  $_" }
    Write-Output ""
    Write-Output ("FAIL - CopyFromScreen reads the screen, so it can capture any window that " +
                  "happens to be over ours. Guarding it is not enough; that is what failed. " +
                  "Use Get-SafeWindowCapture, which renders the window with PrintWindow.")
    exit 1
}

# The module must still BE the thing that captures. A clean sweep that found no screen reads
# because nothing captures at all would be a false pass of exactly the shape this tool has
# twice been caught by.
if ($rendering.Count -eq 0) {
    Write-Output "FAIL - nothing in the repository renders a window. SafeCapture.ps1 is missing or gutted."
    exit 1
}

# ---------------------------------------------------------------------------
# AND THE GUARD MUST ACTUALLY RETURN FALSE WHEN IT FAILS.
#
# Every diagnostic in the module was once written with `Write-Output`, which in PowerShell
# becomes part of a function's RETURN VALUE: a failing guard returned @('message', $false),
# a non-empty array, which is TRUE. So `if (-not (Test-CaptureTarget ...))` never fired, and
# every check in a module written after a privacy incident was inert the moment it had
# something to say. Nothing about reading the code showed it; only running it does.
# ---------------------------------------------------------------------------
. "$PSScriptRoot\SafeCapture.ps1"

# NOT -Quiet, and that is the whole point. The leak only happens on the path that WRITES a
# diagnostic, so a quiet call returns a clean $false whether the module is fixed or not - the
# first version of this self-test passed -Quiet and duly passed against a deliberately broken
# module. The message below is the guard talking during its own test.
Write-Host "GUARD SELF-TEST - calling Test-CaptureTarget with a null handle; it should complain:"
$result = Test-CaptureTarget -Handle ([IntPtr]::Zero) -ExpectedPid 999999

if ($result -isnot [bool]) {
    # NOTE the parentheses: `-f` binds tighter than `+`, so formatting a concatenation
    # without them substitutes into the LAST fragment only and prints {0} literally. That is
    # exactly what the first version of this message did.
    Write-Output (("FAIL - a failing guard returned [{0}] with {1} element(s), not a boolean. " +
                   "A diagnostic has leaked into the pipeline and the guard is inert: " +
                   "`if (-not (Test-CaptureTarget ...))` will never fire.") `
                  -f $result.GetType().Name, @($result).Count)
    exit 1
}
if ($result -ne $false) {
    Write-Output "FAIL - the guard returned $result for a null handle and a pid that does not exist."
    exit 1
}

Write-Output "GUARD SELF-TEST - a failing check returns a plain `$false. OK"
Write-Output ""
Write-Output "PASS - nothing reads the screen; captures go through PrintWindow."
exit 0
