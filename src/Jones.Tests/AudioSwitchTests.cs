using System;
using System.IO;
using System.Linq;
using Jones.Audio;
using Jones.Audio.Original;

namespace Jones.Tests;

/// <summary>
/// The A/B switch between the two audio paths.
///
/// Everything here is about the same worry: that adding a second soundtrack quietly changed
/// the first one. So the tests are mostly about what must NOT have happened â€” the default is
/// still the AdLib path, the mono render is still the same samples, and a cue with no
/// original version still comes from the chip.
/// </summary>
public class AudioSwitchTests
{
    private const int Rate = JonesAudioMixer.SampleRate;

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

    private static JonesAudioMixer Mixer() =>
        new(SciSoundLibrary.FromAssetRoot(AssetRoot()), new OriginalSoundBank(Rate));

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

    // ------------------------------------------------------------------ the default

    /// <summary>
    /// THE ONE THAT MATTERS MOST. A mixer nobody has touched plays the AdLib path, which is
    /// what the port has always done.
    /// </summary>
    /// <summary>
    /// THE DEFAULT IS NOW THE ORIGINAL SET. It was the AdLib path for as long as the
    /// original one was incomplete; every resource the scripts can reach now has an
    /// original cue, so the player does not have to turn their own soundtrack on.
    ///
    /// This test used to assert the opposite and is deliberately kept rather than deleted,
    /// because the day the default moves again is a day somebody should have to edit a test
    /// that says so out loud.
    /// </summary>
    [Fact]
    public void TheDefaultIsNowTheOriginalSet()
    {
        var mixer = Mixer();
        Assert.Equal(AudioSource.Original, mixer.Source);

        Assert.True(mixer.UsesOriginal(5));    // the town board
        Assert.True(mixer.UsesOriginal(23));   // the button click
        Assert.True(mixer.UsesOriginal(6));    // the title theme
    }

    /// <summary>
    /// ...but a head with no original set at all stays on the AdLib path. Missing assets
    /// must not mean a game that claims a soundtrack it cannot play.
    /// </summary>
    [Fact]
    public void WithNoOriginalSetTheDefaultIsStillTheAdLibPath()
    {
        var mixer = new JonesAudioMixer(SciSoundLibrary.FromAssetRoot(AssetRoot()), null);
        Assert.Equal(AudioSource.AdLib, mixer.Source);
        Assert.False(mixer.OriginalAvailable);
    }

    /// <summary>
    /// In AdLib mode the stereo render is the mono chip placed up the middle, and the two
    /// channels must be bit-identical. Anything else means something has widened or
    /// reprocessed a path that is supposed to be untouched.
    /// </summary>
    [Fact]
    public void InAdLibModeBothChannelsAreTheSameSamples()
    {
        var mixer = Mixer();
        mixer.Source = AudioSource.AdLib;
        mixer.PlayMusic(39, loop: true);

        var buffer = new float[Rate];            // half a second of stereo
        mixer.RenderStereo(buffer);

        Assert.True(Peak(buffer) > 0.01, "the bed never started");
        for (var i = 0; i < buffer.Length; i += 2)
            Assert.Equal(buffer[i], buffer[i + 1]);
    }

    /// <summary>
    /// The mono path still exists and still produces exactly what the AdLib engine produces
    /// on its own â€” measured against a bare <see cref="SciSoundEngine"/> playing the same
    /// resource, sample for sample.
    /// </summary>
    [Fact]
    public void TheMonoPathIsSampleForSampleTheOldOne()
    {
        var library = SciSoundLibrary.FromAssetRoot(AssetRoot())!;

        var bare = new SciSoundEngine(library.Bank);
        bare.PlayMusic(library.Get(39)!, loop: true);

        var mixer = new JonesAudioMixer(library, new OriginalSoundBank(Rate))
        {
            Source = AudioSource.AdLib,
        };
        mixer.PlayMusic(39, loop: true);

        var a = new short[Rate / 2];
        var b = new short[Rate / 2];
        bare.Render(a);
        mixer.Render(b);

        Assert.Equal(a, b);
    }

    // ------------------------------------------------------------------ switching

    [Fact]
    public void SwitchingToTheOriginalSetChangesWhichPathACueComesFrom()
    {
        var mixer = Mixer();
        mixer.Source = AudioSource.AdLib;
        Assert.False(mixer.UsesOriginal(5));

        mixer.Source = AudioSource.Original;

        Assert.True(mixer.UsesOriginal(5), "resource 5 has an original cue and should use it");
        Assert.True(mixer.UsesOriginal(23), "resource 23 has an original effect");
    }

