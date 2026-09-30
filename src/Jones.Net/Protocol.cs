using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jones.Net;

// ----------------------------------------------------------------------------------------
// THE WIRE PROTOCOL
//
// Network play is HOST-AUTHORITATIVE REMOTE DISPLAY. Exactly one copy of the game exists,
// on the host. A player who joins runs the same app as a terminal: the host streams what is
// on screen, as recipes rather than pixels, plus every sound cue; the joiner streams its
// clicks and keys back. Nothing is simulated twice, so nothing can diverge, and the RNG
// question NETWORK-HANDOFF.md raises does not arise: only the host ever rolls.
//
// Everything travels as one JSON object per line (newline-delimited JSON). JSON never
// contains a raw newline, so the framing needs no length prefix.
// ----------------------------------------------------------------------------------------

/// <summary>Bumped whenever a message changes shape. A mismatched joiner is refused.</summary>
public static class ProtocolInfo
{
    public const int Version = 1;

    /// <summary>The default port. Arbitrary, and overridable on the command line.</summary>
    public const int DefaultPort = 7117;
}

/// <summary>
/// How to make one bitmap, so the joiner can make the identical one from its own copy of
/// the game's resources. Every image on screen comes from one of five loaders, and each is
/// fully described by its arguments.
/// </summary>
public sealed record ArtRef
{
    /// <summary>"c" cel, "s" in-between walk frame, "p" pic, "n" named file, "t" rendered text.</summary>
    [JsonPropertyName("k")] public string K { get; init; } = "";

    /// <summary>View / pic number / font number.</summary>
    [JsonPropertyName("a")] public int A { get; init; }

    /// <summary>Loop.</summary>
    [JsonPropertyName("b")] public int B { get; init; }

    /// <summary>Cel.</summary>
    [JsonPropertyName("c")] public int C { get; init; }

    /// <summary>Sub-frame, for "s".</summary>
    [JsonPropertyName("d")] public int D { get; init; }

    /// <summary>The text for "t"; the file name for "n".</summary>
    [JsonPropertyName("s")] public string? S { get; init; }

    /// <summary>Text colour, ARGB.</summary>
    [JsonPropertyName("f")] public uint F { get; init; }

    /// <summary>Shadow colour, ARGB, or null.</summary>
    [JsonPropertyName("h")] public uint? Sh { get; init; }

    /// <summary>Background band colour, ARGB, or null.</summary>
    [JsonPropertyName("g")] public uint? Bg { get; init; }

    public static ArtRef Cel(int view, int loop, int cel) => new() { K = "c", A = view, B = loop, C = cel };
    public static ArtRef SubCel(int view, int loop, int cel, int sub) => new() { K = "s", A = view, B = loop, C = cel, D = sub };
    public static ArtRef Pic(int number) => new() { K = "p", A = number };
    public static ArtRef Named(string file) => new() { K = "n", S = file };

    public static ArtRef Text(int font, string text, uint argb, uint? shadow, uint? back) =>
        new() { K = "t", A = font, S = text, F = argb, Sh = shadow, Bg = back };
}

/// <summary>A positioned piece of art — the host's SpriteVm.</summary>
public sealed record SpriteDto(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("i")] ArtRef Art,
    [property: JsonPropertyName("t")] string? Tip,
    [property: JsonPropertyName("q")] bool Click);

/// <summary>
/// A clickable text line — the host's MenuLineVm (a WButton) — as the ARGUMENTS it was
/// constructed with rather than the bitmaps it rendered. Text lines have two faces (the
/// game's bitmap font and the optional outline face, UiFont) with their own layout rules,
/// so the joiner re-runs the same constructor instead of copying pixels: whatever face it
/// draws in, the hit rectangle is font 10's on both machines.
/// </summary>
public sealed record LineDto(
    [property: JsonPropertyName("s")] string Text,
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("tc")] int TextColour,
    [property: JsonPropertyName("sc")] int ShadowColour,
    [property: JsonPropertyName("fc")] int FlashColour,
    [property: JsonPropertyName("z")] double Size,
    [property: JsonPropertyName("t")] string? Tooltip,
    [property: JsonPropertyName("e")] bool Enabled,
    [property: JsonPropertyName("se")] bool Selected,
    [property: JsonPropertyName("k")] int Key,
    [property: JsonPropertyName("v")] string? Value);

