# Rebuild, relaunch and screenshot the desktop head.
# Kept as a script because the capture needs a P/Invoke type that cannot be redefined
# in the same PowerShell session, and re-running it inline kept failing.
param([string]$Out = 'C:\AI-Accelerator\jones\assets\shot.png', [int]$WaitSeconds = 120)

Get-Process -Name 'Jones.App.Desktop' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Push-Location 'C:\AI-Accelerator\jones'
$build = dotnet build src\Jones.App\Jones.App.Desktop\Jones.App.Desktop.csproj --nologo -v q 2>&1
# Match real diagnostics: Roslyn writes "... : error CS1234: ..." while Avalonia's XAML
# compiler writes "... Avalonia error AVLN3000: ...". Both must be caught, and the
# "0 Error(s)" summary line must NOT be — it contains the word "error" too.
$errors = $build | Select-String -Pattern ': error |Avalonia error|error AVLN'
if ($errors) { $errors | Select-Object -First 10; Pop-Location; exit 1 }
'build ok'

Start-Process -FilePath 'dotnet' `
    -ArgumentList 'run --project src\Jones.App\Jones.App.Desktop\Jones.App.Desktop.csproj --nologo' `
    -WorkingDirectory 'C:\AI-Accelerator\jones'
Pop-Location

$deadline = (Get-Date).AddSeconds($WaitSeconds)
$p = $null
while ((Get-Date) -lt $deadline) {
    $p = Get-Process -Name 'Jones.App.Desktop' -ErrorAction SilentlyContinue |
         Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($p) { break }
    Start-Sleep -Milliseconds 1500
}
if (-not $p) { 'no window appeared'; exit 1 }

# The capture goes through the shared module, which RENDERS THE WINDOW rather than reading
# the screen - `PrintWindow` with PW_RENDERFULLCONTENT. This script used to call
# `SetForegroundWindow`, sleep four seconds and copy the screen rectangle on the ASSUMPTION
# that the game was underneath it; on one run it was not, and a capture written that way
# saved the user's WhatsApp window instead.
#
# The module's answer to that was an ownership check and a foreground check, and in October
# 2026 a capture passed both and still came back containing another application - foreground
# is not the same as unobscured. So the API changed: nothing here reads the screen any more,
# the window no longer has to be in front, and THIS SCRIPT NO LONGER STEALS FOCUS. See the
# header of SafeCapture.ps1 for all three findings.
. "$PSScriptRoot\SafeCapture.ps1"

if (-not (Save-SafeWindowCapture -Process $p -Out $Out -SettleMs 4000)) { exit 1 }
