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

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public class JonesShot {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

[void][JonesShot]::SetForegroundWindow($p.MainWindowHandle)
Start-Sleep -Seconds 4

$r = New-Object JonesShot+RECT
[void][JonesShot]::GetWindowRect($p.MainWindowHandle, [ref]$r)
$bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
"captured -> $Out"
