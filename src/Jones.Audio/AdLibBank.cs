namespace Jones.Audio;

/// <summary>
/// One FM operator, as the game's own instrument bank stores it.
/// </summary>
public struct AdLibOperator
{
    public byte KbScaleLevel;   // 0-3   -> register 0x40 bits 7:6
    public byte FrequencyMult;  // 0-15  -> register 0x20 bits 3:0
    public byte AttackRate;     // 0-15  -> register 0x60 bits 7:4
    public byte SustainLevel;   // 0-15  -> register 0x80 bits 7:4
    public bool EnvelopeType;   // sustaining envelope, register 0x20 bit 5
    public byte DecayRate;      // 0-15  -> register 0x60 bits 3:0
    public byte ReleaseRate;    // 0-15  -> register 0x80 bits 3:0
    public byte TotalLevel;     // 0-63, 0 = loudest
    public bool AmplitudeMod;   // tremolo, register 0x20 bit 7
    public bool Vibrato;        // register 0x20 bit 6
    public bool KbScaleRate;    // register 0x20 bit 4
    public byte WaveForm;       // 0-3   -> register 0xE0
}

public struct AdLibPatch
{
    public AdLibOperator Op0;   // modulator
    public AdLibOperator Op1;   // carrier
    public byte Feedback;       // 0-7
    public bool Algorithm;      // true = additive (AM), false = FM
}

/// <summary>
/// The instrument bank the game ships in <c>patch.003</c> (<c>assets/raw/patch/3.patch</c>).
///
/// WHY THIS FILE EXISTS AT ALL: the sound resources carry MT-32 program numbers, so
/// rendering their MIDI on a General MIDI synth plays the right notes with the wrong
/// instruments. But every resource also carries an AdLib arrangement (track type 0x00),
/// and patch.003 is the AdLib instrument definitions that arrangement indexes into. That
/// means the timbres do not have to be guessed — they are in the box.
///
/// LAYOUT, VERIFIED AGAINST THE BYTES rather than assumed:
///   1344 bytes = 48 instruments x 28 bytes.
///   Each instrument is two 13-byte operator records at +0 and +13, then two waveform
///   bytes at +26 and +27. Within an operator record:
///       +0  key-scale level      +1  frequency multiplier   +2  feedback (op0 only)
///       +3  attack rate          +4  sustain level          +5  envelope type
///       +6  decay rate           +7  release rate           +8  total level
///       +9  tremolo             +10  vibrato               +11  key-scale rate
///      +12  connection/algorithm (op0 only, inverted)
///
/// Every column's observed maximum lands exactly on its OPL field width across all 48
/// entries — key-scale level tops out at 3, feedback at 7, total level at 63, and the
/// flag columns hold only 0 and 1. The two bytes the layout says are unused (+2 and +12
/// of the SECOND operator, i.e. file offsets 15 and 25) are the only two that do not fit
/// a parameter range, and offset 15 takes just four distinct values across the whole
/// bank — junk, not data. Slots 3 and 22..31 are all-63 (silent) filler, and every
/// carrier's total level is 0 because the driver supplies it from note velocity.
///
/// Field assignment follows ScummVM's SCI AdLib driver, which is the same decoding the
/// original DOS driver performs.
/// </summary>
public sealed class AdLibBank
{
    public const int PatchCount = 96;
    public const int FileSize = 1344;

    private readonly AdLibPatch[] _patches;

    public AdLibPatch this[int index] => _patches[index];

    private AdLibBank(AdLibPatch[] patches) => _patches = patches;

    /// <summary>True when <paramref name="program"/> names a real instrument slot.</summary>
    public bool IsValid(int program) => program >= 0 && program < PatchCount;

    public static AdLibBank Load(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != FileSize)
            throw new InvalidDataException(
                $"patch.003 should be {FileSize} bytes (48 instruments x 28); got {data.Length}.");

        var patches = new AdLibPatch[PatchCount];
        for (var i = 0; i < 48; i++)
            patches[i] = Decode(data, i * 28);

        // The original driver pads a 1344-byte bank out to 96 entries with zeroes, so a
        // program change above 47 selects a silent instrument instead of reading off the
        // end. Programs 48..95 stay at their default (all-zero) value here for the same
        // reason. The game does use a program number outside 0..47 — 0x7F — but only on
        // MIDI channel 15, where it is the loop marker rather than an instrument.

        return new AdLibBank(patches);
    }

    private static AdLibPatch Decode(byte[] d, int at)
    {
        var p = new AdLibPatch
        {
            Op0 = DecodeOperator(d, at),
            Op1 = DecodeOperator(d, at + 13),
            Feedback = (byte)(d[at + 2] & 0x07),
            // Note the inversion: the stored byte is 1 for every instrument in this bank,
            // so every one of them is a true FM (modulator -> carrier) patch and only the
            // carrier gets velocity-scaled.
            Algorithm = d[at + 12] == 0,
        };
        p.Op0.WaveForm = (byte)(d[at + 26] & 0x03);
        p.Op1.WaveForm = (byte)(d[at + 27] & 0x03);
        return p;
    }

    private static AdLibOperator DecodeOperator(byte[] d, int op) => new()
    {
        KbScaleLevel  = (byte)(d[op + 0] & 0x03),
        FrequencyMult = (byte)(d[op + 1] & 0x0F),
        AttackRate    = (byte)(d[op + 3] & 0x0F),
        SustainLevel  = (byte)(d[op + 4] & 0x0F),
        EnvelopeType  = d[op + 5] != 0,
        DecayRate     = (byte)(d[op + 6] & 0x0F),
        ReleaseRate   = (byte)(d[op + 7] & 0x0F),
        TotalLevel    = (byte)(d[op + 8] & 0x3F),
        AmplitudeMod  = d[op + 9] != 0,
        Vibrato       = d[op + 10] != 0,
        KbScaleRate   = d[op + 11] != 0,
    };
}
