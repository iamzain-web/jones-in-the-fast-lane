namespace Jones.App;

/// <summary>
/// The game's VGA palette: the RGB behind a palette INDEX.
///
/// WHY THIS EXISTS. The scripts name colours by index and nothing else — `textColor 39`,
/// `shadowColor 115`, `dsBACKGROUND 101`, `dsCOLOR 0`. Every one of those is meaningless
/// without the palette, and every colour in this port that was written as a hex literal
/// instead of looked up here was invented. The art is decoded to PNGs with its palette
/// already applied, so this table is the only place an index can still be resolved.
///
/// WHERE THE NUMBERS COME FROM. Each SCI1 view carries its own 1284-byte palette at the
/// offset in bytes 6-7 (the format is documented in <c>tools/decode_views.py</c>:
/// 256 bytes of index mapping, a 4-byte header, then 256 × (used flag, R, G, B)). 90 of
/// this game's views carry one. Where two views both mark an index used they agree, so
/// there is a single game-wide palette here and not ninety: each entry below is the value
/// the views agree on. The handful of disagreements are ±3 per channel — one view rounding
/// #285068 to #2B536B — and the majority value is taken.
///
/// The standalone palette resource `assets/raw/palette/999.palette` is NOT the source: it
/// marks everything from 119 up as unused, and the interface colours run to 116 with the
/// balloon backgrounds above that. `room1.sc:1151-1152` explains why — the room unsets the
/// flags on 8..16 and 144..255 — and 145..254 are consequently black here.
///
/// CROSS-CHECKS, measured rather than asserted. Three independent ones, each a place where
/// a script names an index and the shipped art can be sampled at the coordinates the script
/// draws at:
///
/// * `broker.sc:208-215` fills its table behind font-10 text with index 93; the broker's own
///   backdrop (view 696) is a flat #6098C8 at (112,8), (115,16), (154,8), (147,16) and at
///   every price row — index 93 here.
/// * `select3.sc:355-366`, `goalsDefine.sc:236` and `viewGoals.sc:395-408` fill with index
///   99; view 501 and view 505 are a flat #7088E0 under all three — index 99 here.
/// * `room1.sc:1445` fills the calculator readout with index 101; the calculator cel
///   (view 0 loop 4) is a flat #98A8B0 under (+22,+6) — index 101 here.
///
/// This is the same route TALKER.md §5.3 took for the speech balloon, and it reproduces
/// that table exactly: 26 → #204838, 27 → #682028, 63 → #C03838, 128 → #F8E898.
///
/// To regenerate: read every `assets/raw/view/*.view`, take each embedded palette's
/// used entries, and keep the majority RGB per index.
/// </summary>
public static class SciPalette
{
    private static readonly uint[] Rgb =
    [
        0x000000, 0x181818, 0x383838, 0x585858, 0x787878, 0x989898, 0xB8B8B8, 0xD8D8D8,
        0x560F0F, 0x771515, 0x971A1A, 0xB82020, 0xD82626, 0xDE4646, 0xE46767, 0xE98787,
        0x281818, 0x202020, 0x302020, 0x282828, 0x202830, 0x382818, 0x382828, 0x282838,
        0x303030, 0x403030, 0x204838, 0x682028, 0x383848, 0x483838, 0x205840, 0x584020,
        0x404048, 0x584030, 0x206048, 0x305848, 0x584040, 0x404858, 0x604838, 0x285068,
        0x306848, 0x585048, 0x604848, 0x485060, 0x685040, 0x207860, 0x385870, 0x307858,
        0x984028, 0x585858, 0x685050, 0x785838, 0x288060, 0x904830, 0x385880, 0x586068,
        0x38B038, 0x486080, 0x806048, 0x405890, 0x786058, 0x409060, 0x589048, 0xC03838,
        0x684888, 0x289878, 0x489858, 0x48A848, 0x585890, 0x689048, 0x704890, 0x489868,
        0x687078, 0x3080A0, 0x48A860, 0xE03838, 0x507098, 0x58A060, 0x907858, 0x3880A8,
        0x58A070, 0x787880, 0x50A080, 0x908068, 0x708090, 0x808890, 0x7088A0, 0x6070C8,
        0xE08040, 0x5890C0, 0xF86848, 0x8890A0, 0xD09058, 0x6098C8, 0xF87850, 0x8080C8,
        0x5890E0, 0xE07080, 0x9898A8, 0x7088E0, 0x7878F0, 0x98A8B0, 0x90C898, 0x88D098,
        0x78A8D0, 0xA8D080, 0x78D0B0, 0xE0B868, 0xF8A068, 0x80D0B8, 0x98D0A0, 0xA8B0B8,
        0xA0B0C8, 0xA0E0A0, 0xB0B8C0, 0x98B8E0, 0xE0C898, 0xA0C0E0, 0xF8B0A0, 0x98E8D0,
        0xA8C8E8, 0xF8C0A0, 0xB8C8E0, 0x98E0F0, 0x98F8D8, 0xC0D0E0, 0xA8D8F8, 0xA0F8E0,
        0xF8E898, 0xD8C8E0, 0xA0E8F8, 0xB8D0F8, 0xD0D8E0, 0xF8F0A0, 0xE0E0D0, 0xE0D0E0,
        0xC0D8F8, 0xC8E0F8, 0xE8E8D8, 0xD8E8F8, 0xE0F0F8, 0xF8F8E0, 0xF0F8F8, 0xF8F8F0,
        0xF0F0F0, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000,
        0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000,
        0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000,
        0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000,
        0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000,
        0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000,
        0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000,
        0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000,
        0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000,
        0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000,
        0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000,
        0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000,
        0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000,
        0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0xFFFFFF,
    ];

    /// <summary>
    /// The opaque ARGB behind a palette index, in the Bgra8888 order
    /// <see cref="SciFont.Render(string, uint)"/> takes.
    /// </summary>
    /// <remarks>
    /// The layout is the one <see cref="TextVm.ToArgb"/> already produces: 0xAARRGGBB in a
    /// uint, which is [B, G, R, A] in memory on a little-endian machine — Bgra8888.
    /// </remarks>
    public static uint Argb(int index) => 0xFF000000u | Rgb[index & 0xFF];

    /// <summary>The same colour as <c>#RRGGBB</c>, for the call sites that still take hex.</summary>
    public static string Hex(int index) => $"#{Rgb[index & 0xFF]:X6}";

    /// <summary>
    /// <c>dsBACKGROUND -1</c> and <c>backColor -1</c>: draw the glyphs alone, with the art
    /// showing through the gaps. Most of the game's text is this.
    /// </summary>
    public const int Transparent = -1;
}