    /// <summary>
    /// Switching while a bed is playing restarts THAT bed on the other synthesiser. A switch
    /// that left the room silent until the player walked out and back in would be useless
    /// for comparing the two, which is the only thing it is for.
    /// </summary>
    [Fact]
    public void SwitchingMidBedRestartsTheSameBedOnTheOtherSynthesiser()
    {
        var mixer = Mixer();
        mixer.PlayMusic(5, loop: true);
        Assert.True(Render(mixer, 0.5) > 0.01, "the AdLib bed never started");

        mixer.Source = AudioSource.Original;

        Assert.True(mixer.MusicPlaying, "the switch left the room silent");
        Assert.True(Render(mixer, 1.0) > 0.01, "the original bed never started");

        // ...and back again.
        mixer.Source = AudioSource.AdLib;
        Assert.True(mixer.MusicPlaying);
        Assert.True(Render(mixer, 0.5) > 0.01, "the AdLib bed did not come back");
    }

    /// <summary>
    /// With the original set selected, its music really is stereo â€” which is the whole
    /// reason the seam was widened.
    /// </summary>
    [Fact]
    public void TheOriginalSetActuallyUsesTheStereoItWasWidenedFor()
    {
        var mixer = Mixer();
        mixer.Source = AudioSource.Original;
        mixer.PlayMusic(5, loop: true);

        var buffer = new float[Rate * 2];       // one second
        var done = 0;
        double difference = 0;
        while (done < buffer.Length)
        {
            var take = Math.Min(4096, buffer.Length - done);
            mixer.RenderStereo(buffer.AsSpan(done, take));
            done += take;
        }
        for (var i = 0; i < buffer.Length; i += 2) difference += Math.Abs(buffer[i] - buffer[i + 1]);

        Assert.True(difference / (buffer.Length / 2) > 0.001,
            "the original bed came out identical on both channels");
    }

    /// <summary>
    /// THE FALLBACK, which is what lets the set be written a batch at a time: with the
    /// original set selected, a resource nobody has written yet must still play, from the
    /// chip, rather than fall silent.
    ///
    /// The resource is CHOSEN AT RUN TIME rather than named, because naming one means
    /// editing this test every time a batch of cues lands â€” and the first version of it did
    /// name resource 39, which broke the moment QT Clothing was written. Once every
    /// resource has an original version there is nothing left to fall back from, and the
    /// test retires itself.
    /// </summary>
    [Fact]
    public void ACueWithNoOriginalVersionStillPlaysFromTheChip()
    {
        var bank = new OriginalSoundBank(Rate);
        var unwritten = SciAudioTests.AllResources.FirstOrDefault(n => !bank.Has(n));
        if (unwritten == 0) return;          // the original set is complete

        var mixer = Mixer();
        mixer.Source = AudioSource.Original;

        Assert.False(mixer.UsesOriginal(unwritten));

        mixer.PlayMusic(unwritten, loop: true);
        Assert.True(mixer.MusicPlaying);
        Assert.True(Render(mixer, 0.5) > 0.01,
            $"resource {unwritten} has no original version and went silent instead of " +
            "falling back to the AdLib arrangement");
    }

    /// <summary>
    /// The button click is the sound the interface depends on most, and it is one of the six
    /// that ARE written. It has to be audible on both paths.
    /// </summary>
    [Theory]
    [InlineData(AudioSource.AdLib)]
    [InlineData(AudioSource.Original)]
    public void TheButtonClickIsAudibleOnEitherPath(AudioSource source)
    {
        var mixer = Mixer();
        mixer.Source = source;
        mixer.PlayEffect(23);

        Assert.True(Render(mixer, 0.5) > 0.01, $"the click was silent on {source}");
    }

    /// <summary>
    /// `(gASong pause: 1)` then `(gASoundEffect play: 44 gASong)` â€” the duck under a sting,
    /// which `employment.sc:35-36` performs. It has to work when the bed and the sting are
    /// on DIFFERENT synthesisers, which is a case the single-engine code never had.
    /// </summary>
    /// <remarks>
    /// Checked on BOTH paths. It used to be written as a CROSS-ENGINE test, with the
    /// Employment Office bed original and sting 44 still coming from the chip, because at
    /// the time 44 had not been written. Coverage is complete now, so that combination can
    /// no longer arise in play — but the cross-engine release in
    /// <see cref="JonesAudioMixer.PlayEffect"/> is kept, because a resource added later
    /// would recreate it.
    /// </remarks>
    [Theory]
    [InlineData(AudioSource.AdLib)]
    [InlineData(AudioSource.Original)]
    public void ADuckedBedComesBackWhenTheStingEnds(AudioSource source)
    {
        var mixer = Mixer();
        mixer.Source = source;

        mixer.PlayMusic(43, loop: true);
        Render(mixer, 0.25);

        mixer.PauseMusic();
        Assert.True(mixer.MusicPaused);

        mixer.PlayEffect(44, resumeMusicWhenDone: true);

        // Sting 44 is 153 ticks, about 2.6 s; render well past it.
        Render(mixer, 8.0);

        Assert.False(mixer.MusicPaused,
            $"on {source} the bed stayed ducked after the sting ended");
    }

