<#
.SYNOPSIS
    Publishes the browser head and puts it on GitHub Pages, then proves it is there.

.DESCRIPTION
    One command for the whole deploy. Everything this does by hand is written up in
    BROWSER.md; this exists because each of those steps fails SILENTLY, and a web build
    that is broken looks exactly like a web build that is fine until someone opens it.

    The guards are the point, not the copying:

      * THE ART IS NOT IN THE REPOSITORY. `assets/` is gitignored - it is Sierra's
        extracted data, not this port's work to redistribute - so a clone builds a game
        that falls back to the 838 1x cels compiled into the assembly. That is a working,
        shippable, WRONG site: her town, her buildings, her signs, under our name. The
        first check refuses to deploy without the art.

      * `dotnet publish` DOES NOT CLEAN. Hashed assemblies accumulate across builds; one
        run here left eight `dotnet.native.*.wasm` of ~30 MB each. We publish into an
        empty directory and then assert there is exactly one.

      * THE `.gz`/`.br` SIDECARS ARE DEAD WEIGHT. A static host serves neither and the
        loader never asks for them. They were 380 MB of a 475 MB site.

      * `.nojekyll` OR NOTHING RUNS. Without it Pages silently skips every directory
        whose name begins with an underscore, which is the entire .NET runtime.

      * THE PUSH NEEDS THE RIGHT ACCOUNT. The remote URL carries the user for a reason:
        the bare URL resolves to a different stored credential and GitHub answers 403.

    Then it waits for Pages to build and FETCHES THE LIVE SITE to confirm this build is
    the one being served - the assembly this publish produced, and the board art beside
    it. An exit code is not evidence; the only thing that counts is the bytes a visitor
    gets.

.PARAMETER Test
    Run the full test suite before publishing. Off by default: the suite takes about
    three and a half minutes and most deploys here are art changes, which it cannot see.

.PARAMETER NoPush
    Build, check and stage everything, but stop before pushing. For seeing what WOULD go
    out.

.EXAMPLE
    .\tools\deploy-web.ps1

.EXAMPLE
    .\tools\deploy-web.ps1 -Test
