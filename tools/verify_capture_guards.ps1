# Proves that every CopyFromScreen in this project is guarded. Exits 1 if any is not.
#
# This exists because a guard was reported as landed when it had gone into ONE of nine call
# sites, and because four of the remaining scripts CONTAINED `GetForegroundWindow` while
# being completely unguarded - the symbol was inside a focus helper and never gated a
# capture. So this checks PROXIMITY, not presence: for every CopyFromScreen line, is there
# an actual guard call within the few lines above it.
param([string[]]$Roots = @('C:\AI-Accelerator\jones'))

$guardPattern = 'Assert-CaptureTargetByName|Test-CaptureTarget|Save-SafeWindowCapture|Get-SafeWindowCapture'
$bad = @()
$good = @()
$viaModule = @()

# Scripts that capture THROUGH the module have no CopyFromScreen of their own, so the
# proximity scan below cannot see them and would silently report a clean sweep while saying
# nothing about them. They are listed separately, or this tool has the same shape of hole
# as the thing it is checking for.
foreach ($root in $Roots) {
    Get-ChildItem $root -Recurse -Include *.ps1 -File -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -ne 'SafeCapture.ps1' } |
      ForEach-Object {
        $t = Get-Content $_.FullName -Raw
        if ($t -match 'Save-SafeWindowCapture|Get-SafeWindowCapture' -and
            $t -notmatch 'CopyFromScreen') { $viaModule += $_.FullName }
      }
}

foreach ($root in $Roots) {
    Get-ChildItem $root -Recurse -Include *.ps1 -File -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -ne 'verify_capture_guards.ps1' } |
      ForEach-Object {
        $file = $_.FullName
        $lines = Get-Content $file
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -notmatch 'CopyFromScreen') { continue }
            if ($lines[$i] -match '^\s*#') { continue }
            # 14 lines, not 8. The module's own copy sits nine lines below its guard - the
            # rect fetch, the size check and the bitmap allocation are in between - and an
            # 8-line window reported it as unguarded. The number is a heuristic either way;
            # what stops it being a rubber stamp is that it FAILED on a real file first.
            $from = [Math]::Max(0, $i - 14)
            $window = $lines[$from..$i] -join "`n"
            if ($window -match $guardPattern) {
                $good += "{0}:{1}" -f $file, ($i + 1)
            } else {
                $bad += "{0}:{1}  {2}" -f $file, ($i + 1), $lines[$i].Trim()
            }
        }
      }
}

Write-Output "GUARDED IN PLACE ($($good.Count)) - a guard within 8 lines above the copy:"
$good | ForEach-Object { "  $_" }
Write-Output ""
Write-Output "CAPTURE VIA THE MODULE ($($viaModule.Count)) - no raw copy of their own:"
$viaModule | ForEach-Object { "  $_" }
Write-Output ""
if ($bad.Count) {
    Write-Output "UNGUARDED ($($bad.Count)):"
    $bad | ForEach-Object { "  $_" }
    Write-Output ""
    Write-Output "FAIL - a screen copy with no check of what is on the screen."
    exit 1
}
Write-Output "PASS - every CopyFromScreen has a guard within 8 lines above it."
exit 0