/// <summary>A line of display text — the host's TextVm — as its constructor arguments, for the same reason.</summary>
public sealed record TextDto(
    [property: JsonPropertyName("s")] string Text,
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("z")] double Size,
    [property: JsonPropertyName("c")] string Colour,
    [property: JsonPropertyName("b")] bool Bold,
    [property: JsonPropertyName("f")] int? FontNumber,
    [property: JsonPropertyName("g")] int Background,
    [property: JsonPropertyName("h")] int Shadow,
    [property: JsonPropertyName("v")] string? Value);

/// <summary>A bare hit rectangle: a board hotspot (Text = tooltip) or a balloon button (Text = label).</summary>
public sealed record RectDto(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("w")] double W,
    [property: JsonPropertyName("h")] double H,
    [property: JsonPropertyName("t")] string? Text);

/// <summary>One piece of the speech balloon: a flat fill (colour) or a bitmap.</summary>
public sealed record PartDto(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("w")] double W,
    [property: JsonPropertyName("h")] double H,
    [property: JsonPropertyName("f")] string? Fill,
    [property: JsonPropertyName("i")] ArtRef? Art);

/// <summary>A goal slider's track on the "Set Your Goals" screen.</summary>
public sealed record TrackDto(
    [property: JsonPropertyName("n")] int Index,
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("w")] double W,
    [property: JsonPropertyName("h")] double H);

/// <summary>
/// Everything the view draws, at one instant. The view model's collections are the whole of
/// the screen — the view binds to nothing else that varies — so this is a complete picture.
/// </summary>
public sealed class FrameMsg
{
    [JsonPropertyName("n")] public int Seq { get; set; }

    /// <summary>The host's Screen enum, as an int; drives ShowBoard and the pointer claims.</summary>
    [JsonPropertyName("sc")] public int Screen { get; set; }

    /// <summary>Which seat is allowed to act right now.</summary>
    [JsonPropertyName("ac")] public int Acting { get; set; }

    [JsonPropertyName("cg")] public bool CanGoals { get; set; }
    [JsonPropertyName("cs")] public bool CanStats { get; set; }

    [JsonPropertyName("sp")] public List<SpriteDto> Sprites { get; set; } = [];
    [JsonPropertyName("ho")] public List<RectDto> Hotspots { get; set; } = [];
    [JsonPropertyName("tx")] public List<TextDto> Texts { get; set; } = [];
    [JsonPropertyName("ml")] public List<LineDto> Lines { get; set; } = [];
    [JsonPropertyName("gt")] public List<TrackDto> Tracks { get; set; } = [];
    [JsonPropertyName("ba")] public List<PartDto> Balloon { get; set; } = [];
    [JsonPropertyName("bb")] public List<RectDto> BalloonButtons { get; set; } = [];
}

/// <summary>What the joiner did. See <see cref="InputKind"/>.</summary>
public sealed class InputMsg
{
    [JsonPropertyName("k")] public string Kind { get; set; } = "";

    /// <summary>The frame the joiner was looking at when it clicked.</summary>
    [JsonPropertyName("n")] public int Seq { get; set; }

    /// <summary>Index of the element within its collection, in that frame.</summary>
    [JsonPropertyName("i")] public int Index { get; set; }

    /// <summary>
    /// The element's identity (<see cref="Identity"/>), so the host can find it again if the
    /// screen was rebuilt between the frame going out and the click coming back.
    /// </summary>
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("key")] public string? KeyName { get; set; }
    [JsonPropertyName("c")] public bool Ctrl { get; set; }
    [JsonPropertyName("s")] public bool Shift { get; set; }

    /// <summary>Goal slider: the pointer y in 320x200 space. Also the slider index for Begin.</summary>
    [JsonPropertyName("y")] public int Y { get; set; }
}

