# DISABLED. THIS SCRIPT DRIVES SYNTHETIC MOUSE INPUT AND NO AGENT MAY RUN IT.
# ==========================================================================
#
# It moves the real cursor with `SetCursorPos` and presses the real left button with
# `mouse_event`, on the machine Zain is sitting at. Every brief written for this project
# carries the same constraint - **no synthetic mouse or keyboard input** - and a script whose
# entire purpose is to violate it is a loaded gun on the table. It refuses to run unless a
# HUMAN passes -IAmAHumanAtThisKeyboard, which no agent may pass on anyone's behalf.
#
# IT IS NOT DELETED, deliberately. The refusal is more informative than an absent file: it
# tells the next person what this was for, why it stopped, and what to use instead. The
# coordinate maths below is also the only worked example in the repository of converting the
# game's 320x200 space to screen pixels through the Viewbox, which is worth keeping.
#
# IT IS ALSO THE LAST THING IN THE REPOSITORY THAT MOVES THE USER'S FOCUS. `SafeCapture.ps1`
# stopped calling `SetForegroundWindow` when it moved to `PrintWindow` - captures now render
# the window without bringing it forward - so the `SetForegroundWindow` on line 40 below is
# the only remaining call that takes the screen away from whoever is using it. That is here
# to deliver clicks, not to capture, and it goes when this script does.
#
# WHAT TO USE INSTEAD. To see a screen without driving the game: `JONES_DEBUG_LOCATION=<id>`
# opens a building's interior and `JONES_DEBUG_BOARD=1` the town board, both at startup with
# no input at all, and `tools/shot.ps1` captures. To check that a control responds where it is
# drawn: `JONES_DEBUG_HITTEST=1` asks the visual tree directly, again with no mouse.
param(
    [string]$OutDir = 'C:\AI-Accelerator\jones\assets',
    [int]$WaitSeconds = 120,

    # The only way past the refusal. A human types this; an agent does not.
    [switch]$IAmAHumanAtThisKeyboard
)

if (-not $IAmAHumanAtThisKeyboard) {
    Write-Host ''
    Write-Host 'REFUSED: clickthrough.ps1 drives SYNTHETIC MOUSE INPUT.' -ForegroundColor Red
    Write-Host ''
    Write-Host '  It calls SetCursorPos and mouse_event, so it takes over the real pointer on'
    Write-Host '  the machine someone is sitting at. No agent on this project may run it:'
    Write-Host '  "no synthetic mouse or keyboard input" is in every brief, because Zain is at'
    Write-Host '  the keyboard.'
    Write-Host ''
    Write-Host '  To see a screen without driving the game:'
    Write-Host '    JONES_DEBUG_LOCATION=<LocationId>  opens a building interior at startup'
    Write-Host '    JONES_DEBUG_BOARD=1                opens the town board at startup'
    Write-Host '    tools\shot.ps1                     captures, without stealing focus'
    Write-Host '    JONES_DEBUG_HITTEST=1              checks controls respond where they draw'
    Write-Host ''
    Write-Host '  A human who genuinely wants this may re-run with -IAmAHumanAtThisKeyboard.'
    Write-Host ''
    exit 2
}

Write-Host 'clickthrough.ps1: taking the pointer. Do not touch the mouse until it finishes.' -ForegroundColor Yellow

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public class JonesDrive {
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}
"@

$LEFTDOWN = 0x0002
$LEFTUP = 0x0004

$deadline = (Get-Date).AddSeconds($WaitSeconds)
$p = $null
while ((Get-Date) -lt $deadline) {
    $p = Get-Process -Name 'Jones.App.Desktop' -ErrorAction SilentlyContinue |
         Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($p) { break }
    Start-Sleep -Milliseconds 1000
}
if (-not $p) { 'no window'; exit 1 }

$h = $p.MainWindowHandle
[void][JonesDrive]::SetForegroundWindow($h)
Start-Sleep -Seconds 3

# Client area, and where the 320x200 canvas sits inside it after uniform scaling.
$cr = New-Object JonesDrive+RECT
[void][JonesDrive]::GetClientRect($h, [ref]$cr)
$origin = New-Object JonesDrive+POINT
[void][JonesDrive]::ClientToScreen($h, [ref]$origin)

$cw = $cr.Right - $cr.Left
$ch = $cr.Bottom - $cr.Top
$scale = [Math]::Min($cw / 320.0, $ch / 200.0)
$offX = $origin.X + ($cw - 320 * $scale) / 2
$offY = $origin.Y + ($ch - 200 * $scale) / 2
"client ${cw}x${ch}  scale $([Math]::Round($scale,2))  origin $($origin.X),$($origin.Y)"

function Click([int]$gx, [int]$gy) {
    $sx = [int]($offX + $gx * $scale)
    $sy = [int]($offY + $gy * $scale)
    [void][JonesDrive]::SetCursorPos($sx, $sy)
    Start-Sleep -Milliseconds 250
    [JonesDrive]::mouse_event($LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [JonesDrive]::mouse_event($LEFTUP, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 700
}

# The capture goes through the shared module, which RENDERS the window with PrintWindow and
# never reads the screen. It used to copy the screen rectangle; a capture written that way
# once saved the user's WhatsApp window instead of the game, and the ownership-plus-foreground
# guard added in response later failed too, because foreground is not the same as unobscured.
# See the header of tools/SafeCapture.ps1 for all three findings.
. "$PSScriptRoot\SafeCapture.ps1"

function Shot([string]$name) {
    $path = Join-Path $OutDir "flow_$name.png"
    # The boolean is now trustworthy: the module's diagnostics go to the host rather than
    # into its own return value, which is what used to make every refusal read as success.
    if (Save-SafeWindowCapture -Process $p -Out $path -SettleMs 300) { "  shot -> $path" }
    else { "  shot REFUSED for $name - see the guard message above" }
}

Shot '1_menu'

# select1: PLAY GAME is at dialog (69,44) + nsLeft 27, nsTop 17, and the cel is 133x20.
Click (69 + 27 + 60) (44 + 17 + 10)
Shot '2_playercount'

# select1b: "1 player" button at nsLeft 20, nsTop 55, 32x9.
Click (69 + 20 + 16) (44 + 55 + 4)
Shot '3_character'

# select2: first character's select button at nsLeft 6, nsTop 108.
Click (69 + 6 + 16) (44 + 108 + 4)
Shot '4_goals'

# select3: the exit/PLAY button at nsLeft 143, nsTop 108.
Click (69 + 143 + 16) (44 + 108 + 4)
Shot '5_board'

'done'
