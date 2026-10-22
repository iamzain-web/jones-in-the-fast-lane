using Jones.Audio;
using Jones.Audio.Original;

namespace Jones.Tests;

/// <summary>
/// THE SYNTHESISER ACTUALLY MAKES A SOUND.
///
/// <para>
/// WHY THIS EXISTS, AND IT IS NOT A HYPOTHETICAL. The browser head reported itself
/// perfectly healthy while playing nothing at all: the synth built, the output context ran,
/// the pump ticked twenty times a second, and the scheduler held its buffer at exactly the
/// target depth. Every indicator anyone would think to check was green. The audio was
/// SILENCE - a buffer full of zeroes schedules precisely as happily as a buffer full of
/// music, and nothing upstream can tell the difference.
/// </para>
///
/// <para>
/// So the thing to assert is the only thing that matters to a player: that samples come out
/// and they are not all zero. Everything else - that a pump ran, that a buffer was queued -
/// is a check of PLUMBING, and the plumbing was never the part that was broken.
/// </para>
///
/// <para>
/// Managed code all the way down: the scores are embedded resources in Jones.Audio and the
/// synthesis is C#, so this runs with no device, no browser and no platform layer.
/// </para>
/// </summary>
public class AudibleOutputTests
{
    /// <summary>Half a second of stereo at the engine's own rate.</summary>
    private const int Frames = JonesAudioMixer.SampleRate / 2;

    private static (float Peak, double Rms) Measure(JonesAudioMixer engine, int chunks)
    {
        var buffer = new float[Frames * 2];
        float peak = 0;
        double sum = 0;
        long n = 0;

        for (var c = 0; c < chunks; c++)
        {
            engine.RenderStereo(buffer);
            foreach (var v in buffer)
            {
                var a = v < 0 ? -v : v;
                if (a > peak) peak = a;
                sum += (double)v * v;
                n++;
            }
        }

        return (peak, Math.Sqrt(sum / Math.Max(1, n)));
    }

    private static JonesAudioMixer Engine()
    {
        var bank = new OriginalSoundBank(JonesAudioMixer.SampleRate);
        bank.PreRenderEffects();
        return new JonesAudioMixer(bank);
    }

    /// <summary>
    /// SILENCE WHEN NOTHING IS PLAYING, so a non-zero reading below means something rather
    /// than the measurement being incapable of returning zero.
    /// </summary>
    [Fact]
    public void AnIdleEngineRendersSilence()
    {
        var (peak, rms) = Measure(Engine(), 2);

        Assert.Equal(0f, peak);
        Assert.Equal(0d, rms);
    }

    /// <summary>
    /// THE TOWN BOARD'S THEME COMES OUT AUDIBLE. Resource 5 is what `room1.sc:281` plays
    /// under the board, and it is the first music anybody hears.
    /// </summary>
    [Fact]
    public void TheBoardThemeProducesSignal()
    {
        var engine = Engine();
        engine.PlayMusic(LocationMusicBoard);

        var (peak, rms) = Measure(engine, 4);     // two seconds

        Assert.True(peak > 0.01f,
            $"the board theme rendered a peak of {peak:0.0000} - that is silence, not music.");
        Assert.True(rms > 0.001d,
            $"the board theme rendered an RMS of {rms:0.000000} - audible music is not that quiet.");
    }

    /// <summary>`LocationMusic.Board`, repeated here so Jones.Tests needs no view-model reference.</summary>
    private const int LocationMusicBoard = 5;

    /// <summary>
    /// EVERY SCORE THE LIBRARY CLAIMS TO HAVE RENDERS SOMETHING. A resource present in the
    /// table but silent in practice is the same defect as a missing one, and harder to see:
    /// the building plays, the bed is "running", and the room is quiet.
    /// </summary>
    [Fact]
    public void EveryWrittenScoreIsAudible()
    {
        var silent = new List<int>();

        foreach (var resource in ScoreLibrary.ForResource.Keys.OrderBy(n => n))
        {
            var engine = Engine();
            engine.PlayMusic(resource);

            var (peak, _) = Measure(engine, 2);
            if (peak <= 0.01f) silent.Add(resource);
        }

        Assert.True(silent.Count == 0,
            $"these resources are in ScoreLibrary and render silence: {string.Join(", ", silent)}");
    }

    /// <summary>
    /// THE BUTTON CLICK, which is the effect path rather than the music path and is the
    /// sound a player hears first and most often.
    /// </summary>
    [Fact]
    public void AnEffectProducesSignal()
    {
        var engine = Engine();
        engine.PlayEffect(ButtonClick);

        var (peak, _) = Measure(engine, 2);

        Assert.True(peak > 0.01f,
            $"the button click rendered a peak of {peak:0.0000} - that is silence.");
    }

    /// <summary>`LocationMusic.ButtonClick`.</summary>
    private const int ButtonClick = 23;
}