#>
[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$NoPush
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is 10x slower with the bar

# ----------------------------------------------------------------------------------
# Plumbing
# ----------------------------------------------------------------------------------

function Step([string]$text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Ok([string]$text) { Write-Host "    $text" -ForegroundColor DarkGray }
function Warn([string]$text) { Write-Host "    ! $text" -ForegroundColor Yellow }

function Die([string]$text) {
    Write-Host "`nSTOPPED: $text`n" -ForegroundColor Red
    exit 1
}

# Native commands do not throw on failure and $ErrorActionPreference does not reach them,
# so every one of them goes through here. A deploy that carries on past a failed publish
# is how a stale site gets pushed over a good one.
function Native([string]$exe, [string[]]$arguments, [string]$what) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { Die "$what failed (exit $LASTEXITCODE)." }
}

$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root 'src\Jones.App\Jones.App.Browser\Jones.App.Browser.csproj'
$PublishDir = Join-Path $Root 'src\Jones.App\Jones.App.Browser\bin\Release\net10.0-browser\publish'
$Wwwroot = Join-Path $PublishDir 'wwwroot'
$Worktree = Join-Path $Root '.deploy\gh-pages'

# ----------------------------------------------------------------------------------
# 1. The art has to be here, or the site is Sierra's
# ----------------------------------------------------------------------------------

Step 'Checking the art is present'

$CastDir = Join-Path $Root 'assets\png4x-cast0'
$BaseDir = Join-Path $Root 'assets\png4x'

if (-not (Test-Path $BaseDir)) {
    # A SINGLE-QUOTED here-string, deliberately: in an expandable one a backtick is the
    # escape character, so "`assets`" would emit a BEL rather than the word.
    Die @'
assets\png4x is missing.

assets/ is gitignored, so a fresh clone has none of it, and the game falls back to the
838 1x cels compiled into Jones.App. That builds and runs and looks finished - and it is
Sierra's 1990 artwork, published under your name.

Extract it first: tools\extract_resources.py. See BROWSER.md.
'@
}

if (-not (Test-Path (Join-Path $CastDir 'board.png'))) {
    Die "assets\png4x-cast0\board.png is missing - the renamed board would not ship. See BUILDINGS.md."
}

$castCount = (Get-ChildItem $CastDir -Filter *.png -ErrorAction SilentlyContinue).Count
if ($castCount -lt 100) {
    Die "assets\png4x-cast0 holds only $castCount cels. That is not the cast set; refusing to deploy a half-replaced board."
}
Ok "png4x-cast0: $castCount cels"

# THE TITLE PANEL. Sierra paints the setup dialogs' cream panel into pic_11; ours is
# generated from board.png by tools\board\title_panel.py. If the two files are identical
# the generator has not been run since the board changed, and the main menu will float
# over the town with no panel behind it - which is how this shipped once.
$castBoard = Join-Path $CastDir 'board.png'
$castPic11 = Join-Path $CastDir 'pic_11.png'
if (-not (Test-Path $castPic11)) {
    Warn 'png4x-cast0\pic_11.png is missing - the title screen will fall back to Sierra''s.'
    Warn 'Run: python tools\board\title_panel.py'
}
elseif ((Get-Item $castBoard).Length -eq (Get-Item $castPic11).Length) {
    Warn 'png4x-cast0\pic_11.png is the same size as board.png - the title panel looks unpainted.'
    Warn 'Run: python tools\board\title_panel.py'
}
else {
    Ok 'title panel present in pic_11'
}

# ----------------------------------------------------------------------------------
# 2. State of the working tree
# ----------------------------------------------------------------------------------

Step 'Checking the working tree'

Push-Location $Root
try {
    $dirty = & git status --porcelain
    if ($LASTEXITCODE -ne 0) { Die 'git status failed - is this a checkout?' }
    if ($dirty) {
        Warn 'Uncommitted changes - the deployed build will not match any commit:'
        $dirty | Select-Object -First 10 | ForEach-Object { Warn "    $_" }
    }
    $sha = (& git rev-parse --short HEAD).Trim()
    Ok "HEAD is $sha"

    # Owner and repository, read once from the remote and used for both the push URL and
    # the Pages address. $Matches is reused by every later -match in the script, so these
    # are captured into named variables immediately rather than read back further down.
    $origin = (& git remote get-url origin).Trim()
    if ($origin -notmatch 'github\.com[:/]+(?:[^/@]+@)?([^/]+)/([^/]+?)(?:\.git)?/?$') {
        Die "Cannot read the GitHub owner and repository from origin: $origin"
    }
    $Owner = $Matches[1]
    $Repo = $Matches[2]

    # THE PUSH MUST NAME THE ACCOUNT. A bare github.com URL picks up whichever credential
    # the manager has stored for the host - here, a different user - and the push dies
    # with 403 after the entire build has run.
    if ($origin -match '@') { $pushUrl = $origin }
    else {
        $pushUrl = "https://$Owner@github.com/$Owner/$Repo.git"
        Warn "origin names no account; pushing as '$Owner'."
    }
    Ok "$Owner/$Repo"
}
finally { Pop-Location }

# ----------------------------------------------------------------------------------
# 3. Tests, if asked
# ----------------------------------------------------------------------------------

if ($Test) {
    Step 'Running the test suite'
    Native 'dotnet' @('test', (Join-Path $Root 'src\Jones.Tests\Jones.Tests.csproj'), '--nologo', '-v', 'q') 'dotnet test'
    Ok 'suite passed'
}

# ----------------------------------------------------------------------------------
# 4. Publish, into an EMPTY directory
# ----------------------------------------------------------------------------------

Step 'Publishing the browser head'

if (Test-Path $PublishDir) {
    try { Remove-Item $PublishDir -Recurse -Force }
    catch {
        Die @"
Could not empty the publish directory:
  $PublishDir
  $($_.Exception.Message)

Something is holding a file open - usually a local web server still serving it. Stop it
and run this again. Publishing on top of the old output leaves stale hashed assemblies
behind, which is how a 475 MB site happens.
"@
    }
}

Native 'dotnet' @('publish', $Project, '-c', 'Release', '-o', $PublishDir, '--nologo') 'dotnet publish'

if (-not (Test-Path $Wwwroot)) { Die "publish produced no wwwroot at $Wwwroot" }

# ----------------------------------------------------------------------------------
# 5. Is what we just built actually whole?
# ----------------------------------------------------------------------------------

Step 'Checking the publish output'

$framework = Join-Path $Wwwroot '_framework'

$natives = @(Get-ChildItem $framework -Filter 'dotnet.native.*.wasm' | Where-Object { $_.Name -notmatch '\.(gz|br)$' })
if ($natives.Count -ne 1) {
    Die "expected exactly one dotnet.native.*.wasm, found $($natives.Count). The output was not clean."
}
Ok ("runtime {0} ({1:N1} MB)" -f $natives[0].Name, ($natives[0].Length / 1MB))

$appAsm = @(Get-ChildItem $framework -Filter 'Jones.App.*.wasm' |
    Where-Object { $_.Name -notmatch '\.(gz|br)$' -and $_.Name -notmatch 'Jones\.App\.Browser' })
if ($appAsm.Count -ne 1) {
    Die "expected exactly one Jones.App.*.wasm, found $($appAsm.Count). The output was not clean."
}
# Kept: this exact file is what we ask the live site for at the end. Its name carries a
# content hash, so serving it is proof the deployed site is THIS build and not the last.
$appAsmName = $appAsm[0].Name
Ok "assembly $appAsmName"

$manifest = Join-Path $Wwwroot 'art\art-manifest.txt'
if (-not (Test-Path $manifest)) {
    Die 'art\art-manifest.txt is missing. Without it every cel costs a round trip to learn it is absent.'
}
$manifestLines = (Get-Content $manifest).Count
if ($manifestLines -lt 1000) {
    Die "art-manifest.txt lists only $manifestLines cels - the art did not copy. Expected ~1,250."
}
Ok "art manifest: $manifestLines cels"

$publishedBoard = Join-Path $Wwwroot 'art\png4x-cast0\board.png'
if (-not (Test-Path $publishedBoard)) { Die 'the renamed board did not reach wwwroot\art.' }
$boardLength = (Get-Item $publishedBoard).Length
Ok ("board {0:N0} bytes" -f $boardLength)

# ----------------------------------------------------------------------------------
# 6. Stage it on the gh-pages worktree
# ----------------------------------------------------------------------------------

Step 'Staging the gh-pages branch'

Push-Location $Root
try {
    Native 'git' @('fetch', '--quiet', 'origin', 'gh-pages') 'git fetch'

    if (Test-Path (Join-Path $Worktree '.git')) {
        Native 'git' @('-C', $Worktree, 'reset', '--quiet', '--hard', 'origin/gh-pages') 'git reset'
        Native 'git' @('-C', $Worktree, 'clean', '--quiet', '-fdx') 'git clean'
        Ok 'reused the existing worktree'
    }
    else {
        if (Test-Path $Worktree) { Remove-Item $Worktree -Recurse -Force }
        Native 'git' @('worktree', 'add', '--quiet', '-B', 'gh-pages', $Worktree, 'origin/gh-pages') 'git worktree add'
        Ok "created a worktree at .deploy\gh-pages"
    }
}
finally { Pop-Location }

# Empty it, keeping only git's own bookkeeping. In a LINKED worktree `.git` is a file,
# not a directory, which is why this matches by name rather than by type.
Get-ChildItem $Worktree -Force |
    Where-Object { $_.Name -ne '.git' } |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force }

