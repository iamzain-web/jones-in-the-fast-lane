# Shared guard for every screenshot this project takes. Dot-source it:
#
#     . "$PSScriptRoot\SafeCapture.ps1"
#     $bmp = Get-SafeWindowCapture -Process $p          # $null if it refused
#
# WHY THIS EXISTS
# ---------------
# `Graphics.CopyFromScreen` does exactly what its name says: it copies the pixels on the
# SCREEN inside a rectangle. It has no idea which window owns them. Every capture script
# here called `SetForegroundWindow`, slept, and then copied the game's rectangle on the
# assumption that the game was underneath it.
#
# On one run it was not. The game had not come forward in time and the capture wrote THE
# USER'S WHATSAPP WINDOW to disk - their private conversations, saved for a screenshot of a
# video game. The file was deleted unread. Nothing in any of the scripts could have
# prevented it, because none of them checked.
#
# WHAT IS CHECKED, AND WHY BOTH HALVES ARE NEEDED
# -----------------------------------------------
#   OWNERSHIP   `GetWindowThreadProcessId` on the handle we are about to measure must
#               return the PID we expect. This is the strong guarantee: it says the
#               rectangle belongs to our game, not to something that inherited the handle
#               or to a stale handle after a restart.
#   FOREGROUND  the same handle must be the foreground window. Ownership alone does NOT
#               make the capture safe, because CopyFromScreen reads the screen: another
#               application sitting ON TOP of our window would be copied instead. This is
#               the half that the WhatsApp incident actually needed.
#
# AND IT IS CHECKED TWICE - before the grab and again immediately after, with the bitmap
# discarded if anything changed in between. A dialog, a notification toast or a focus steal
# landing between the check and the copy would otherwise defeat a single check, which is
# the specific weakness of guarding on foreground at all.
#
# A refusal returns $null. Callers MUST treat that as "no screenshot", never as success.

