# Shared capture for every screenshot this project takes. Dot-source it:
#
#     . "$PSScriptRoot\SafeCapture.ps1"
#     $bmp = Get-SafeWindowCapture -Process $p          # $null if it refused
#
# WHY THIS EXISTS, AND WHY IT WAS REWRITTEN
# -----------------------------------------
# FIRST INCIDENT. Every capture script here called `SetForegroundWindow`, slept, and then
# used `Graphics.CopyFromScreen` on the game's rectangle on the ASSUMPTION that the game was
# underneath it. On one run it was not, and the capture wrote THE USER'S WHATSAPP WINDOW to
# disk - their private conversations, saved for a screenshot of a video game. This module was
# written in response, and it checked two things before and after every copy: OWNERSHIP, that
# the handle belongs to the process we expect, and FOREGROUND, that the same handle is the
# foreground window.
#
# SECOND INCIDENT, and the reason none of that is here any more. On 5 Oct 2026 a capture
# passed BOTH checks, before and after, and still came back containing another application's
# window across the lower two thirds of the frame. The module's own header reasoned at length
# about ownership and foreground and never once considered OCCLUSION.
#
#   FOREGROUND IS NOT THE SAME AS UNOBSCURED.
#
# A window can be the foreground window and still have something drawn over it: a notification
# toast, a tooltip, an always-on-top panel, another app mid-focus-change, or simply a window
# that is higher in the z-order without being active. And because `CopyFromScreen` reads THE
# SCREEN, the checks can only ever run before and after the copy - never during it. There is
# no number of checks that makes reading the screen safe.
#
# SO THE API CHANGED, NOT THE GUARD. This module now uses `PrintWindow` with
# PW_RENDERFULLCONTENT, which asks the window to render ITSELF into a device context we own.
# It does not read the screen at all. An obscuring window is not merely unlikely to appear in
# the frame - it CANNOT appear, because the frame is never taken from the screen.
#
# WHAT THAT CHANGES FOR CALLERS
# -----------------------------
#   * The window no longer has to be in front, so this NO LONGER STEALS FOCUS. The old module
#     called SetForegroundWindow and slept; that is gone. Captures can run while the user is
#     working in another application.
#   * OWNERSHIP IS STILL CHECKED. It is the question `PrintWindow` cannot answer for us: is
#     this handle ours. A stale handle after a restart would otherwise render somebody else's
#     window perfectly faithfully.
#   * A MINIMISED window is still refused. It renders, but as its restored-size placeholder
#     or as nothing, and neither is the screenshot the caller asked for.
#   * A BLANK RESULT IS A REFUSAL, NOT A FILE. `PrintWindow` can return a black or empty
#     bitmap for a GPU-composited window - which an Avalonia window is. If that happens the
#     capture is refused and says so, rather than writing a black PNG that reads as "the app
#     drew nothing" or, worse, falling back to reading the screen.
#
# `CopyFromScreen` MUST NOT COME BACK. `verify_capture_guards.ps1` fails on any occurrence of
# it anywhere in the repository, guarded or not - the guarded form is the thing that failed.
#
# THIRD FINDING, AND IT IS THE WORST OF THE THREE: THE GUARDS NEVER FIRED.
# -----------------------------------------------------------------------
# Every diagnostic in this module used to be written with `Write-Output`, including the ones
# inside the guard itself:
#
#     function _no($msg) { Write-Output "  capture guard: $msg"; return $false }
#     ...
#     if (-not [SafeCaptureNative]::IsWindow($Handle)) { return (_no 'not a window') }
#
# In PowerShell a function returns EVERYTHING it writes to the pipeline. `_no` therefore
# returned the two-element array @('  capture guard: ...', $false) - and a non-empty array is
# TRUE. So `if (-not (Test-CaptureTarget ...))` was false whenever the guard had something to
# say, which is to say: WHENEVER IT FAILED. The ownership check, the minimised check and the
# re-check after the copy were all inert the moment they had a complaint, and
# `Save-SafeWindowCapture` returned @('REFUSED: ...', $false) so `if (-not (Save-...))` in
# `shot.ps1` could never exit 1 either.
#
# The module read as careful and was not wired up. Diagnostics now go to the HOST via
# `Write-Host`, so they never enter the pipeline, and `verify_capture_guards.ps1` asserts that
# a failing guard returns a plain $false rather than something merely falsey-looking.

