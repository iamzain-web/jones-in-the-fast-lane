using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jones.Audio;

namespace Jones.Tests;

/// <summary>
/// Checks on the sound path, written because nobody can hear a unit test.
///
/// The claims worth pinning down are: the instrument bank really is laid out the way the
/// AdLib format says (so the timbres are the game's and not a guess), the sequencer runs
/// at SCI's 60 ticks a second, the synthesiser produces the pitches the driver's own
/// frequency table asks for, and every one of the 34 resources actually makes a sound
/// rather than silence.
/// </summary>
public class SciAudioTests
{
    private static string AssetRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "assets");
            if (Directory.Exists(Path.Combine(candidate, "raw", "sound"))) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("could not find assets/raw/sound above the test binary");
    }

    private static SciSoundLibrary Library() => SciSoundLibrary.FromAssetRoot(AssetRoot())!;

    /// <summary>Every sound resource the game owns.</summary>
    public static readonly int[] AllResources =
    [
        5, 6, 7, 8, 9, 10, 20, 21, 23, 25, 27, 28, 29, 30, 31, 32, 34, 35, 36, 37,
        38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 100,
    ];

    public static IEnumerable<object[]> EveryResource => AllResources.Select(n => new object[] { n });

    // ------------------------------------------------------------------
    // The instrument bank
    // ------------------------------------------------------------------

    [Fact]
    public void PatchBankIsFortyEightInstrumentsOfTwentyEightBytes()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AssetRoot(), "raw", "patch", "3.patch"));
        Assert.Equal(1344, bytes.Length);
        Assert.Equal(0, bytes.Length % 28);
        Assert.Equal(48, bytes.Length / 28);
    }

    /// <summary>
    /// The layout claim, tested rather than assumed. If patch.003 were something other
    /// than an AdLib bank, its bytes would not all sit inside the OPL field widths that
    /// the 13-byte operator record assigns them: key-scale level is two bits, feedback is
    /// three, total level is six, and seven of the columns are single-bit flags.
    ///
    /// Bytes 15 and 25 are excluded because the format does not use them — feedback and
    /// the algorithm bit are read from the FIRST operator's record only. Byte 15 in
    /// particular takes four distinct values across the whole bank, which is what makes it
    /// padding rather than a parameter.
    /// </summary>
    [Fact]
    public void EveryBankByteFitsTheOplFieldItIsDecodedInto()
    {
        var d = File.ReadAllBytes(Path.Combine(AssetRoot(), "raw", "patch", "3.patch"));

        //            ksl mult  fb  atk  sus  eg  dec  rel   tl  am  vib ksr conn
        int[] limits = [3, 15, 7, 15, 15, 1, 15, 15, 63, 1, 1, 1, 1];

        for (var inst = 0; inst < 48; inst++)
        {
            for (var op = 0; op < 2; op++)
            {
                for (var f = 0; f < 13; f++)
                {
                    var column = op * 13 + f;
                    if (op == 1 && (f == 2 || f == 12)) continue;   // unused in this format

                    var value = d[inst * 28 + column];
                    Assert.True(value <= limits[f],
                        $"instrument {inst} byte {column} = {value}, over the {limits[f]} " +
                        "its OPL field can hold - the 48 x 28 AdLib layout does not hold");
                }
            }

            Assert.True(d[inst * 28 + 26] <= 3, $"instrument {inst} waveform 0 out of range");
            Assert.True(d[inst * 28 + 27] <= 3, $"instrument {inst} waveform 1 out of range");
        }
    }

    /// <summary>
    /// The bank's carriers all sit at total level 0 because the driver supplies the level
    /// from note velocity. If this ever stops holding, the velocity path is reading the
    /// wrong operator.
    /// </summary>
    [Fact]
    public void BankCarriersLeaveTheirLevelToTheDriver()
    {
        var bank = Library().Bank;
        var silentFillers = 0;

        for (var i = 0; i < 48; i++)
        {
            var p = bank[i];
            if (p.Op0.TotalLevel == 63 && p.Op1.TotalLevel == 63) { silentFillers++; continue; }
            Assert.True(p.Op1.TotalLevel <= 2,
                $"instrument {i} carrier level {p.Op1.TotalLevel} is not driver-supplied");
        }

        // Slot 3 and slots 22..31 are unused filler in this bank.
        Assert.Equal(11, silentFillers);
    }

    [Fact]
    public void BankInstrumentsAreAllFmNotAdditive()
    {
        var bank = Library().Bank;
        for (var i = 0; i < 48; i++)
            Assert.False(bank[i].Algorithm, $"instrument {i} decoded as additive");
    }

    // ------------------------------------------------------------------
    // Resource parsing
    // ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(EveryResource))]
    public void EveryResourceParsesToAnAdLibArrangement(int number)
    {
        var res = Library().Get(number);
        Assert.NotNull(res);
        Assert.NotEmpty(res!.Events);
        Assert.True(res.DurationTicks > 0);
    }

    /// <summary>
    /// Programs must index the 48-instrument bank. The single exception is 0x7F, and only
    /// on the control channel, where it is the loop marker — which the parser consumes, so
    /// it must never reach the event stream.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryResource))]
    public void ProgramChangesStayInsideTheBank(int number)
    {
        var res = Library().Get(number)!;

        foreach (var e in res.Events)
        {
            if ((e.Status & 0xF0) != 0xC0) continue;
            var channel = e.Status & 0x0F;
            if (channel == SciSoundResource.ControlChannel) continue;
            Assert.True(e.Data1 < 48,
                $"resource {number} selects instrument {e.Data1}, outside the 48 the bank holds");
        }

        Assert.DoesNotContain(res.Events,
            e => (e.Status & 0xF0) == 0xC0
                 && (e.Status & 0x0F) == SciSoundResource.ControlChannel
                 && e.Data1 == 0x7F);
    }

    // ------------------------------------------------------------------
    // The other four device tracks
    //
    // Every one of the 34 resources carries FIVE arrangements, one per sound card Sierra
    // shipped a driver for, and only the AdLib one is played. These tests exist because the
    // parser now takes the track type: they pin the identification down against the bytes,
    // so that a claim about which track is which can be checked rather than believed.
    //
    // The identification comes from the game's own files, not from a remembered table:
    //   patch.003  1344 bytes, 48 x 28 AdLib instruments      -> 0x00 AdLib
    //   patch.101    610 bytes, CMS.DRV "Game Blaster Card"   -> 0x09 Game Blaster
    //   patch.001  12953 bytes, opening "Jones-InThe-FastLane"
    //              then 10-character Roland timbre names      -> 0x0C MT-32
    //   STD.DRV    "IBM PC or Compatible Internal Speaker"    -> 0x12, one voice
    //   TANDY3V.DRV "Tandy Three-voice Only"                  -> 0x13, three voices
    // ------------------------------------------------------------------

    private static byte[] RawSound(int number) =>
        File.ReadAllBytes(Path.Combine(AssetRoot(), "raw", "sound", $"{number}.sound"));

    private static int NoteChannels(SciSoundResource res) =>
        res.Events.Where(e => (e.Status & 0xF0) == 0x90 && e.Data2 > 0)
                  .Select(e => e.Status & 0x0F).Distinct().Count();

    [Theory]
    [MemberData(nameof(EveryResource))]
    public void EveryResourceCarriesAllFiveDeviceArrangements(int number)
    {
        var bytes = RawSound(number);

        foreach (var track in new[]
                 {
                     SciSoundResource.TrackAdLib, SciSoundResource.TrackGameBlaster,
                     SciSoundResource.TrackMt32, SciSoundResource.TrackPcSpeaker,
                     SciSoundResource.TrackTandy,
                 })
        {
            var res = SciSoundResource.Load(bytes, track);
            Assert.True(res.Events.Count > 0,
                $"resource {number} track 0x{track:X2} decoded to nothing");
            Assert.True(res.DurationTicks > 0,
                $"resource {number} track 0x{track:X2} has no duration");
        }
    }

    /// <summary>
    /// The capability argument, as an assertion. A one-voice device gets a one-voice part
    /// and a three-voice device gets at most three; that is what identifies 0x12 as the
    /// internal speaker and 0x13 as the Tandy, and it holds in all 34 resources.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryResource))]
    public void TheNarrowTracksFitTheCardsTheyAreNamedFor(int number)
    {
        var bytes = RawSound(number);

        Assert.Equal(1, NoteChannels(SciSoundResource.Load(bytes, SciSoundResource.TrackPcSpeaker)));
        Assert.InRange(NoteChannels(SciSoundResource.Load(bytes, SciSoundResource.TrackTandy)), 1, 3);
    }

    /// <summary>
    /// The MT-32 track is the fullest arrangement in the file — never fewer parts than the
    /// AdLib one, and the same written length.
    ///
    /// This is the finding that would matter if the port ever wanted better-sounding music
    /// out of the game's OWN data: 0x0C is what the composer wrote for the good hardware
    /// and 0x00 is the cut-down version of it. Nothing acts on that today; the AdLib track
    /// is still the one played, and the original-music work went in a different direction.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryResource))]
    public void TheMt32TrackIsNeverPoorerThanTheAdLibOne(int number)
    {
        // ...with ONE exception, and it is the exception that proves what the resource is
        // for. Resource 100 is the fallback the game swaps in when the sound device cannot
        // manage four voices — `(if (<= (DoSound sndGET_POLYPHONY) 3) (aSong number: 100)
        // …)`, Main.sc:1188-1191 — so it exists FOR the weak devices, and its MT-32 track
        // is the one that was left thin, at one part against the AdLib track's two.
        if (number == 100) return;

        var bytes = RawSound(number);
        var adlib = SciSoundResource.Load(bytes, SciSoundResource.TrackAdLib);
        var mt32 = SciSoundResource.Load(bytes, SciSoundResource.TrackMt32);

        Assert.True(NoteChannels(mt32) >= NoteChannels(adlib),
            $"resource {number}: MT-32 has {NoteChannels(mt32)} parts, AdLib has {NoteChannels(adlib)}");
        Assert.Equal(adlib.DurationTicks, mt32.DurationTicks);
        Assert.Equal(adlib.LoopTick, mt32.LoopTick);
    }

    /// <summary>
    /// The six resources where the MT-32 arrangement carries a part the AdLib one does not.
    /// Written down as a list rather than a count so that a change to the parser shows up
    /// as "resource 39 lost its extra part", not as "6 became 5".
    /// </summary>
    [Fact]
    public void SixResourcesHaveAnExtraPartOnTheMt32Track()
    {
        var richer = AllResources.Where(n =>
        {
            var bytes = RawSound(n);
            return NoteChannels(SciSoundResource.Load(bytes, SciSoundResource.TrackMt32)) >
                   NoteChannels(SciSoundResource.Load(bytes, SciSoundResource.TrackAdLib));
        }).ToArray();

        Assert.Equal([5, 7, 35, 39, 41, 46], richer);
    }

    [Fact]
    public void AskingForADeviceTrackThatIsNotThereSaysSo()
    {
        // 0x06 exists in eight resources and carries only the control channel; 0x7A exists
        // nowhere at all.
        var ex = Assert.Throws<InvalidDataException>(() => SciSoundResource.Load(RawSound(5), 0x7A));
        Assert.Contains("0x7A", ex.Message);
    }

    [Fact]
    public void ElevenResourcesMarkALoopPoint()
    {
        var lib = Library();
        var marked = AllResources.Where(n => lib.Get(n)!.LoopTick > 0).ToArray();
        Assert.Equal([7, 9, 10, 25, 36, 38, 40, 43, 46, 47, 49], marked);
    }

    [Fact]
    public void ButtonClickIsTwoNotesOnOneChannel()
    {
        // WButton.sc:152-153 plays resource 23 on every press; it is a five-tick,
        // single-channel blip on instrument 47.
        var res = Library().Get(23)!;
        Assert.Equal(5, res.DurationTicks);

        var noteOns = res.Events.Count(e => (e.Status & 0xF0) == 0x90 && e.Data2 > 0);
        Assert.Equal(2, noteOns);
        Assert.Single(res.Events.Where(e => (e.Status & 0xF0) == 0xC0).Select(e => e.Data1).Distinct());
        Assert.Contains(res.Events, e => (e.Status & 0xF0) == 0xC0 && e.Data1 == 47);
    }

    // ------------------------------------------------------------------
    // Synthesis
    // ------------------------------------------------------------------

    private static short[] RenderOnce(SciSoundLibrary lib, SciSoundResource res, double extraSeconds)
    {
        var engine = new SciSoundEngine(lib.Bank);
        engine.PlayEffect(res);   // one-shot: no looping, so the render terminates

        var total = (int)((res.DurationTicks / 60.0 + extraSeconds) * SciSoundEngine.SampleRate);
        var buffer = new short[total];
        var pos = 0;
        while (pos < total)
        {
            var take = Math.Min(4096, total - pos);
            if (!engine.Render(buffer.AsSpan(pos, take))) break;
            pos += take;
        }

        return buffer[..pos];
    }

    /// <summary>
    /// The headline claim: these resources make sound. Rendering each one and measuring
    /// its peak is the only way to say so without hearing it.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryResource))]
    public void EveryResourceRendersAudibleAudio(int number)
    {
        var lib = Library();
        var res = lib.Get(number)!;
        var pcm = RenderOnce(lib, res, extraSeconds: 1.0);

        Assert.NotEmpty(pcm);

        var peak = 0;
        long sumSquares = 0;
        foreach (var s in pcm)
        {
            peak = Math.Max(peak, Math.Abs((int)s));
            sumSquares += (long)s * s;
        }
        var rms = Math.Sqrt(sumSquares / (double)pcm.Length);

        // A single OPL2 channel tops out near 4085 in this emulator's output scale, so
        // even the quietest one-voice effect clears 1000 comfortably.
        Assert.True(peak > 1000, $"resource {number} peaked at only {peak} - effectively silent");
        Assert.True(rms > 50, $"resource {number} has RMS {rms:F1} - essentially silence");
    }

    /// <summary>
    /// The sequencer clock. A resource of N ticks must take N/60 seconds to play, which is
    /// what ties the arrangement to the tempo the game was written at.
    /// </summary>
    [Theory]
    [InlineData(34)]    // 1595 ticks, the Security Apartments bed
    [InlineData(100)]   // 7229 ticks, the longest resource
    [InlineData(44)]    // 153 ticks, a short sting
    public void RenderedLengthMatchesTheResourceTickCount(int number)
    {
        var lib = Library();
        var res = lib.Get(number)!;

        var engine = new SciSoundEngine(lib.Bank);
        engine.PlayEffect(res);

        // Render exactly the written length and check the last event has been dispatched
        // by measuring where the audio actually stops once we keep going.
        var expectedSeconds = res.DurationTicks / 60.0;
        var buffer = new short[(int)((expectedSeconds + 6) * SciSoundEngine.SampleRate)];
        var pos = 0;
        while (pos < buffer.Length)
        {
            var take = Math.Min(4096, buffer.Length - pos);
            if (!engine.Render(buffer.AsSpan(pos, take))) break;
            pos += take;
        }

        var actualSeconds = pos / (double)SciSoundEngine.SampleRate;

        // Playback runs for the written length plus the release tail, which the engine
        // trims once the chip goes quiet. Anything shorter than the written length means
        // the clock is running fast.
        Assert.True(actualSeconds >= expectedSeconds,
            $"resource {number}: stopped after {actualSeconds:F2}s, arrangement is {expectedSeconds:F2}s");
        Assert.True(actualSeconds < expectedSeconds + 5.5,
            $"resource {number}: ran {actualSeconds:F2}s for a {expectedSeconds:F2}s arrangement");
    }

    /// <summary>
    /// Pitch, measured rather than trusted. The driver's own table puts MIDI note 60 at
    /// F-number 0x157 in block 4, which on a 49716 Hz OPL is 343 * 49716 / 2^16 = 260.2 Hz
    /// — the table is very slightly flat of the 261.6 Hz of equal temperament, and that
    /// flatness is the original's, not an error here.
    /// </summary>
    [Fact]
    public void MiddleCComesOutAtTheFrequencyTheSciTableAsksFor()
    {
        var lib = Library();
        var chip = new NukedOPL3Sharp.Opl3Chip();
        chip.Reset((uint)SciSoundEngine.SampleRate);
        var driver = new SciAdLibDriver(chip, lib.Bank);

        driver.Send(0xB0, 0x4B, 1);      // channel 0 gets one chip voice
        driver.Send(0xB0, 0x07, 0x7F);   // full channel volume
        driver.Send(0xC0, 16, 0);        // a sustained instrument from the bank
        driver.Send(0x90, 60, 0x7F);     // middle C

        var frames = SciSoundEngine.SampleRate / 2;
        var stereo = new short[frames * 2];
        chip.GenerateStream(stereo);

        // Count positive-going zero crossings over the middle of the note, past the attack.
        var from = frames / 4;
        var to = frames * 3 / 4;
        var crossings = 0;
        for (var i = from + 1; i < to; i++)
            if (stereo[(i - 1) * 2] <= 0 && stereo[i * 2] > 0) crossings++;

        var hz = crossings / ((to - from) / (double)SciSoundEngine.SampleRate);

        Assert.InRange(hz, 255.0, 266.0);
    }

    /// <summary>
    /// The claim this whole approach rests on: the instrument the chip is actually set up
    /// with is the one in the game's bank, byte for byte.
    ///
    /// The emulator keeps the decoded register fields per operator, so this reads them
    /// back after a program change and compares every one against
    /// <see cref="AdLibBank"/>. If the operator stride, the field order, the waveform
    /// bytes or the feedback/algorithm split were wrong, this is where it would show.
    ///
    /// The carrier's total level is the one field that legitimately differs: the driver
    /// overwrites it with the note's velocity, which is exactly why the bank stores 0
    /// there. It is checked separately against the velocity formula.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(47)]    // the button click's instrument
    public void TheChipIsSetUpWithExactlyTheInstrumentTheBankHolds(int program)
    {
        var lib = Library();
        var chip = new NukedOPL3Sharp.Opl3Chip();
        var driver = new SciAdLibDriver(chip, lib.Bank);

        driver.Send(0xB0, 0x4B, 1);            // channel 0 gets chip voice 0
        driver.Send(0xB0, 0x07, 0x7F);
        driver.Send(0xC0, (byte)program, 0);
        driver.Send(0x90, 60, 0x7F);

        // Register writes reach the chip through its write buffer, a couple of samples
        // apart, so they have to be clocked in before they can be read back.
        var frame = new short[2];
        for (var i = 0; i < 512; i++) chip.GenerateStream(frame);

        var expected = lib.Bank[program];
        var channel = chip.Channels[0];
        var modulator = channel.Slotz[0];
        var carrier = channel.Slotz[1];

        void Check(string who, NukedOPL3Sharp.Opl3Operator got, AdLibOperator want)
        {
            Assert.Equal(want.KbScaleLevel, got.RegKeyScaleLevel);
            Assert.Equal(want.FrequencyMult, got.RegFrequencyMultiplier);
            Assert.Equal(want.AttackRate, got.RegAttackRate);
            Assert.Equal(want.DecayRate, got.RegDecayRate);
            Assert.Equal(want.ReleaseRate, got.RegReleaseRate);
            Assert.Equal(want.WaveForm, got.RegWaveformSelect);
            Assert.Equal(want.EnvelopeType ? 1 : 0, got.RegOperatorType);
            Assert.Equal(want.KbScaleRate ? 1 : 0, got.RegKeyScaleRate);
            Assert.Equal(want.Vibrato ? 1 : 0, got.RegVibrato);
            Assert.Equal(want.AmplitudeMod, got.TremoloEnabled);

            // The one field the chip does not store verbatim: a sustain level of 15 means
            // "maximum attenuation" on an OPL and is held internally as 31.
            Assert.Equal(want.SustainLevel == 15 ? 31 : want.SustainLevel, got.RegSustainLevel);

            // Total level holds for BOTH operators here, and for the carrier that is a
            // result rather than a copy. The driver overwrites the carrier's level with
            // the note's velocity, and at full channel volume, full velocity and full
            // master volume its formula collapses to 63 - (63 - bankLevel), which is the
            // bank's level again. Anything else would mean the velocity path is wrong.
            Assert.True(want.TotalLevel == got.RegTotalLevel,
                $"{who} total level {got.RegTotalLevel} != bank's {want.TotalLevel}");
        }

        Check("modulator", modulator, expected.Op0);
        Check("carrier", carrier, expected.Op1);

        Assert.Equal(expected.Feedback, channel.Feedback);
        Assert.Equal(expected.Algorithm ? 1 : 0, channel.Connection);

        // Middle C: the driver's own table asks for F-number 0x157 in block 4.
        Assert.Equal(0x157, channel.FNumber);
        Assert.Equal(4, channel.Block);
    }

    [Fact]
    public void ANoteOnlySoundsOnceTheChannelHasBeenGivenAVoice()
    {
        var lib = Library();
        var chip = new NukedOPL3Sharp.Opl3Chip();
        var driver = new SciAdLibDriver(chip, lib.Bank);

        // Controller 0x4B is how SCI hands chip voices to a MIDI channel. Without it the
        // channel owns nothing and the note is dropped - which is exactly what happens to
        // the control channel, and why channel 15 never makes a sound.
        driver.Send(0xC0, 5, 0);
        driver.Send(0x90, 60, 0x7F);
        Assert.Equal(0, driver.SoundingVoices);

        driver.Send(0xB0, 0x4B, 2);
        Assert.Equal(2, driver.MappedVoices);

        driver.Send(0x90, 60, 0x7F);
        Assert.Equal(1, driver.SoundingVoices);

        driver.Send(0x80, 60, 0);
        Assert.Equal(0, driver.SoundingVoices);
    }

    [Fact]
    public void TheChipNeverHandsOutMoreThanItsNineVoices()
    {
        var lib = Library();
        var driver = new SciAdLibDriver(new NukedOPL3Sharp.Opl3Chip(), lib.Bank);

        // Ask for far more than the card has across several channels.
        for (var channel = 0; channel < 6; channel++)
            driver.Send((byte)(0xB0 | channel), 0x4B, 4);

        Assert.Equal(SciAdLibDriver.Voices, driver.MappedVoices);

        for (var channel = 0; channel < 6; channel++)
            for (var note = 60; note < 64; note++)
                driver.Send((byte)(0x90 | channel), (byte)note, 0x7F);

        Assert.True(driver.SoundingVoices <= SciAdLibDriver.Voices);
    }

    /// <summary>
    /// Voice stealing must restart the envelope of the note that takes the voice.
    ///
    /// This is the subtlest thing in the whole sound path. The driver steals a voice by
    /// keying it off and immediately keying it on again; if those two register writes land
    /// at the same emulated instant the chip never enters its release state and the new
    /// note inherits the old one's decaying envelope. Every repeat then comes out quieter
    /// than the last until the part fades away entirely, which is what happened before the
    /// driver was moved onto the emulator's buffered write path. Real hardware cannot hit
    /// that case because writing two registers takes longer than an envelope clock.
    /// </summary>
    [Fact]
    public void AStolenVoiceComesBackAtFullLevelEveryTime()
    {
        var lib = Library();
        var chip = new NukedOPL3Sharp.Opl3Chip();
        var driver = new SciAdLibDriver(chip, lib.Bank);

        driver.Send(0xB1, 0x4B, 1);      // ONE voice, so the second note must steal it
        driver.Send(0xB1, 0x07, 0x7F);
        driver.Send(0xC1, 47, 0);        // the button click's instrument

        var frame = new short[2];
        var peaks = new List<int>();

        for (var repeat = 0; repeat < 6; repeat++)
        {
            driver.Send(0x91, 0x44, 0x7F);
            driver.Send(0x91, 0x48, 0x7F);   // steals the voice from the first note

            var peak = 0;
            for (var tick = 0; tick < 2; tick++)
            {
                for (var s = 0; s < SciSoundEngine.SampleRate / 60; s++)
                {
                    chip.GenerateStream(frame);
                    peak = Math.Max(peak, Math.Abs((int)frame[0]));
                }
                driver.Tick();
            }
            peaks.Add(peak);

            driver.Send(0x91, 0x48, 0x00);
        }

        Assert.All(peaks, p => Assert.True(p > 3000,
            $"a stolen voice came back at {p}: peaks were [{string.Join(", ", peaks)}]"));

        // And specifically: the last repeat is as loud as the first, not a fraction of it.
        Assert.True(peaks[^1] > peaks[0] * 0.9,
            $"level decayed across repeats: [{string.Join(", ", peaks)}]");
    }

    /// <summary>
    /// A bed loops; an effect does not. Rendering well past the end of each proves it,
    /// because a looping slot never reports itself idle.
    /// </summary>
    [Fact]
    public void BedsLoopAndEffectsStop()
    {
        var lib = Library();
        var click = lib.Get(23)!;

        var effect = new SciSoundEngine(lib.Bank);
        effect.PlayEffect(click);
        var buffer = new short[SciSoundEngine.SampleRate];   // one second, click is 0.08s
        effect.Render(buffer);
        Assert.False(effect.EffectPlaying);

        var bed = new SciSoundEngine(lib.Bank);
        bed.PlayMusic(click, loop: true);
        for (var i = 0; i < 4; i++) bed.Render(buffer);
        Assert.True(bed.MusicPlaying);

        // ...and a loop keeps making sound rather than dying into silence.
        var peak = 0;
        foreach (var s in buffer) peak = Math.Max(peak, Math.Abs((int)s));
        Assert.True(peak > 500, $"looping bed went quiet (peak {peak})");
    }

    /// <summary>
    /// `(gASong fade:)` is `(DoSound sndFADE self 0 25 10 1)` — 13 steps of 25 ticks, so a
    /// full fade takes 325 ticks and then stops the sound. Anything much shorter is a cut
    /// pretending to be a fade.
    /// </summary>
    [Fact]
    public void StopMusicFadesOverTheDurationTheScriptAsksFor()
    {
        var lib = Library();
        var engine = new SciSoundEngine(lib.Bank);
        engine.PlayMusic(lib.Get(34)!, loop: true);

        var buffer = new short[SciSoundEngine.SampleRate / 10];
        engine.Render(buffer);
        Assert.True(engine.MusicPlaying);

        engine.StopMusic();

        var rendered = 0;
        while (engine.MusicPlaying && rendered < SciSoundEngine.SampleRate * 10)
        {
            engine.Render(buffer);
            rendered += buffer.Length;
        }

        var seconds = rendered / (double)SciSoundEngine.SampleRate;
        Assert.False(engine.MusicPlaying);
        Assert.InRange(seconds, 4.5, 6.5);   // 325 ticks at 60 Hz is 5.42 s
    }

    // ------------------------------------------------------------------
    // stop: / pause: / a second effect slot / a looping effect
    //
    // These are the four things the scripts ask for that a `play` and a `fade` cannot
    // express. Each is measured in the rendered samples rather than asserted about the
    // engine's own bookkeeping, because the bookkeeping is what would be wrong.
    // ------------------------------------------------------------------

    private static int Peak(ReadOnlySpan<short> b)
    {
        var p = 0;
        foreach (var s in b) p = Math.Max(p, Math.Abs((int)s));
        return p;
    }

    /// <summary>Renders <paramref name="seconds"/> in buffer-sized bites and returns the peak.</summary>
    private static int RenderPeak(SciSoundEngine engine, double seconds)
    {
        var buffer = new short[SciSoundEngine.SampleRate / 20];   // 50 ms, as the device uses
        var left = (int)(seconds * SciSoundEngine.SampleRate);
        var peak = 0;
        while (left > 0)
        {
            var take = Math.Min(buffer.Length, left);
            engine.Render(buffer.AsSpan(0, take));
            peak = Math.Max(peak, Peak(buffer.AsSpan(0, take)));
            left -= take;
        }
        return peak;
    }

    /// <summary>
    /// `(gASong stop:)` is `(DoSound sndSTOP self)` (Sound.sc:73-81) and `(gASong fade:)`
    /// is `(DoSound sndFADE self 0 25 10 1)` — two different calls that the scripts use in
    /// two different places, and the port used to render both as the fade. A cut is over
    /// inside one buffer; the fade takes the five and a half seconds the other test pins.
    /// </summary>
    [Fact]
    public void CutMusicStopsInsideOneBufferWhereFadeTakesFiveSeconds()
    {
        var lib = Library();
        var engine = new SciSoundEngine(lib.Bank);
        engine.PlayMusic(lib.Get(34)!, loop: true);

        Assert.True(RenderPeak(engine, 0.5) > 500, "the bed never started");

        engine.CutMusic();
        Assert.False(engine.MusicPlaying);

        // Nothing at all afterwards, not a diminishing tail.
        var buffer = new short[SciSoundEngine.SampleRate / 20];
        Assert.False(engine.Render(buffer));
        Assert.Equal(0, Peak(buffer));
    }

    /// <summary>
    /// `(gASong pause: 1)` / `(gASong pause: 0)` — Sound.sc:83-88. The bed must go silent
    /// where it stands and come back, rather than stop and restart: the arrangement
    /// carries on from the same bar.
    /// </summary>
    [Fact]
    public void PausingTheSongSilencesItAndResumingBringsItBack()
    {
        var lib = Library();
        var engine = new SciSoundEngine(lib.Bank);
        engine.PlayMusic(lib.Get(34)!, loop: true);

        Assert.True(RenderPeak(engine, 0.5) > 500, "the bed never started");

        engine.PauseMusic();
        Assert.True(engine.MusicPaused);

        // Still the current bed — paused is not stopped.
        Assert.True(engine.MusicPlaying);

        var buffer = new short[SciSoundEngine.SampleRate / 2];
        Assert.False(engine.Render(buffer));
        Assert.Equal(0, Peak(buffer));

        engine.ResumeMusic();
        Assert.False(engine.MusicPaused);
        Assert.True(RenderPeak(engine, 1.0) > 500, "the bed did not come back");
    }

    /// <summary>
    /// The ducking, end to end. `employment.sc:35-36` is
    ///
    ///     (gASong pause: 1)
    ///     (gASoundEffect play: 44 gASong)
    ///
    /// and the second argument is the sound's `client` (Sound.sc:49), which `Sound::check`
    /// cues when the sound ends (Sound.sc:118-127); `aSong::cue` is `(self pause: 0)`
    /// (`Main.sc:1267-1270`). So the bed comes back on its own, with nothing else asked
    /// to notice that the sting has finished.
    /// </summary>
    [Fact]
    public void AStingCuesThePausedBedBackWhenItEnds()
    {
        var lib = Library();
        var engine = new SciSoundEngine(lib.Bank);
        engine.PlayMusic(lib.Get(43)!, loop: true);        // the Employment Office's bed
        RenderPeak(engine, 0.25);

        engine.PauseMusic();
        engine.PlayEffect(lib.Get(44)!, resumeMusicWhenDone: true);   // the refusal sting

        // While the sting plays the bed stays down, so everything heard is the sting.
        Assert.True(engine.MusicPaused);

        // Sting 44 is 153 ticks, about 2.6 s; render well past it.
        var buffer = new short[SciSoundEngine.SampleRate / 20];
        var rendered = 0;
        while (engine.EffectPlaying && rendered < SciSoundEngine.SampleRate * 12)
        {
            engine.Render(buffer);
            rendered += buffer.Length;
        }

        Assert.False(engine.EffectPlaying);
        Assert.False(engine.MusicPaused);
        Assert.True(RenderPeak(engine, 1.0) > 500, "the bed did not come back after the sting");
    }

    /// <summary>
    /// A sting REPLACED before it finishes never cues its client, because `Sound::play`
    /// assigns `client` unconditionally and a one-argument `play:` therefore clears it
    /// (Sound.sc:49). The consequence in the game is that clicking another button while
    /// the Employment Office's sting is playing leaves the bed down until the next
    /// `playBed:`. That is the original's behaviour and is kept deliberately.
    /// </summary>
    [Fact]
    public void ReplacingAStingLosesItsCueAndLeavesTheBedDown()
    {
        var lib = Library();
        var engine = new SciSoundEngine(lib.Bank);
        engine.PlayMusic(lib.Get(43)!, loop: true);
        RenderPeak(engine, 0.25);

        engine.PauseMusic();
        engine.PlayEffect(lib.Get(44)!, resumeMusicWhenDone: true);
        RenderPeak(engine, 0.25);

        engine.PlayEffect(lib.Get(23)!);         // a button click takes the slot

        var buffer = new short[SciSoundEngine.SampleRate / 20];
        var rendered = 0;
        while (engine.EffectPlaying && rendered < SciSoundEngine.SampleRate * 12)
        {
            engine.Render(buffer);
            rendered += buffer.Length;
        }

        Assert.False(engine.EffectPlaying);
        Assert.True(engine.MusicPaused, "the lost cue brought the bed back anyway");

        // ...and the next bed releases it, which is how the game recovers.
        engine.PlayMusic(lib.Get(43)!, loop: true);
        Assert.False(engine.MusicPaused);
        Assert.True(RenderPeak(engine, 0.5) > 500);
    }

    /// <summary>
    /// `(gASoundEffect loop: -1 play: 25)` and, twenty states later,
    /// `(gASoundEffect loop: 1)` — `lottoScript.sc:43` and `:232`. An effect must be able
    /// to keep sounding indefinitely and then be told to play out its current pass rather
    /// than being cut off, which is what putting the loop COUNT back to one does.
    ///
    /// Measured on the button click rather than on the lotto machine, because the click is
    /// five ticks long: a whole second of it is a dozen passes, and a slot that failed to
    /// loop would have gone quiet and retired long before.
    /// </summary>
    [Fact]
    public void ALoopingEffectRunsUntilItsLoopCountIsPutBack()
    {
        var lib = Library();
        var engine = new SciSoundEngine(lib.Bank);
        engine.PlayEffect(lib.Get(23)!, loop: true);

        Assert.True(RenderPeak(engine, 1.0) > 500, "the looping effect went quiet");
        Assert.True(engine.EffectPlaying, "a five-tick effect outlived its loop");

        engine.EndEffectLoop();

        var buffer = new short[SciSoundEngine.SampleRate / 20];
        var rendered = 0;
        while (engine.EffectPlaying && rendered < SciSoundEngine.SampleRate * 10)
        {
            engine.Render(buffer);
            rendered += buffer.Length;
        }

        Assert.False(engine.EffectPlaying);
    }

    /// <summary>
    /// A fact about the lotto worth writing down, because it decides what the wiring has
    /// to do rather than the other way round: resource 25 is SEVEN AND A HALF SECONDS,
    /// which is longer than the sequence that plays it.
    ///
    /// `lottoScript` runs twenty-one states, and the only wait it states outright is
    /// `(proc0_3 240)` at `:215` — 240 ticks, four seconds. So `(gASoundEffect loop: -1)`
    /// at `:43` is a guard against the animation outlasting the sound, not something a
    /// player ever hears repeat, and `(gASoundEffect loop: 1)` at `:232` lands while the
    /// machine is still on its first pass. The sound then plays out in full, over the
    /// board, until something else takes the effect slot.
    /// </summary>
    [Fact]
    public void TheLottoMachineOutlastsTheSequenceThatPlaysIt()
    {
        var res = Library().Get(25)!;
        var seconds = res.DurationTicks / 60.0;

        Assert.True(seconds > 4.0,
            $"resource 25 is {seconds:F1}s, now shorter than lottoScript's own 4 s wait - " +
            "the loop would be audible and the wiring's assumption no longer holds");

        // And it is one of the eleven that marks a loop point, so looping it would jump to
        // that mark rather than back to tick 0 if it ever did wrap.
        Assert.True(res.LoopTick > 0);
    }

    /// <summary>
    /// `gASoundEffect2` exists because `gASoundEffect` is busy. `room1.sc:1497-1500`
    /// chimes the end of the week on the second object at the moment the hours reach 60 —
    /// which is inside the button press whose own click (resource 23) is still playing on
    /// the first. Both must be heard.
    /// </summary>
    [Fact]
    public void TheSecondEffectSlotSoundsOverTheFirst()
    {
        var lib = Library();
        var buffer = new short[SciSoundEngine.SampleRate / 4];

        var chimeOnly = new SciSoundEngine(lib.Bank);
        chimeOnly.PlayEffect2(lib.Get(29)!);
        chimeOnly.Render(buffer);
        var chimePeak = Peak(buffer);
        Assert.True(chimePeak > 1000, "the chime alone is silent");

        var both = new SciSoundEngine(lib.Bank);
        both.PlayEffect(lib.Get(23)!);           // the click that spent the last hour
        both.PlayEffect2(lib.Get(29)!);          // the chime over the top of it
        both.Render(buffer);

        Assert.True(both.Effect2Playing);
        Assert.True(Peak(buffer) >= chimePeak,
            "the click and the chime cancelled rather than mixing");
    }

    /// <summary>
    /// `winnerScript.sc:67-69` clears BOTH effect objects before the fanfare, and touches
    /// the song only to start it.
    /// </summary>
    [Fact]
    public void StopEffectsClearsBothSlotsAndLeavesTheSongPlaying()
    {
        var lib = Library();
        var engine = new SciSoundEngine(lib.Bank);

        engine.PlayMusic(lib.Get(5)!, loop: true);
        engine.PlayEffect(lib.Get(25)!, loop: true);
        engine.PlayEffect2(lib.Get(29)!);
        RenderPeak(engine, 0.1);

        engine.StopEffects();

        Assert.False(engine.EffectPlaying);
        Assert.False(engine.Effect2Playing);
        Assert.True(engine.MusicPlaying);
        Assert.True(RenderPeak(engine, 0.5) > 500, "the song was stopped along with the effects");
    }

    /// <summary>
    /// `sndMASTER_VOLUME` runs 0..15 and the driver folds it into the note velocity, so 0
    /// is the bottom of that scale rather than a mute — the original behaves the same way,
    /// because turning sound OFF is `soundOn`, not a volume of zero. What has to hold is
    /// that the bottom of the scale is dramatically quieter than the top.
    /// </summary>
    [Fact]
    public void TheMasterVolumeScaleReachesFromLoudToNearlyNothing()
    {
        var lib = Library();

        static int PeakAt(SciSoundLibrary lib, int volume)
        {
            var engine = new SciSoundEngine(lib.Bank) { MasterVolume = volume };
            engine.PlayMusic(lib.Get(39)!, loop: true);
            var buffer = new short[SciSoundEngine.SampleRate / 2];
            engine.Render(buffer);
            var peak = 0;
            foreach (var s in buffer) peak = Math.Max(peak, Math.Abs((int)s));
            return peak;
        }

        var loud = PeakAt(lib, 15);
        var quiet = PeakAt(lib, 0);

        Assert.True(loud > 2000, $"full volume only reached {loud}");
        Assert.True(quiet * 8 < loud, $"volume 0 ({quiet}) is not far below volume 15 ({loud})");
    }

    /// <summary>The sound switch, which is what `Enabled = false` reaches for.</summary>
    [Fact]
    public void StoppingEverythingLeavesTrueSilence()
    {
        var lib = Library();
        var engine = new SciSoundEngine(lib.Bank);
        engine.PlayMusic(lib.Get(39)!, loop: true);
        engine.PlayEffect(lib.Get(23)!);

        var buffer = new short[SciSoundEngine.SampleRate / 4];
        engine.Render(buffer);

        engine.StopAll();
        Assert.False(engine.MusicPlaying);
        Assert.False(engine.EffectPlaying);

        Assert.False(engine.Render(buffer));
        foreach (var s in buffer) Assert.Equal(0, s);
    }

    [Fact]
    public void MusicAndAnEffectMixWithoutEitherSilencingTheOther()
    {
        var lib = Library();

        static int Peak(short[] b)
        {
            var p = 0;
            foreach (var s in b) p = Math.Max(p, Math.Abs((int)s));
            return p;
        }

        var buffer = new short[SciSoundEngine.SampleRate / 4];

        var musicOnly = new SciSoundEngine(lib.Bank);
        musicOnly.PlayMusic(lib.Get(39)!, loop: true);
        musicOnly.Render(buffer);
        var musicPeak = Peak(buffer);

        var both = new SciSoundEngine(lib.Bank);
        both.PlayMusic(lib.Get(39)!, loop: true);
        both.PlayEffect(lib.Get(23)!);
        both.Render(buffer);

        Assert.True(both.MusicPlaying);
        Assert.True(Peak(buffer) >= musicPeak,
            "the click quietened the bed instead of mixing over it");
    }

    /// <summary>
    /// The sound the interface depends on most. Rendered to a real WAV so it can be played
    /// by anyone who wants to check it by ear, and measured so the test itself does not
    /// have to.
    /// </summary>
    [Fact]
    public void ButtonClickRendersToAPlayableWav()
    {
        var lib = Library();
        var pcm = RenderOnce(lib, lib.Get(23)!, extraSeconds: 0.5);
        var wav = WavFile.Build(pcm, SciSoundEngine.SampleRate);

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal(pcm.Length * 2, BitConverter.ToInt32(wav, 40));
        Assert.Equal(SciSoundEngine.SampleRate, BitConverter.ToInt32(wav, 24));

        // Short: the arrangement is five ticks, so everything after is release tail.
        Assert.True(pcm.Length < SciSoundEngine.SampleRate,
            "the click should be well under a second");
    }
}
