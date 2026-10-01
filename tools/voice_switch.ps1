<#
.SYNOPSIS
    Switch the game's spoken clips between Sierra's originals and the Kokoro voices.

.DESCRIPTION
    `MainViewModel.Speak(audioId)` plays `assets\audio\speech\<audioId>.wav`. There is no
    switch inside the game and none is wanted: the clip the game plays is whatever file is
    sitting at that path, so swapping voices is a file copy and nothing has to be rebuilt.

    Three directories, and only the first one is ever read by the game:

        assets\audio\speech          what the game plays RIGHT NOW
        assets\audio\speech-sierra   every one of Sierra's 533 clips, untouched
        assets\audio\speech-kokoro   the generated clips, by `tools\gen_voice.py`

    `-To sierra` restores ONLY the ids that speech-kokoro holds, so it cannot disturb the
    400 clips that were never replaced. Nothing is ever deleted.

    None of these directories is in git - `.gitignore` excludes `/assets/` - so this is the
    whole of the backup. Do not delete speech-sierra.

.PARAMETER To
    kokoro | sierra

.PARAMETER Status
    Report, per clip, which voice is currently installed. Compares file hashes against both
    stores rather than trusting a marker file, because a marker goes stale silently.

.EXAMPLE
    powershell -File tools\voice_switch.ps1 -Status
    powershell -File tools\voice_switch.ps1 -To kokoro
    powershell -File tools\voice_switch.ps1 -To sierra
#>
[CmdletBinding()]
param(
    [ValidateSet('kokoro', 'sierra')]
    [string]$To,
    [switch]$Status
)

$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $PSScriptRoot
$live   = Join-Path $root 'assets\audio\speech'
$sierra = Join-Path $root 'assets\audio\speech-sierra'
$kokoro = Join-Path $root 'assets\audio\speech-kokoro'

if (-not (Test-Path $live)) { throw "No speech directory at $live" }

# The backup is the only copy of the originals. Refuse to do anything without it.
if (-not (Test-Path $sierra)) {
    throw "No original-clip backup at $sierra. Restore it before switching anything."
}

$sierraCount = (Get-ChildItem $sierra -Filter '*.wav' -ErrorAction SilentlyContinue).Count
if ($sierraCount -lt 533) {
    throw "Backup at $sierra holds $sierraCount clips, expected 533. Refusing to touch the live speech."
}

function Get-Hash($path) {
    if (-not (Test-Path $path)) { return $null }
    (Get-FileHash -Path $path -Algorithm SHA256).Hash
}

if ($Status -or -not $To) {
    if (-not (Test-Path $kokoro)) {
        Write-Output "No generated clips at $kokoro. Run tools\gen_voice.py --generate first."
        return
    }

    $new = @(); $old = @(); $other = @()
    foreach ($f in Get-ChildItem $kokoro -Filter '*.wav') {
        $id = [IO.Path]::GetFileNameWithoutExtension($f.Name)
        $h  = Get-Hash (Join-Path $live $f.Name)
        if     ($h -eq (Get-Hash $f.FullName))                     { $new   += $id }
        elseif ($h -eq (Get-Hash (Join-Path $sierra $f.Name)))     { $old   += $id }
        else                                                       { $other += $id }
    }

    $total = (Get-ChildItem $live -Filter '*.wav').Count
    Write-Output "live clips          : $total"
    Write-Output "generated available : $($new.Count + $old.Count + $other.Count)"
    Write-Output "  playing KOKORO    : $($new.Count)"
    Write-Output "  playing SIERRA    : $($old.Count)"
    if ($other.Count) {
        Write-Output "  MATCHING NEITHER  : $($other.Count)  -> $($other -join ',')"
    }
    Write-Output ""
    Write-Output "originals held in   : $sierra ($sierraCount clips)"
    return
}

if ($To -eq 'kokoro') {
    if (-not (Test-Path $kokoro)) { throw "No generated clips at $kokoro." }
    $files = Get-ChildItem $kokoro -Filter '*.wav'
    if ($files.Count -eq 0) { throw "No .wav files in $kokoro." }

    # Every clip about to be overwritten must exist in the backup FIRST. A partial backup
    # is how a restore quietly stops being possible.
    foreach ($f in $files) {
        if (-not (Test-Path (Join-Path $sierra $f.Name))) {
            throw "No original of $($f.Name) in the backup. Nothing copied."
        }
    }

    $files | ForEach-Object { Copy-Item $_.FullName -Destination (Join-Path $live $_.Name) -Force }
    Write-Output "Installed $($files.Count) Kokoro clips into $live"
    Write-Output "Switch back with:  powershell -File tools\voice_switch.ps1 -To sierra"
}
elseif ($To -eq 'sierra') {
    if (-not (Test-Path $kokoro)) { throw "No generated clips at $kokoro - nothing to undo." }

    # Restore only what was replaced. Copying all 533 back would be correct today and
    # wrong the moment another batch is generated from a different source.
    $names = (Get-ChildItem $kokoro -Filter '*.wav').Name
    $n = 0
    foreach ($name in $names) {
        $src = Join-Path $sierra $name
        if (Test-Path $src) { Copy-Item $src -Destination (Join-Path $live $name) -Force; $n++ }
    }
    Write-Output "Restored $n of Sierra's clips into $live"
}