Copy-Item -Path (Join-Path $Wwwroot '*') -Destination $Worktree -Recurse -Force

# The sidecars: a static host serves neither, and the loader never asks for them.
$sidecars = @(Get-ChildItem $Worktree -Recurse -File -Include '*.gz', '*.br')
if ($sidecars.Count -gt 0) {
    $saved = ($sidecars | Measure-Object -Property Length -Sum).Sum
    $sidecars | Remove-Item -Force
    Ok ("dropped {0:N0} .gz/.br sidecars ({1:N0} MB no browser would fetch)" -f $sidecars.Count, ($saved / 1MB))
}

# Without this, Pages skips _framework entirely and the page loads nothing.
$nojekyll = Join-Path $Worktree '.nojekyll'
if (-not (Test-Path $nojekyll)) { New-Item -ItemType File -Path $nojekyll | Out-Null }
Ok '.nojekyll in place'

# NOTHING IN A DEPLOYED SITE IS TEXT, AND LETTING GIT THINK OTHERWISE IS A REAL HAZARD.
#
# This repository runs with core.autocrlf=true, which rewrites line endings on the way
# into a commit. Every file here is a BUILD OUTPUT being served verbatim: the runtime's
# own .js, the loader, the source maps. Rewriting one changes the bytes a browser
# receives from the bytes the build produced - and .NET records a content hash for its
# assets, so a rewritten loader is not a cosmetic difference, it is a site that refuses
# to start with an integrity error.
#
# Belt and braces, because this is not a failure anyone would diagnose quickly: the
# attributes file settles it for any future clone, and -c settles it for this run without
# touching the main checkout's config (a worktree shares it).
Set-Content -Path (Join-Path $Worktree '.gitattributes') -Encoding ASCII -Value @(
    '# A published site. Every file is a build output, served byte for byte.',
    '# Nothing here may have its line endings rewritten; the runtime checks hashes.',
    '* -text'
)
Ok '.gitattributes pins every file as binary'

$deployFiles = @(Get-ChildItem $Worktree -Recurse -File | Where-Object { $_.FullName -notmatch '\\\.git\\' })
$deploySize = ($deployFiles | Measure-Object -Property Length -Sum).Sum
Ok ("{0:N0} files, {1:N0} MB" -f $deployFiles.Count, ($deploySize / 1MB))

