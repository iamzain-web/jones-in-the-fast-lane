using System;
using System.IO;
using System.Linq;
using Jones.Audio;
using Jones.Audio.Original;

namespace Jones.Tests;

/// <summary>
/// The game's audio, which is now entirely this project's own.
///
/// <para>
/// THIS FILE REPLACES <c>AudioSwitchTests</c>. That file tested an A/B switch between
/// Sierra's AdLib arrangements and the original set, and a good half of it existed to prove
/// the AdLib path had not been disturbed — bit-identical channels, sample-for-sample mono
/// output, per-cue fallback to the chip. Those tests were guarding a promise that no longer
/// applies: the AdLib path, its driver, its instrument bank and the OPL emulator are gone,
/// and a test asserting that something matches deleted code is worse than no test.
/// </para>
///
/// <para>
/// What survives is everything that was never about the switch: that the soundtrack covers
/// every resource the scripts can reach, that music and effects actually sound, that a bed
/// ducked for a sting comes back, and that stopping really is silence.
/// </para>
/// </summary>
public class OriginalAudioTests
{
    private const int Rate = JonesAudioMixer.SampleRate;

    /// <summary>
    /// The 34 sound resources the game ships. Previously in <c>SciAudioTests</c>, which was
    /// deleted with the AdLib path; the list is game data and outlived the tests that used
    /// it to drive an OPL emulator.
    /// </summary>
    public static readonly int[] AllResources =
    [
        5, 6, 7, 8, 9, 10, 20, 21, 23, 25, 27, 28, 29, 30, 31, 32, 34, 35, 36, 37,
        38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 100,
    ];

    private static JonesAudioMixer Mixer() => new(new OriginalSoundBank(Rate));

    private static double Peak(ReadOnlySpan<float> pcm)
    {
        double p = 0;
        foreach (var s in pcm) p = Math.Max(p, Math.Abs(s));
        return p;
    }

    private static double Render(JonesAudioMixer mixer, double seconds)
    {
        var frames = (int)(seconds * Rate);
        var buffer = new float[frames * 2];
        var done = 0;
        double peak = 0;
        while (done < frames)
        {
            var take = Math.Min(2048, frames - done);
            var span = buffer.AsSpan(done * 2, take * 2);
            mixer.RenderStereo(span);
            peak = Math.Max(peak, Peak(span));
            done += take;
        }
        return peak;
    }

    // ------------------------------------------------------------------ coverage

    /// <summary>
    /// COMPLETE COVERAGE: every sound resource the game can actually reach has an original
    /// version, so nothing the player can hear is Sierra's music.
    ///
    /// <para>
    /// This mattered while the AdLib path still existed, because an uncovered resource
    /// silently fell back to it. It matters more now that the fallback is gone: an
    /// uncovered resource is SILENCE, and silence in a room is easy to miss.
    /// </para>
    ///
    /// <para>
    /// 28 AND 32 ARE EXCLUDED, and that is a finding rather than a convenience. Every
    /// `play:`, `playBed:` and `number` in BOTH decompilations reaches exactly 32 sound
    /// numbers, and neither 28 nor 32 is among them — no call site, no `UnLoad 132`, no
    /// Sound instance default. The game cannot play them. The reachable set is listed here
    /// rather than computed so a change to the scripts fails this test rather than quietly
    /// shrinking the number.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryResourceTheGameCanReachHasAnOriginalCue()
    {
        int[] reachable =
        [
            5, 6, 7, 8, 9, 10, 20, 21, 23, 25, 27, 29, 30, 31,
            34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 100,
        ];

        var bank = new OriginalSoundBank(Rate);
        var missing = reachable.Where(n => !bank.Has(n)).ToArray();

        Assert.True(missing.Length == 0,
            "these resources can be played by the scripts and have no original cue, so the " +
            "player would hear nothing at all: " + string.Join(", ", missing));

        Assert.False(bank.Has(28));
        Assert.False(bank.Has(32));

        Assert.Equal(AllResources.OrderBy(n => n), reachable.Concat([28, 32]).OrderBy(n => n));
    }

    [Fact]
    public void EveryResourceTheBankClaimsIsOneTheGameActuallyHas()
    {
        var bank = new OriginalSoundBank(Rate);
        Assert.NotEmpty(bank.Resources);

        foreach (var resource in bank.Resources)
        {
            Assert.Contains(resource, AllResources);
            Assert.True(bank.Music(resource) is not null || bank.Effect(resource) is not null,
                $"the bank claims resource {resource} but produces nothing for it");
        }
    }

    /// <summary>
    /// A resource the bank does not cover reports nothing rather than throwing. Resource 28
    /// is the case that actually exists: the game has it and nothing can play it.
    /// </summary>
    [Fact]
    public void AResourceTheBankDoesNotHaveReportsNothingRatherThanThrowing()
    {
        var bank = new OriginalSoundBank(Rate);
        Assert.False(bank.Has(28));
        Assert.Null(bank.Music(28));
        Assert.Null(bank.Effect(28));
    }

    // ------------------------------------------------------------------ playback

    [Fact]
    public void ABedPlaysAndIsAudible()
    {
        var mixer = Mixer();
        mixer.PlayMusic(39, loop: true);       // QT Clothing
        Assert.True(mixer.MusicPlaying);
        Assert.True(Render(mixer, 0.5) > 0.01, "the bed was silent");
    }

