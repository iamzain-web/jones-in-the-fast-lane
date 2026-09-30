# Drives the running app through the new-game flow and screenshots each screen, so the
# ported sequence can be checked against the original without manual clicking.
#
# Coordinates are given in the game's own 320x200 space and converted to screen pixels
# using the Viewbox scale, which is what keeps this stable across window sizes.
param(
    [string]$OutDir = 'C:\AI-Accelerator\jones\assets',
    [int]$WaitSeconds = 120
)

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

# The capture goes through the shared guard, which verifies the window is OWNED by this
# process and is genuinely in front, before and after the pixel copy. This function used to
# copy the screen rectangle unconditionally; a capture written that way once saved the
# user's WhatsApp window instead of the game. See tools/SafeCapture.ps1.
. "$PSScriptRoot\SafeCapture.ps1"

function Shot([string]$name) {
    $path = Join-Path $OutDir "flow_$name.png"
    if (Save-SafeWindowCapture -Process $p -Out $path -SettleMs 300) { "  shot -> $path" }
    else { "  shot REFUSED for $name - the game was not the window on screen" }
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
