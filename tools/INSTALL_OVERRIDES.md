# Installs that shipped against the gate

Every entry here is a build `check_install` rejected and a person installed anyway, with the reason they gave. The gate was not weakened for any of them - see the comment at the override in `tools/install_walk.py` for why that distinction is the whole point.

## 2026-10-05 07:29

**Reason:** Interim: trades a sheared sole on 282's raised foot for the duplicate shoe Zain actually complained about (why does he have 2 shoes on the same leg?). The fragments are the rigid region's box clipping a shoe the source photographs half-occluded - not fixable in the renderer, see the comment at the box in tools/warp_walk.py. Superseded as soon as 282's source is regenerated with the feet clear. FOOT_FRAG_MAX deliberately NOT relaxed: it still catches the same defect on 280.

Sources: png4x `C:\Users\ZAIN~1.CAS\AppData\Local\Temp\claude\C--AI-Accelerator\1709fd27-425c-4815-a187-5d85a4ff583f\scratchpad\fin7_4`, png12x `C:\Users\ZAIN~1.CAS\AppData\Local\Temp\claude\C--AI-Accelerator\1709fd27-425c-4815-a187-5d85a4ff583f\scratchpad\fin7_12`

Gate failures waived:

- `png12x/view_282_l0_c0_s2.png` - 2 detached fragment(s)
- `png12x/view_282_l0_c0_s3.png` - 2 detached fragment(s)
- `png12x/view_282_l0_c1_s1.png` - 2 detached fragment(s)