if (-not ('SafeCaptureNative' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class SafeCaptureNative {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    // Used only by the guard's own self-test, to put a window into the state that defeats
    // a foreground-only check.
    public static bool ShowWindowMinimized(IntPtr h) { return ShowWindow(h, 6); }
    [DllImport("user32.dll", SetLastError=true)]
    public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
}
Add-Type -AssemblyName System.Drawing

function Test-CaptureTarget {
    <#  Is it safe, right now, to copy the screen where $Handle is?
        Returns $true only if the window exists, is visible, is owned by $ExpectedPid,
        and is the foreground window. Writes the reason when it is not. #>
    param(
        [Parameter(Mandatory = $true)][IntPtr]$Handle,
        [Parameter(Mandatory = $true)][int]$ExpectedPid,
        [switch]$Quiet
    )
    function _no($msg) {
        if (-not $Quiet) { Write-Output "  capture guard: $msg" }
        return $false
    }
    if (-not [SafeCaptureNative]::IsWindow($Handle))        { return (_no 'the handle is not a window') }
    if (-not [SafeCaptureNative]::IsWindowVisible($Handle)) { return (_no 'the window is not visible') }
    # IsWindowVisible is TRUE for a MINIMISED window, and a minimised window's rect is
    # off-screen (around -32000,-32000). Copying that rectangle reads whatever the desktop
    # has there, which is exactly the failure this module exists to stop - and it would
    # pass both the ownership and the foreground test, because a minimised window can be
    # the foreground window. Found by testing, not by reading.
    if ([SafeCaptureNative]::IsIconic($Handle)) { return (_no 'the window is MINIMISED - its rectangle is off-screen') }

    $owner = 0
    [void][SafeCaptureNative]::GetWindowThreadProcessId($Handle, [ref]$owner)
    if ($owner -ne $ExpectedPid) {
        return (_no "the window belongs to pid $owner, expected $ExpectedPid (OWNERSHIP)")
    }
    $fg = [SafeCaptureNative]::GetForegroundWindow()
    if ($fg -ne $Handle) {
        $fgPid = 0
        [void][SafeCaptureNative]::GetWindowThreadProcessId($fg, [ref]$fgPid)
        $who = try { (Get-Process -Id $fgPid -ErrorAction Stop).ProcessName } catch { "pid $fgPid" }
        return (_no "'$who' is in front of the target - refusing to capture another application (FOREGROUND)")
    }
    return $true
}

function Assert-CaptureTargetByName {
    <#  The one-line guard for scripts that track a raw handle rather than a Process.
        Throws - deliberately - so a script that ignores return values still stops.

        Written because four scripts in this project already CONTAINED
        `GetForegroundWindow` and were still unsafe: in every one of them it sat inside a
        "focus the window if it is not already focused" helper and never gated the capture
        at all. Grepping for the symbol says nothing about whether anything is guarded. #>
    param(
        [Parameter(Mandatory = $true)][IntPtr]$Handle,
        [string]$ProcessName = 'Jones.App.Desktop'
    )
    $pids = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
              ForEach-Object { $_.Id })
    if (-not $pids) { throw "capture guard: no '$ProcessName' process is running" }
    $owner = 0
    [void][SafeCaptureNative]::GetWindowThreadProcessId($Handle, [ref]$owner)
    if ($pids -notcontains $owner) {
        throw "capture guard: that window belongs to pid $owner, not to $ProcessName (OWNERSHIP)"
    }
    $fg = [SafeCaptureNative]::GetForegroundWindow()
    if ($fg -ne $Handle) {
        $fgPid = 0
        [void][SafeCaptureNative]::GetWindowThreadProcessId($fg, [ref]$fgPid)
        $who = try { (Get-Process -Id $fgPid -ErrorAction Stop).ProcessName } catch { "pid $fgPid" }
        throw "capture guard: '$who' is in front of the target - refusing to capture another application (FOREGROUND)"
    }
}

function Get-SafeWindowCapture {
    <#  Capture $Process's main window, or return $null and say why.
        The guard runs BEFORE and AFTER the pixel copy; if anything changed in between the
        bitmap is discarded, because it may contain another application's pixels. #>
    param(
        [Parameter(Mandatory = $true)]$Process,
        [int]$SettleMs = 2200
    )
    $Process.Refresh()
    $h = $Process.MainWindowHandle
    if ($h -eq 0) { Write-Output '  capture guard: the process has no main window'; return $null }

    [void][SafeCaptureNative]::SetForegroundWindow($h)
    Start-Sleep -Milliseconds $SettleMs

    if (-not (Test-CaptureTarget -Handle $h -ExpectedPid $Process.Id)) { return $null }

    # THE RECTANGLE ITSELF IS VALIDATED, not just the window's flags.
    #
    # Found by the self-test: raising a minimised window clears IsIconic BEFORE its
    # geometry is restored, so for a moment the window is owned, foreground and not
    # iconic while GetWindowRect still returns the minimised placeholder - about 160x28,
    # parked off-screen. The guard passed and a 160x28 image of somebody else's desktop
    # was written. Every flag said yes and the rectangle said no.
    #
    # So the rect is polled until it is plausible, and refused if it never becomes so.
    $r = New-Object SafeCaptureNative+RECT
    $w = 0; $ht = 0
    $deadline = (Get-Date).AddMilliseconds(3000)
    while ((Get-Date) -lt $deadline) {
        [void][SafeCaptureNative]::GetWindowRect($h, [ref]$r)
        $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
        $onScreen = ($r.Left -gt -10000) -and ($r.Top -gt -10000)
        if ($w -ge 200 -and $ht -ge 150 -and $onScreen -and
            -not [SafeCaptureNative]::IsIconic($h)) { break }
        Start-Sleep -Milliseconds 150
    }
    $onScreen = ($r.Left -gt -10000) -and ($r.Top -gt -10000)
    if ($w -lt 200 -or $ht -lt 150 -or -not $onScreen) {
        Write-Output ("  capture guard: the window rectangle is not plausible " +
                      "({0}x{1} at {2},{3}) - it is minimised or still restoring" `
                      -f $w, $ht, $r.Left, $r.Top)
        return $null
    }

    # FINAL CHECK, immediately above the copy. The first check is thirty lines up, with the
    # rect polling in between, and `verify_capture_guards.ps1` rightly reported the copy as
    # unguarded at that distance. The honest fix is to assert here rather than to widen the
    # checker's window until it stops meaning anything - and re-checking after a wait that
    # can last three seconds is worth doing on its own merits.
    if (-not (Test-CaptureTarget -Handle $h -ExpectedPid $Process.Id)) { return $null }

    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $ht))
    $g.Dispose()

    # SECOND CHECK. If focus moved while the pixels were being copied, those pixels may
    # belong to whatever took it, so the bitmap is destroyed rather than returned.
    if (-not (Test-CaptureTarget -Handle $h -ExpectedPid $Process.Id)) {
        $bmp.Dispose()
        Write-Output '  capture guard: focus changed DURING the copy - bitmap discarded'
        return $null
    }
    return $bmp
}

function Save-SafeWindowCapture {
    <# Capture and write to $Out. Returns $true on success, $false on refusal. #>
    param(
        [Parameter(Mandatory = $true)]$Process,
        [Parameter(Mandatory = $true)][string]$Out,
        [int]$SettleMs = 2200
    )
    $bmp = Get-SafeWindowCapture -Process $Process -SettleMs $SettleMs
    if ($null -eq $bmp) { Write-Output "REFUSED: no screenshot written to $Out"; return $false }
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output "captured -> $Out"
    return $true
}