/// <summary>The input kinds. Strings, so a log line reads as what happened.</summary>
public static class InputKind
{
    public const string Sprite = "sprite";
    public const string Hotspot = "hotspot";
    public const string Line = "line";
    public const string BalloonButton = "bbutton";
    public const string DismissBalloon = "dismiss";
    public const string EndTurn = "endturn";
    public const string Key = "key";
    public const string GoalBegin = "goalbegin";
    public const string GoalDrag = "goaldrag";
    public const string GoalEnd = "goalend";
    public const string SkipIntro = "intro";
    public const string DismissWinner = "winner";
    public const string Goals = "goals";
    public const string Stats = "stats";
}

/// <summary>One call on the host's IAudioPlayer, replayed on the joiner.</summary>
public sealed class AudioMsg
{
    [JsonPropertyName("o")] public string Op { get; set; } = "";
    [JsonPropertyName("i")] public int Id { get; set; }
    [JsonPropertyName("l")] public bool Loop { get; set; }
    [JsonPropertyName("r")] public bool Resume { get; set; }
}

/// <summary>The audio operations — one per IAudioPlayer method that makes or stops a sound.</summary>
public static class AudioOp
{
    public const string Speech = "speech";
    public const string StopSpeech = "stopspeech";
    public const string Music = "music";
    public const string StopMusic = "stopmusic";
    public const string CutMusic = "cutmusic";
    public const string PauseMusic = "pausemusic";
    public const string Effect = "effect";
    public const string EndEffectLoop = "endloop";
    public const string Effect2 = "effect2";
    public const string StopEffects = "stopeffects";
}

/// <summary>The joiner's opening line.</summary>
public sealed class HelloMsg
{
    [JsonPropertyName("v")] public int Protocol { get; set; }

    /// <summary>Identifies this joiner across a reconnect, so it gets its own seat back.</summary>
    [JsonPropertyName("t")] public string Token { get; set; } = "";
}

/// <summary>The host's answer. Seat -1 means refused; the host then hangs up.</summary>
public sealed class WelcomeMsg
{
    [JsonPropertyName("s")] public int Seat { get; set; }

    /// <summary>For the log only. Never shown in the game (CLAUDE.md §1).</summary>
    [JsonPropertyName("r")] public string? Reason { get; set; }
}

/// <summary>One line on the wire.</summary>
public sealed class Envelope
{
    [JsonPropertyName("hello")] public HelloMsg? Hello { get; set; }
    [JsonPropertyName("welcome")] public WelcomeMsg? Welcome { get; set; }
    [JsonPropertyName("frame")] public FrameMsg? Frame { get; set; }
    [JsonPropertyName("input")] public InputMsg? Input { get; set; }
    [JsonPropertyName("audio")] public AudioMsg? Audio { get; set; }
}

/// <summary>Encoding and decoding of <see cref="Envelope"/> lines.</summary>
public static class Wire
{
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>One envelope as a UTF-8 line, newline included.</summary>
    public static byte[] Encode(Envelope e)
    {
        var json = JsonSerializer.Serialize(e, Options);
        return Encoding.UTF8.GetBytes(json + "\n");
    }

    public static Envelope? Decode(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<Envelope>(line, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The identity strings that let the host match a click to an element after a rebuild. Both
/// ends compute them from the same DTO, so they agree by construction.
/// </summary>
public static class Identity
{
    public static string Of(SpriteDto s) => $"{s.X},{s.Y},{Art(s.Art)}";
    public static string Of(LineDto l) => $"{l.X},{l.Y},{l.Text}";
    public static string Of(RectDto r) => $"{r.X},{r.Y},{r.W},{r.H},{r.Text}";

    private static string Art(ArtRef a) => $"{a.K}{a.A}/{a.B}/{a.C}/{a.D}/{a.S}";
}
