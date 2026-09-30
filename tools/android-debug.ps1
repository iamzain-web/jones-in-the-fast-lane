<#
.SYNOPSIS
    Installs the Jones APK on an attached phone, launches it, and captures the log that
    explains what happened.

.DESCRIPTION
    THE POINT OF THIS SCRIPT IS THE LOG, NOT THE INSTALL.

    An Avalonia Android app that fails during startup dies inside a process Android has
    already torn down. Nothing appears on screen, nothing is written anywhere reachable from
    the phone, and the person holding it can only report "it doesn't open" - which is not
    something anyone can act on. `adb logcat` is the only place the reason survives, and it
    survives for seconds.

    So this does the three steps in one go, in the only order that works:

      1. clears the log buffer, so what comes back is THIS launch and not last week's
      2. installs and starts the app
      3. captures the log while it starts, and saves it to a file

    The app tags every line of its own startup `JonesFastLane` (see AndroidLog.cs). This
    script keeps those, and keeps the runtime's own crash channels beside them -
    AndroidRuntime, monodroid, mono-stdout, DOTNET, ActivityManager - because a process that
    dies before managed code runs writes to those and to nothing else. That case is not
    hypothetical: it is precisely what a fast-deployment Debug APK does when it is sideloaded.

.PARAMETER Configuration
    Release (the default) or Debug. Release is the one to try first: it is the configuration
    the working Android app on this phone was built in, it carries no debugging components,
    and it is ahead-of-time compiled so it starts faster.

.PARAMETER Apk
    An explicit APK path, instead of the one this script works out from -Configuration.

.PARAMETER Seconds
    How long to watch the log for after launching. The default of 20 covers a cold start
    with the first-run asset unpack in it.

.PARAMETER All
    Capture the WHOLE log rather than only the tags above. Use this if the filtered capture
    comes back empty - it means the failure is somewhere nobody thought to tag.

.EXAMPLE
    powershell -File tools\android-debug.ps1

.EXAMPLE
    powershell -File tools\android-debug.ps1 -Configuration Debug -Seconds 30 -All

.NOTES
    The phone needs Developer options on, USB debugging on, and this computer authorised -
    the phone shows a prompt the first time, and `adb devices` reports `unauthorized` until
    it is accepted.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',

    [string] $Apk,

    [int] $Seconds = 20,

    [switch] $All
)

$ErrorActionPreference = 'Stop'

$package  = 'com.jones.fastlane'
$repo     = Split-Path -Parent $PSScriptRoot
$android  = Join-Path $repo 'src\Jones.App\Jones.App.Android'

