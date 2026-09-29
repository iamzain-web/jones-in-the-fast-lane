# Lays the downloaded Stable Diffusion 1.5 and ControlNet component files out in the
# directory shape diffusers' from_pretrained expects, so tools/gen_walkers.py can load
# them with no network and no hand-built configs.
#
# HARD LINKS, not copies: the weights are 2.7GB and the flat downloads are the resume
# point if anything has to be fetched again. Same volume, so a link is free; the fallback
# is a copy for the case where it is not.
#
# WHY THE MODELS ARE NOT IN THE REPO: 2.7GB of weights, plus the 2.4GB torch wheel they
# need. They live beside the upscaler ONNX models that tools/restore_faces.py already
# caches, under %LOCALAPPDATA%\jones-upscale-models.
#
#   gen\sd15-unet.safetensors          -> gen\sd15\unet\diffusion_pytorch_model.safetensors
#   gen\sd15-vae.safetensors           -> gen\sd15\vae\diffusion_pytorch_model.safetensors
#   gen\sd15-text_encoder.safetensors  -> gen\sd15\text_encoder\model.safetensors
#   gen\cn-openpose.safetensors        -> gen\cn-openpose\diffusion_pytorch_model.safetensors
#   ... and the matching config.json / tokenizer files.

$gen = Join-Path $env:LOCALAPPDATA 'jones-upscale-models\gen'
if (-not (Test-Path $gen)) { throw "$gen does not exist - nothing has been downloaded" }

function Link([string]$from, [string]$to) {
    $dir = Split-Path $to -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    if (Test-Path $to) { Remove-Item $to -Force }
    try { New-Item -ItemType HardLink -Path $to -Target $from -ErrorAction Stop | Out-Null }
    catch { Copy-Item $from $to -Force }
    "  $(Split-Path $to -Leaf)  <-  $(Split-Path $from -Leaf)"
}

$map = @(
    @('sd15-unet.safetensors',          'sd15\unet\diffusion_pytorch_model.safetensors'),
    @('sd15-unet-config.json',          'sd15\unet\config.json'),
    @('sd15-vae.safetensors',           'sd15\vae\diffusion_pytorch_model.safetensors'),
    @('sd15-vae-config.json',           'sd15\vae\config.json'),
    @('sd15-text_encoder.safetensors',  'sd15\text_encoder\model.safetensors'),
    @('sd15-te-config.json',            'sd15\text_encoder\config.json'),
    @('sd15-scheduler.json',            'sd15\scheduler\scheduler_config.json'),
    @('tok-vocab.json',                 'sd15\tokenizer\vocab.json'),
    @('tok-merges.txt',                 'sd15\tokenizer\merges.txt'),
    @('tok-config.json',                'sd15\tokenizer\tokenizer_config.json'),
    @('tok-special.json',               'sd15\tokenizer\special_tokens_map.json'),
    @('cn-openpose.safetensors',        'cn-openpose\diffusion_pytorch_model.safetensors'),
    @('cn-openpose-config.json',        'cn-openpose\config.json'),
    # tools/gen_board.py conditions on soft edges, not on a skeleton.
    @('cn-softedge.safetensors',        'cn-softedge\diffusion_pytorch_model.safetensors'),
    @('cn-softedge-config.json',        'cn-softedge\config.json')
)

foreach ($m in $map) {
    $from = Join-Path $gen $m[0]
    if (Test-Path $from) { Link $from (Join-Path $gen $m[1]) }
    else { "  skipped $($m[0]) (not downloaded)" }
}
"arranged under $gen"
