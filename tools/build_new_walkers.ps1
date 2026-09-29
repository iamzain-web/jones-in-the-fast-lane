# Builds the generated character art, end to end, from the poses to the switchable art set.
#
# Nothing here touches assets/png or assets/png12x. Everything lands in
# assets/png12x-new, which is an ART SET: RenderScale searches it BEFORE assets/png12x
# when JONES_ART_SET=new is in the environment, and ignores it entirely when it is not.
# So the switch is
#
#     $env:JONES_ART_SET = 'new'   the new cast
#     Remove-Item Env:\JONES_ART_SET   the ported photographs
#
# and reverting for good is deleting one directory.
#
# ORDER, AND WHY
#
#   1. sprite_pose.py     measures the twenty walker views and writes a skeleton per cel
#   2. gen_walkers.py     the new characters, as raw 512x1024 frames (GPU, about an hour)
#   3. fit_walkers.py     mattes them and fits them into the original cel boxes at 12x
#   4. fit_walkers.py --portraits   rebuilds view 500, the character-select screen
#   5. smooth_walk.py --dir         the three in-between frames per cel
#   6. walker_samples.py            before/after stills and animations
#
# STEP 5 IS NOT OPTIONAL. MainViewModel.WalkerFrame displays four frames per cel and asks
# SciArt.SubCel for view_V_lL_cC_sN.png. If the set does not supply those, the lookup falls
# through to assets/png12x and finds the PORTED ones - so the cycle shows one frame of the
# new character followed by three of the old one, sixty times a minute. Re-run step 5 after
# any re-run of step 3.
#
# Steps 3-6 are CPU only and take a couple of minutes; only step 2 is slow.

param(
    [string]$Scratch = (Join-Path $env:TEMP 'jones-walkers'),
    [int]$Factor = 12,
    [string]$Bodies = 'body0,body1,body2,body3,jones',
    [switch]$SkipGenerate
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$py = Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'
if (-not (Test-Path $py)) { throw "no python at $py (the PATH python.exe is a Store stub)" }

$poses = Join-Path $Scratch 'poses'
$gen = Join-Path $Scratch 'gen'
$set = Join-Path $root "assets\png$($Factor)x-new"
$base = Join-Path $root "assets\png$($Factor)x"
if (-not (Test-Path $base)) {
    throw "$base does not exist. Run: tools\Upscale\bin\Release\net8.0\Upscale.exe $Factor 4"
}

New-Item -ItemType Directory -Force $Scratch | Out-Null

"== 1. poses =="
& $py (Join-Path $PSScriptRoot 'sprite_pose.py') --out $poses --size 512x1024

if (-not $SkipGenerate) {
    "== 2. generate (GPU, ~1 hour) =="
    & $py (Join-Path $PSScriptRoot 'gen_walkers.py') --poses $poses --out $gen `
        --bodies $Bodies --steps 24 --offload
}

"== 3. matte and fit =="
& $py (Join-Path $PSScriptRoot 'fit_walkers.py') --poses $poses --gen $gen --out $set --factor $Factor

"== 4. character-select screen (view 500) =="
& $py (Join-Path $PSScriptRoot 'fit_walkers.py') --poses $poses --gen $gen --out $set `
    --factor $Factor --portraits --against $base

"== 5. in-between frames =="
& $py (Join-Path $PSScriptRoot 'smooth_walk.py') --dir $set --scale $Factor --no-samples

"== 6. samples =="
& $py (Join-Path $PSScriptRoot 'walker_samples.py') --new $set --old $base `
    --out (Join-Path $root 'tools\samples')

"== consistency =="
& $py (Join-Path $PSScriptRoot 'fit_walkers.py') --poses $poses --measure $set --against $base

""
"Set built: $set"
"  `$env:JONES_ART_SET = 'new'   to use it"
"  Remove-Item Env:\JONES_ART_SET   to go back to the ported art"