# ---------------------------------------------------------------- adb
$adb = (Get-Command adb.exe -ErrorAction SilentlyContinue).Source
if (-not $adb) { $adb = Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe' }
if (-not (Test-Path $adb)) {
    throw "adb.exe not found. Install the Android SDK platform-tools, or put adb.exe on PATH."
}

# ---------------------------------------------------------------- the APK
if (-not $Apk) {
    $candidates = @(
        (Join-Path $android "bin\$Configuration\net10.0-android\publish\$package-Signed.apk"),
        (Join-Path $android "bin\$Configuration\net10.0-android\$package-Signed.apk")
    )
    $Apk = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Apk -or -not (Test-Path $Apk)) {
    throw "No $Configuration APK found. Build one first:`n" +
          "    cd $android`n" +
          "    dotnet publish -c Release"
}

$size = [math]::Round((Get-Item $Apk).Length / 1MB, 1)
Write-Host "APK    : $Apk ($size MB, built $((Get-Item $Apk).LastWriteTime))"

# A standalone APK carries its managed assemblies inside it. One that does not will install
# and then refuse to open, with no managed code ever running and therefore nothing logged -
# which is worth knowing BEFORE spending twenty seconds watching an empty log.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($Apk)
try {
    # Three shapes, because .NET for Android has used all three and which one you get
    # depends on the configuration: a plain `assemblies/` folder, one `libassembly-store.so`
    # per ABI (Release), or one `lib_<Name>.dll.so` per assembly per ABI (Debug, embedded).
    $hasAssemblies = @($zip.Entries | Where-Object {
        $_.FullName -like 'assemblies/*' -or
        $_.FullName -like '*libassembly-store*' -or
        $_.FullName -match '^lib/[^/]+/lib_.*\.dll\.so$'
    }).Count
    $wavs = @($zip.Entries | Where-Object { $_.FullName -like 'assets/speech/*.wav' }).Count
    $data = @($zip.Entries | Where-Object { $_.FullName -eq 'assets/jones-data.zip' }).Count
} finally { $zip.Dispose() }

Write-Host "Assets : $wavs speech WAVs, jones-data.zip $(if ($data) { 'present' } else { 'MISSING' })"
if ($hasAssemblies -eq 0) {
    Write-Warning ("This APK contains NO MANAGED ASSEMBLIES. It is a fast-deployment build: " +
                   "installing it on its own gives an app that cannot start and cannot say why. " +
                   "Rebuild with EmbedAssembliesIntoApk=true.")
}

# ---------------------------------------------------------------- device
# adb writes ordinary progress to stderr - "daemon not running; starting now", the install
# progress bar - and PowerShell turns a native command's stderr into an ErrorRecord, which
# under `$ErrorActionPreference = 'Stop'` would abort this script over a message that says
# nothing is wrong. `throw` is unaffected by the preference, so the checks below still stop
# the script when they mean to.
$ErrorActionPreference = 'Continue'

# Started explicitly so its startup chatter is not attributed to the first real command.
& $adb start-server 2>&1 | Out-Null

$devices = & $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\S' }
if (-not $devices) {
    throw "No device. Plug the phone in, enable USB debugging, and accept the prompt on the phone."
}
Write-Host "Device : $($devices -join '; ')"
Write-Host ''

# ---------------------------------------------------------------- run
Write-Host 'Installing...'
& $adb install -r -- "$Apk"

Write-Host 'Clearing the log and launching...'
& $adb shell am force-stop $package
& $adb logcat -c

# The activity's Java name is generated from a hash of the namespace, so it is not worth
# guessing: `monkey` starts whatever the launcher would have started.
& $adb shell monkey -p $package -c android.intent.category.LAUNCHER 1 | Out-Null

$log = Join-Path $env:TEMP "jones-logcat-$(Get-Date -Format yyyyMMdd-HHmmss).txt"
Write-Host "Capturing for $Seconds seconds..."

$filter = if ($All) {
    @('-v', 'time', '*:V')
} else {
    @('-v', 'time',
      'JonesFastLane:V', 'AndroidRuntime:E', 'monodroid:V', 'monodroid-assembly:V',
      'mono-stdout:V', 'mono-stderr:V', 'DOTNET:V', 'ActivityManager:I', 'libc:F', '*:S')
}

$capture = Start-Process -FilePath $adb -ArgumentList (@('logcat') + $filter) `
                         -RedirectStandardOutput $log -NoNewWindow -PassThru
Start-Sleep -Seconds $Seconds
if (-not $capture.HasExited) { Stop-Process -Id $capture.Id -Force }

Write-Host ''
Write-Host "Log saved to: $log"
Write-Host ('-' * 78)
Get-Content $log
Write-Host ('-' * 78)

$failures = Select-String -Path $log -Pattern 'FAILED:|FATAL|AndroidRuntime|Exception' -ErrorAction SilentlyContinue
if ($failures) {
    Write-Host ''
    Write-Warning "Send the file above. The lines that matter:"
    $failures | Select-Object -First 20 | ForEach-Object { Write-Host "  $($_.Line)" }
} elseif ((Get-Item $log).Length -eq 0) {
    Write-Host ''
    Write-Warning ("The log is EMPTY. Either the process died before any managed code ran " +
                   "(see the assemblies warning above), or the tags are wrong - re-run with -All.")
} else {
    Write-Host ''
    Write-Host 'No failure lines found. If the app is on screen, it started.'
}