    [Fact]
    public void StopAllLeavesTrueSilenceOnEitherPath()
    {
        foreach (var source in new[] { AudioSource.AdLib, AudioSource.Original })
        {
            var mixer = Mixer();
            mixer.Source = source;
            mixer.PlayMusic(5, loop: true);
            mixer.PlayEffect(23);
            Render(mixer, 0.25);

            mixer.StopAll();

            var buffer = new float[4096];
            Assert.False(mixer.RenderStereo(buffer), $"{source} still had something playing");
            Assert.All(buffer, s => Assert.Equal(0f, s));
        }
    }

    // ------------------------------------------------------------------ the bank

    /// <summary>
    /// COMPLETE COVERAGE: every sound resource the game can actually reach has an original
    /// version, so nothing the player can hear is Sierra's music.
    ///
    /// <para>
    /// This is the whole point of the original-audio work stated as an assertion. The
    /// per-cue fallback in <see cref="JonesAudioMixer"/> means an uncovered resource
    /// silently plays the AdLib arrangement instead of falling silent â€” which was exactly
    /// right while the set was being written a batch at a time, and is exactly what would
    /// hide a gap now. Without this test, a resource added later, or a mapping deleted by
    /// accident, would quietly put Sierra's music back and nothing would say so.
    /// </para>
    ///
    /// <para>
    /// 28 AND 32 ARE EXCLUDED, and that is a finding rather than a convenience. Every
    /// `play:`, `playBed:` and `number` in BOTH decompilations reaches exactly 32 sound
    /// numbers, and neither 28 nor 32 is among them â€” no call site, no `UnLoad 132`, no
    /// Sound instance default. The game cannot play them, so they cannot be heard, so they
    /// need no cue. The reachable set is listed here rather than computed so that a change
    /// to the scripts shows up as a failure rather than as a quietly smaller number.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryResourceTheGameCanReachHasAnOriginalCue()
    {
        // Extracted from scripts/jones-cd-dos-1.0/src and scripts/jones-dos-1.000.060/src.
        int[] reachable =
        [
            5, 6, 7, 8, 9, 10, 20, 21, 23, 25, 27, 29, 30, 31,
            34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 100,
        ];

        var bank = new OriginalSoundBank(Rate);
        var missing = reachable.Where(n => !bank.Has(n)).ToArray();

        Assert.True(missing.Length == 0,
            "these resources can be played by the scripts and have no original cue, so the " +
            "player would hear Sierra's music: " + string.Join(", ", missing));

        // ...and the two that are NOT reachable are deliberately not covered.
        Assert.False(bank.Has(28));
        Assert.False(bank.Has(32));

        // Every resource the game has is either reachable or one of those two.
        Assert.Equal(SciAudioTests.AllResources.OrderBy(n => n),
            reachable.Concat([28, 32]).OrderBy(n => n));
    }

    [Fact]
    public void EveryResourceTheBankClaimsIsOneTheGameActuallyHas()
    {
        var bank = new OriginalSoundBank(Rate);
        Assert.NotEmpty(bank.Resources);

        foreach (var resource in bank.Resources)
        {
            Assert.Contains(resource, SciAudioTests.AllResources);
            Assert.True(bank.Music(resource) is not null || bank.Effect(resource) is not null,
                $"the bank claims resource {resource} but produces nothing for it");
        }
    }

    /// <summary>
    /// A resource the bank does not cover reports nothing rather than throwing.
    ///
    /// Resource 28 is the case that actually exists: the game HAS it, and no `play:`,
    /// `playBed:` or `number` in either decompilation can reach it, so it deliberately has
    /// no original cue. It used to be resource 6 here, which stopped being uncovered the
    /// moment the title theme was written.
    /// </summary>
    [Fact]
    public void AResourceTheBankDoesNotHaveReportsNothingRatherThanThrowing()
    {
        var bank = new OriginalSoundBank(Rate);
        Assert.False(bank.Has(28));
        Assert.Null(bank.Music(28));
        Assert.Null(bank.Effect(28));
    }

    /// <summary>
    /// A mixer with no original bank at all â€” a head whose assets are missing â€” must behave
    /// exactly as the port did before any of this existed, and must refuse to switch.
    /// </summary>
    [Fact]
    public void WithNoOriginalSetTheSwitchDoesNothing()
    {
        var mixer = new JonesAudioMixer(SciSoundLibrary.FromAssetRoot(AssetRoot()), null);
        Assert.False(mixer.OriginalAvailable);

        // The switch refuses rather than pretending: there is nothing to switch to.
        mixer.Source = AudioSource.Original;
        Assert.Equal(AudioSource.AdLib, mixer.Source);

        mixer.PlayMusic(39, loop: true);
        Assert.True(Render(mixer, 0.5) > 0.01);
    }
}