if ($deployFiles.Count -lt 1000) {
    Die "only $($deployFiles.Count) files staged - that is not a whole site."
}

# ----------------------------------------------------------------------------------
# 7. Commit and push
# ----------------------------------------------------------------------------------

Step 'Committing'

Native 'git' @('-c', 'core.autocrlf=false', '-c', 'core.eol=lf', '-C', $Worktree, 'add', '-A') 'git add'

& git -C $Worktree diff --cached --quiet
$hasChanges = ($LASTEXITCODE -ne 0)

if (-not $hasChanges) {
    Ok 'nothing changed - the live site is already this build'
}
else {
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm') + ' UTC'
    $message = "Web build from $sha ($stamp)"
    Native 'git' @('-C', $Worktree, 'commit', '--quiet', '-m', $message) 'git commit'
    Ok $message

    if ($NoPush) {
        Warn '-NoPush: committed to the local gh-pages branch, not pushed.'
        Write-Host "`nStaged at $Worktree`n"
        exit 0
    }

    Step 'Pushing'
    Native 'git' @('-C', $Worktree, 'push', '--quiet', $pushUrl, 'gh-pages') 'git push'
    Ok 'pushed'
}

if ($NoPush) { exit 0 }

# ----------------------------------------------------------------------------------
# 8. Prove it is live
# ----------------------------------------------------------------------------------
#
# Pages rebuilds on its own schedule after a push, so this polls rather than assuming.
# What it asks for matters: the assembly's name carries a content hash, so a 200 on THAT
# file is proof the site being served is this build. The board's length is checked too,
# because art changes do not move the assembly's hash.

Step 'Waiting for GitHub Pages'

$site = "https://$Owner.github.io/$Repo/"

function Get-Status([string]$url) {
    try {
        $r = Invoke-WebRequest -Uri $url -Method Head -UseBasicParsing -TimeoutSec 20
        return @{ Code = [int]$r.StatusCode; Length = [int64]$r.Headers['Content-Length'] }
    }
    catch { return @{ Code = 0; Length = 0 } }
}

# The loader's length is checked as well as the assembly's name. It is the one file git
# would convert if the guards above ever came undone, and a CRLF rewrite makes it longer
# by exactly one byte per line - which is also enough to fail the runtime's own integrity
# check. Cheaper to ask than to diagnose.
$loaderLength = (Get-Item (Join-Path $framework 'dotnet.js')).Length

$deadline = (Get-Date).AddMinutes(5)
$live = $false

while ((Get-Date) -lt $deadline) {
    $cacheBust = "?v=" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

    # ${site} AND NOT "$site`art/...". A backtick in a double-quoted string is the escape
    # character, and `a is a recognised one: it emits a BEL. The first version of this
    # line asked for a URL with a control character where the "a" of "art" belonged, got
    # a 404 every time, and reported a five-minute Pages timeout on a site that had been
    # live for four of them.
    $asm = Get-Status "${site}_framework/$appAsmName$cacheBust"
    $board = Get-Status "${site}art/png4x-cast0/board.png$cacheBust"
    $loader = Get-Status "${site}_framework/dotnet.js$cacheBust"

    if ($asm.Code -eq 200 -and $board.Code -eq 200 -and $loader.Code -eq 200) {
        if ($board.Length -ne $boardLength) {
            Die "the live board is $($board.Length) bytes, this build's is $boardLength. Pages is serving different art."
        }
        if ($loader.Length -ne $loaderLength) {
            Die @"
the live dotnet.js is $($loader.Length) bytes, this build's is $loaderLength.

The loader has been altered between here and the server - almost certainly line endings.
The runtime checks content hashes, so the page will not start. Check .gitattributes in
the gh-pages branch.
"@
        }
        $live = $true
        break
    }

    Write-Host '    .' -NoNewline -ForegroundColor DarkGray
    Start-Sleep -Seconds 10
}

Write-Host ''

if (-not $live) {
    Warn 'Pages has not served this build within five minutes.'
    Warn 'That is usually a slow build rather than a failure - check:'
    Warn "    https://github.com/$Owner/$Repo/deployments"
    Warn "and then open $site"
    exit 1
}

$index = Get-Status $site
if ($index.Code -ne 200) { Die "the site root answered $($index.Code)." }

Write-Host ''
Write-Host '  LIVE  ' -ForegroundColor Black -BackgroundColor Green -NoNewline
Write-Host "  $site" -ForegroundColor Green
Write-Host ''
Ok "serving $appAsmName, an unaltered loader, and the $([Math]::Round($boardLength/1KB)) KB board from this build"
Write-Host ''
