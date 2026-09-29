# Builds the regenerated town board into the switchable art set.
#
# Like tools/build_new_walkers.ps1, nothing here touches assets/png, assets/png12x, or the
# app's own Assets/game/board.png. Everything lands in assets/png12x-new, which
# RenderScale searches BEFORE assets/png12x when JONES_ART_SET=new is set and ignores
# entirely when it is not.
#
#     $env:JONES_ART_SET = 'new'        the new board
#     Remove-Item Env:\JONES_ART_SET    the 1990 board
#
# WHAT IT WRITES
#   assets/png12x-new/board.png     what MainView draws through SciArt.Board
#   assets/png12x-new/pic_11.png    the same image under its resource number, which
#                                   MainViewModel.cs:5197 asks for separately. BOTH are
#                                   needed: they are two different lookups for one picture,
#                                   and supplying only one leaves the other on the old art.
#
# ORDERING HAZARDS
#   * The SOURCE is assets/png12x/board.png, which tools/Upscale produced from png4x.
#     Re-running `Upscale 12 4` rewrites that source and does NOT touch the set, so the set
#     goes stale against art that moved underneath it. Re-run this afterwards.
#   * The cel dump in step 1 must come from the RAW pic, not from the composited PNG. The
#     whole method depends on the fourteen building cels being separable, and they only are
#     in assets/raw/pic/11.pic.
#   * This shares no file with tools/fit_walkers.py, so the board and the characters can be
#     rebuilt independently and in either order.
#
# Step 2 is the only one that needs the GPU and it takes about a minute. Step 3 is CPU and
# takes a few, almost all of it in the 4x upscaler.

param(
    [string]$Scratch = (Join-Path $env:TEMP 'jones-board'),
    [int]$Factor = 12,
    [double]$Strength = 0.62,
    [double]$Control = 0.9
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$py = Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'
if (-not (Test-Path $py)) { throw "no python at $py (the PATH python.exe is a Store stub)" }

$cels = Join-Path $Scratch 'picels'
$set = Join-Path $root "assets\png$($Factor)x-new"
$raw = Join-Path $root 'assets\raw\pic\11.pic'
$cn = Join-Path $env:LOCALAPPDATA 'jones-upscale-models\gen\cn-softedge'

New-Item -ItemType Directory -Force $Scratch | Out-Null

"== 1. split pic 11 into its fifteen cels =="
& $py (Join-Path $PSScriptRoot 'decode_pic_full.py') $raw (Join-Path $Scratch 'pic11.png') `
    --cels $cels --alpha | Select-Object -Last 3

"== 2. regenerate the ground (GPU, ~1 min) =="
$args = @('--cels', $cels, '--out', $set, '--factor', $Factor,
          '--strength', $Strength, '--overlay', (Join-Path $root 'tools\samples'))
if (Test-Path $cn) { $args += @('--controlnet', $cn, '--control', $Control) }
else { "  no soft-edge ControlNet at $cn - running img2img alone, structure will drift more" }

"== 3. stamp the buildings back and fit to $($Factor)x =="
& $py (Join-Path $PSScriptRoot 'gen_board.py') @args

""
"Board built into $set"
"  tools/samples/board_hotspots.png proves the thirteen click rectangles still land"
"  `$env:JONES_ART_SET = 'new'   to use it"