if (-not ('SafeCapturePrint' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class SafeCapturePrint {
    // PW_RENDERFULLCONTENT (0x2) is what makes this work for a composited window: without
    // it PrintWindow asks for a WM_PRINT repaint, which a DirectComposition surface answers
    // with nothing. Windows 8.1 and later.
    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", SetLastError=true)]
    public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
}
Add-Type -AssemblyName System.Drawing

function Test-CaptureTarget {
    <#  Is this handle one we may render?

        OWNERSHIP ONLY, and deliberately. The old module also demanded that the window be the
        foreground one; that check existed to make reading the SCREEN safe and it did not
        work. `PrintWindow` renders the window itself, so what is in front of it is no longer
        any of our business - and not requiring focus means captures stop interrupting
        whoever is at the keyboard.

        Still refused: a dead handle, an invisible window, and a minimised one, because a
        minimised window does not render its contents. #>
    param(
        [Parameter(Mandatory = $true)][IntPtr]$Handle,
        [Parameter(Mandatory = $true)][int]$ExpectedPid,
        [switch]$Quiet
    )
    # Write-Host, NOT Write-Output. See the third finding in the header: a diagnostic written
    # to the pipeline becomes part of this function's return value, and @('msg', $false) is
    # true. That is how every guard in this module came to be inert.
    function _no($msg) {
        if (-not $Quiet) { Write-Host "  capture guard: $msg" }
        return $false
    }
    if (-not [SafeCapturePrint]::IsWindow($Handle))        { return (_no 'the handle is not a window') }
    if (-not [SafeCapturePrint]::IsWindowVisible($Handle)) { return (_no 'the window is not visible') }
    if ([SafeCapturePrint]::IsIconic($Handle))             { return (_no 'the window is MINIMISED - it will not render its contents') }

    $owner = 0
    [void][SafeCapturePrint]::GetWindowThreadProcessId($Handle, [ref]$owner)
    if ($owner -ne $ExpectedPid) {
        return (_no "the window belongs to pid $owner, expected $ExpectedPid (OWNERSHIP)")
    }
    return $true
}

function Assert-CaptureTargetByName {
    <#  The one-line ownership assertion for scripts that track a raw handle rather than a
        Process. Throws - deliberately - so a script that ignores return values still stops. #>
    param(
        [Parameter(Mandatory = $true)][IntPtr]$Handle,
        [string]$ProcessName = 'Jones.App.Desktop'
    )
    $pids = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
              ForEach-Object { $_.Id })
    if (-not $pids) { throw "capture guard: no '$ProcessName' process is running" }
    $owner = 0
    [void][SafeCapturePrint]::GetWindowThreadProcessId($Handle, [ref]$owner)
    if ($pids -notcontains $owner) {
        throw "capture guard: that window belongs to pid $owner, not to $ProcessName (OWNERSHIP)"
    }
}

function Test-CaptureIsBlank {
    <#  Did PrintWindow actually draw anything?

        A GPU-composited window can come back entirely one colour even when PrintWindow
        reports success. Sampling a coarse grid is enough to tell a rendered frame from an
        empty one - the game's own screen has a black letterbox AND a board in it, so two
        distinct colours in a 12x12 grid is a very low bar that only a blank frame fails. #>
    param([Parameter(Mandatory = $true)]$Bitmap)

    $colours = @{}
    for ($gx = 0; $gx -lt 12; $gx++) {
        for ($gy = 0; $gy -lt 12; $gy++) {
            $x = [int](($Bitmap.Width - 1) * $gx / 11)
            $y = [int](($Bitmap.Height - 1) * $gy / 11)
            $colours[$Bitmap.GetPixel($x, $y).ToArgb()] = $true
        }
    }
    return ($colours.Count -lt 2)
}

function Get-SafeWindowCapture {
    <#  Render $Process's main window into a bitmap, or return $null and say why.

        THE WINDOW IS NOT BROUGHT TO THE FRONT and does not need to be. Nothing on the screen
        can enter this frame, because the screen is never read. #>
    param(
        [Parameter(Mandatory = $true)]$Process,
        [int]$SettleMs = 2200
    )
    $Process.Refresh()
    $h = $Process.MainWindowHandle
    if ($h -eq 0) { Write-Host '  capture guard: the process has no main window'; return $null }

    # Still a settle, for the application's own sake rather than the compositor's: a window
    # that has not finished its first layout renders a half-built frame whichever API asks.
    Start-Sleep -Milliseconds $SettleMs

    if (-not (Test-CaptureTarget -Handle $h -ExpectedPid $Process.Id)) { return $null }

    # The rectangle is still validated. A window mid-restore reports a placeholder size of
    # about 160x28, and rendering that is a correctly-captured picture of nothing.
    $r = New-Object SafeCapturePrint+RECT
    $w = 0; $ht = 0
    $deadline = (Get-Date).AddMilliseconds(3000)
    while ((Get-Date) -lt $deadline) {
        [void][SafeCapturePrint]::GetWindowRect($h, [ref]$r)
        $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
        if ($w -ge 200 -and $ht -ge 150 -and -not [SafeCapturePrint]::IsIconic($h)) { break }
        Start-Sleep -Milliseconds 150
    }
    if ($w -lt 200 -or $ht -lt 150) {
        Write-Host ("  capture guard: the window rectangle is not plausible ({0}x{1}) - " +
                      "it is minimised or still restoring" -f $w, $ht)
        return $null
    }

    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    try {
        $ok = [SafeCapturePrint]::PrintWindow($h, $hdc, [SafeCapturePrint]::PW_RENDERFULLCONTENT)
    } finally {
        $g.ReleaseHdc($hdc)
        $g.Dispose()
    }

    if (-not $ok) {
        $bmp.Dispose()
        Write-Host '  capture guard: PrintWindow refused to render the window'
        return $null
    }

    # A BLANK FRAME IS A REFUSAL. PrintWindow can report success and hand back nothing for a
    # GPU-composited window; writing that out would be a black PNG that reads as "the app
    # drew nothing". There is deliberately NO fallback to CopyFromScreen - that is the API
    # this module exists to stay away from.
    if (Test-CaptureIsBlank -Bitmap $bmp) {
        $bmp.Dispose()
        Write-Host ('  capture guard: PrintWindow returned a blank frame - this window is ' +
                      'composited in a way it cannot render. NOT falling back to reading the ' +
                      'screen; a different mechanism is needed.')
        return $null
    }

    # The ownership check again, which is cheap and catches a process that died and had its
    # handle reused while we were rendering.
    if (-not (Test-CaptureTarget -Handle $h -ExpectedPid $Process.Id)) {
        $bmp.Dispose()
        Write-Host '  capture guard: the window stopped being ours during the render'
        return $null
    }

    return $bmp
}

function Save-SafeWindowCapture {
    <# Render and write to $Out. Returns $true on success, $false on refusal. #>
    param(
        [Parameter(Mandatory = $true)]$Process,
        [Parameter(Mandatory = $true)][string]$Out,
        [int]$SettleMs = 2200
    )
    $bmp = Get-SafeWindowCapture -Process $Process -SettleMs $SettleMs
    if ($null -eq $bmp) { Write-Host "REFUSED: no screenshot written to $Out"; return $false }
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "captured -> $Out"
    return $true
}
