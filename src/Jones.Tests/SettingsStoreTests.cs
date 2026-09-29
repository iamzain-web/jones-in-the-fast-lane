using System.Text.Json;
using Jones.Core.Save;
using Xunit;

namespace Jones.Tests;

/// <summary>
/// Everything that redirects <see cref="SaveStore.DirectoryOverride"/> runs in here, because
/// that override is a static and xunit runs separate classes in parallel by default. Without
/// this the save tests and the settings tests point the same static at two temp directories at
/// once and one of them deletes the other's.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SaveDirectoryCollection
{
    public const string Name = "the save directory";
}

/// <summary>
/// The sound switches on disk.
///
/// <para>
/// THIS IS A PORT DEVIATION AND THE TESTS SAY SO, because the temptation is to read
/// `Main.sc:1198-1200` as the original reloading its volume. It is not: the file it opens is
/// named "version", the buffer it fills is `global539` — which `Menu.sc:176` prints as the
/// version banner and `Save.sc` hands to `SaveGame` — and the CD decompilation's name for the
/// receiving global, `gVolume`, is called `gVersion` by the floppy decompilation of the same
/// code. The volume, `global520`, is initialised to 12 in the script's own variable block and
/// is never written to disk by anything.
/// </para>
///
/// <para>
/// What IS tested here is that the port's version of the feature behaves like its save game:
/// versioned, magic-checked, and refused outright rather than half-loaded.
/// </para>
/// </summary>
[Collection(SaveDirectoryCollection.Name)]
public class SettingsStoreTests
{
    [Fact]
    public void EverySwitchSurvivesTheRoundTrip()
    {
        InATempSaveDirectory(() =>
        {
            var written = new SoundSettings
            {
                SoundEnabled = false,
                MusicOff = true,
                EffectsOff = true,
                SpeechOff = true,
            };

            Assert.True(SettingsStore.Write(written));

            var read = SettingsStore.Read();

            Assert.NotNull(read);
            Assert.False(read!.SoundEnabled);
            Assert.True(read.MusicOff);
            Assert.True(read.EffectsOff);
            Assert.True(read.SpeechOff);
        });
    }

    /// <summary>The other way round, so a test cannot pass on everything defaulting true.</summary>
    [Fact]
    public void TheAllOnStateSurvivesTooAndIsNotConfusedWithNoFile()
    {
        InATempSaveDirectory(() =>
        {
            Assert.True(SettingsStore.Write(new SoundSettings()));

            var read = SettingsStore.Read();

            Assert.NotNull(read);
            Assert.True(read!.SoundEnabled);
            Assert.False(read.MusicOff);
            Assert.False(read.EffectsOff);
            Assert.False(read.SpeechOff);
        });
    }

    /// <summary>
    /// Nothing on disk is not an error and not a half-state: the caller keeps its defaults,
    /// which is a game that makes a noise.
    /// </summary>
    [Fact]
    public void NoFileReadsAsNull()
    {
        InATempSaveDirectory(() => Assert.Null(SettingsStore.Read()));
    }

    /// <summary>
    /// An unknown version is REFUSED rather than deserialised on a best-effort basis — the
    /// same rule <see cref="SaveStore.Read"/> follows, for the same reason: a flag that means
    /// something different in a later build must not silently unmute somebody's game.
    /// </summary>
    [Fact]
    public void AnUnknownVersionIsRefusedRatherThanHalfLoaded()
    {
        InATempSaveDirectory(() =>
        {
            File.WriteAllText(SettingsStore.Path,
                $$"""
                  { "version": 99, "game": "{{SettingsFormat.Magic}}",
                    "sound": { "soundEnabled": false, "musicOff": true,
                               "effectsOff": true, "speechOff": true } }
                  """);

            Assert.Null(SettingsStore.Read());
        });
    }

    [Fact]
    public void SomeOtherProgramsJsonIsRefused()
    {
        InATempSaveDirectory(() =>
        {
            File.WriteAllText(SettingsStore.Path,
                """{ "version": 1, "game": "something-else", "sound": { "musicOff": true } }""");

            Assert.Null(SettingsStore.Read());
        });
    }

    [Fact]
    public void RubbishIsRefused()
    {
        InATempSaveDirectory(() =>
        {
            File.WriteAllText(SettingsStore.Path, "not json at all {{{");
            Assert.Null(SettingsStore.Read());

            File.WriteAllText(SettingsStore.Path, "[1, 2, 3]");
            Assert.Null(SettingsStore.Read());
        });
    }

    /// <summary>A second write replaces the first; there is one set of settings, as there is one save.</summary>
    [Fact]
    public void WritingAgainReplaces()
    {
        InATempSaveDirectory(() =>
        {
            Assert.True(SettingsStore.Write(new SoundSettings { MusicOff = true }));
            Assert.True(SettingsStore.Write(new SoundSettings { EffectsOff = true }));

            var read = SettingsStore.Read()!;

            Assert.False(read.MusicOff);
            Assert.True(read.EffectsOff);
        });
    }

    /// <summary>
    /// Settings are NOT the save game and must not be read as one, or restoring a game would
    /// carry somebody else's mute with it.
    /// </summary>
    [Fact]
    public void TheSettingsAreTheirOwnFileAndNotTheSaveSlot()
    {
        InATempSaveDirectory(() =>
        {
            Assert.NotEqual(SaveStore.SlotPath, SettingsStore.Path);

            Assert.True(SettingsStore.Write(new SoundSettings { MusicOff = true }));

            // Writing settings does not create a save.
            Assert.False(SaveStore.Exists());
            Assert.Equal(RestoreOutcome.NoSaveFound, SaveStore.Read(out _));
        });
    }

    /// <summary>
    /// The file lands beside the save game, so a head that redirected saves with
    /// <see cref="SaveStore.DirectoryOverride"/> — the Android head's escape hatch — has
    /// redirected these too and does not need a second override.
    /// </summary>
    [Fact]
    public void TheDirectoryOverrideMovesTheSettingsWithTheSaves()
    {
        InATempSaveDirectory(() =>
        {
            Assert.Equal(SaveStore.Directory, Path.GetDirectoryName(SettingsStore.Path));
        });
    }

    /// <summary>The envelope really is versioned on disk, not just in the reader.</summary>
    [Fact]
    public void TheFileCarriesItsVersionAndMagic()
    {
        InATempSaveDirectory(() =>
        {
            Assert.True(SettingsStore.Write(new SoundSettings()));

            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsStore.Path));

            Assert.Equal(SettingsFormat.Version, doc.RootElement.GetProperty("version").GetInt32());
            Assert.Equal(SettingsFormat.Magic, doc.RootElement.GetProperty("game").GetString());
        });
    }

    private static void InATempSaveDirectory(Action body)
    {
        var previous = SaveStore.DirectoryOverride;
        var dir = Path.Combine(Path.GetTempPath(), "jones-settings-tests-" + Guid.NewGuid().ToString("N"));

        try
        {
            SaveStore.DirectoryOverride = dir;
            Directory.CreateDirectory(dir);
            body();
        }
        finally
        {
            SaveStore.DirectoryOverride = previous;
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