    /// <summary>
    /// The music really is stereo, which is why the audio seam was widened to two channels.
    /// </summary>
    [Fact]
    public void TheMusicIsStereo()
    {
        var mixer = Mixer();
        mixer.PlayMusic(5, loop: true);

        var buffer = new float[Rate * 2];
        var done = 0;
        while (done < buffer.Length)
        {
            var take = Math.Min(4096, buffer.Length - done);
            mixer.RenderStereo(buffer.AsSpan(done, take));
            done += take;
        }

        double difference = 0;
        for (var i = 0; i < buffer.Length; i += 2) difference += Math.Abs(buffer[i] - buffer[i + 1]);
        Assert.True(difference / (buffer.Length / 2) > 0.001, "the bed came out mono");
    }

    /// <summary>The button click — the sound the interface depends on most.</summary>
    [Fact]
    public void TheButtonClickIsAudible()
    {
        var mixer = Mixer();
        mixer.PlayEffect(23);
        Assert.True(Render(mixer, 0.5) > 0.01, "the click was silent");
    }

    /// <summary>
    /// EVERY STING PLAYED THROUGH AN EFFECT SLOT MUST SOUND, and this test exists because
    /// it did not.
    ///
    /// <para>
    /// The stings are SCORES, not the six synthesised Foley effects, and the scripts play
    /// several of them through `gASoundEffect` — `muggedByMarket.sc:31` is
    /// `(gASoundEffect play: 20)` and the five rentOffice sites play 44 the same way. The
    /// bank's effect lookup only knew the Foley six, so a sting returned null and fell
    /// through to the AdLib chip: Bad News was still Sierra's long after an original one had
    /// been written. The coverage test did not catch it, because the bank DOES know
    /// resource 44 — as a score. With the chip gone the same gap would be silence.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(20)]    // the mugging
    [InlineData(27)]    // the eviction notice
    [InlineData(30)]    // sacked
    [InlineData(42)]    // the diploma
    [InlineData(44)]    // bad news
    [InlineData(45)]    // good news
    public void EveryStingPlayedAsAnEffectIsAudible(int resource)
    {
        var bank = new OriginalSoundBank(Rate);
        Assert.NotNull(bank.Effect(resource));

        var mixer = new JonesAudioMixer(bank);
        mixer.PlayEffect(resource);
        Assert.True(Render(mixer, 4.0) > 0.01, $"sting {resource} was silent");
    }

    /// <summary>
    /// `(gASong pause: 1)` then `(gASoundEffect play: 44 gASong)` — the duck under a sting,
    /// which `employment.sc:35-36` performs. The bed goes down, the sting plays, and the bed
    /// comes back on its own when the sting ends.
    /// </summary>
    [Fact]
    public void ADuckedBedComesBackWhenTheStingEnds()
    {
        var mixer = Mixer();
        mixer.PlayMusic(43, loop: true);
        Render(mixer, 0.25);

        mixer.PauseMusic();
        Assert.True(mixer.MusicPaused);

        mixer.PlayEffect(44, resumeMusicWhenDone: true);
        Render(mixer, 8.0);

        Assert.False(mixer.MusicPaused, "the bed stayed ducked after the sting ended");
    }

    [Fact]
    public void StopAllLeavesTrueSilence()
    {
        var mixer = Mixer();
        mixer.PlayMusic(5, loop: true);
        mixer.PlayEffect(23);
        Render(mixer, 0.25);

        mixer.StopAll();

        var buffer = new float[4096];
        Assert.False(mixer.RenderStereo(buffer), "something was still playing");
        Assert.All(buffer, s => Assert.Equal(0f, s));
    }

    /// <summary>
    /// `sndMASTER_VOLUME` is a 0-15 control in the scripts. The AdLib driver folded it into
    /// note velocity; with that driver gone it is a plain gain, and what has to hold is that
    /// the bottom of the scale is dramatically quieter than the top.
    /// </summary>
    [Fact]
    public void TheMasterVolumeScaleReachesFromLoudToNearlyNothing()
    {
        static double PeakAt(int volume)
        {
            var mixer = new JonesAudioMixer(new OriginalSoundBank(Rate)) { MasterVolume = volume };
            mixer.PlayMusic(39, loop: true);
            return Render(mixer, 0.5);
        }

        var loud = PeakAt(15);
        var quiet = PeakAt(0);

        Assert.True(loud > 0.05, $"full volume only reached {loud:F3}");
        Assert.True(quiet < loud * 0.1, $"volume 0 ({quiet:F3}) is not far below volume 15 ({loud:F3})");
    }

    /// <summary>
    /// A head with no soundtrack at all must not fall over. There is no second path to fall
    /// back to any more, so the honest behaviour is silence rather than a crash.
    /// </summary>
    [Fact]
    public void WithNoSoundtrackNothingThrows()
    {
        var mixer = new JonesAudioMixer(null);
        Assert.False(mixer.Available);

        mixer.PlayMusic(5);
        mixer.PlayEffect(23);
        mixer.PlayEffect2(29);
        mixer.PauseMusic();
        mixer.ResumeMusic();
        mixer.StopAll();

        var buffer = new float[2048];
        Assert.False(mixer.RenderStereo(buffer));
        Assert.All(buffer, s => Assert.Equal(0f, s));
    }
}
