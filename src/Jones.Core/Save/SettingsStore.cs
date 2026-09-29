using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jones.Core.Save;

/// <summary>
/// The sound switches as they are written to disk.
///
/// <para>
/// BOOLEANS, BECAUSE THAT IS ALL THE PORT HAS. The original's control is a 0-15 master volume
/// — `global520`, initialised to 12 (`Main.sc:664`), moved by the `Gauge` the Options menu
/// opens on Ctrl-V (`Menu.sc:319-334`) and pushed at the driver with
/// `(DoSound sndMASTER_VOLUME global520)`. Neither `Gauge.sc` nor a volume level is ported:
/// <c>IAudioPlayer</c> exposes `Enabled` and nothing else, so Ctrl-V is the two ends of that
/// slider and nothing in between. WHEN A REAL LEVEL ARRIVES this record grows an int and
/// <see cref="SettingsFormat.Version"/> is bumped; until then writing a fake 0-15 number here
/// would be inventing state the port cannot honour.
/// </para>
/// </summary>
public sealed class SoundSettings
{
    /// <summary>
    /// `MainViewModel.Sound.Enabled` — Ctrl-V's master switch, the one that gates all three
    /// channels. The nearest thing the port has to `global520` being 0 or not.
    /// </summary>
    public bool SoundEnabled { get; set; } = true;

    /// <summary>`MainViewModel.MusicOff` — the floppy's `Turn Music Off `#2` (F2).</summary>
    public bool MusicOff { get; set; }

    /// <summary>`MainViewModel.EffectsOff` — the floppy's `Turn Sound Effects Off `#3` (F3).</summary>
    public bool EffectsOff { get; set; }

    /// <summary>
    /// `MainViewModel.SpeechOff`. NOT the original's — there is no dialogue mute in either
    /// build. It is persisted with the other three because it is the same kind of switch and
    /// leaving it out would be the odd one back to loud.
    /// </summary>
    public bool SpeechOff { get; set; }
}

/// <summary>Version and magic for the settings file. Deliberately not the save game's.</summary>
public static class SettingsFormat
{
    /// <summary>
    /// The only version this build writes, and the only one it will read. Same discipline as
    /// <see cref="SaveFormat.Version"/>: a file whose version is not this one is refused
    /// outright rather than half-loaded, so a future build that means something different by
    /// one of these flags cannot silently unmute a game.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// Checked on load so that pointing this at some other program's JSON fails as a wrong
    /// file rather than as corrupt settings. Distinct from <see cref="SaveFormat.Magic"/>
    /// because these are two different files and neither may be read as the other.
    /// </summary>
    public const string Magic = "jones-in-the-fast-lane-settings";

    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

/// <summary>The envelope. Only <see cref="Version"/> is read before anything else is.</summary>
public sealed class SettingsFile
{
    public int Version { get; set; } = SettingsFormat.Version;
    public string Game { get; set; } = SettingsFormat.Magic;
    public DateTimeOffset SavedUtc { get; set; } = DateTimeOffset.UtcNow;
    public SoundSettings Sound { get; set; } = new();
}

/// <summary>
/// Where the sound switches live between launches.
///
/// <para>
/// THIS IS A DEVIATION AND IT IS NOT A CLOSE CALL. It is tempting to read
/// `Main.sc:1198-1200` as the original loading its volume back at startup:
/// <code>
/// (= gVolume 0)
/// (if (!= (= global538 (FileIO fiOPEN {version} 1)) -1)
///     (= gVolume (FileIO fiREAD_STRING @global539 10 global538))
///     (FileIO fiCLOSE global538)
/// )
/// </code>
/// It is not. `gVolume` is the CD decompilation's mis-naming of global 27; the floppy
/// decompilation of the identical code calls the same global `gVersion` and initialises it to
/// the string `{version}` (`jones-dos-1.000.060/src/Main.sc:71`, `:1185-1188`). The file being
/// opened is literally named "version", it is opened in mode 1 (read), and the ten bytes land
/// in the buffer `global539` — which `Menu.sc:176` then formats into text 997[0], "JONES IN
/// THE FAST LANE Version %s", and which `Save.sc:28`, `:55` and `:56` hand to `SaveGame`,
/// `CheckSaveGame` and `RestoreGame` as the game version. It is the VERSION STRING.
/// </para>
///
/// <para>
/// The volume itself, `global520`, is a plain global initialised to 12 in the script's own
/// variable block (`Main.sc:664`). `Menu.sc:321-333` writes it from the Gauge and calls
/// `DoSound`; `Main.sc:1186` re-applies it at startup and `Game.sc:110` re-applies it after a
/// restore. NOTHING WRITES IT TO DISK — the only `FileIO` calls anywhere in the game are the
/// three above, and they are a read. Quit the 1990 game with the volume down and it comes back
/// at 12.
/// </para>
///
/// <para>
/// THE ONE PLACE THE ORIGINAL DOES CARRY IT is inside a saved game: `SaveGame` snapshots the
/// SCI heap, `global520` is in that heap, and `Game.sc:110`'s `(DoSound sndMASTER_VOLUME
/// global520)` on the restore path exists precisely because the restored heap has just
/// replaced it. So the original does remember the volume — across a restore, not across a
/// launch. That is the nearest precedent, and it is why this is recorded as a deviation with
/// a reason rather than presented as fidelity.
/// </para>
///
/// <para>
/// WHERE, AND HOW. Beside the save game and by the same rules as
/// <see cref="SaveStore"/> — <see cref="SaveStore.Directory"/>, so a head that has redirected
/// saves with <see cref="SaveStore.DirectoryOverride"/> has redirected these too — but in its
/// OWN file. Settings are not game state: a restore must not carry someone else's mute in, and
/// deleting a save must not reset the volume.
/// </para>
/// </summary>
public static class SettingsStore
{
    /// <summary>The settings file. Not <see cref="SaveStore.SlotPath"/>, deliberately.</summary>
    public static string Path => System.IO.Path.Combine(SaveStore.Directory, "settings.json");

    /// <summary>
    /// Reads the switches, or returns null when there is nothing usable to read — no file, an
    /// unreadable one, the wrong magic, or a version this build does not know. Null means
    /// "leave the defaults alone": there is no half-loaded case, for the same reason
    /// <see cref="SaveStore.Read"/> has none.
    /// </summary>
    public static SoundSettings? Read()
    {
        string text;
        try
        {
            if (!File.Exists(Path)) return null;
            text = File.ReadAllText(Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return null;

            if (!root.TryGetProperty("game", out var magic)
                || magic.ValueKind != JsonValueKind.String
                || magic.GetString() != SettingsFormat.Magic)
                return null;

            if (!root.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var v)
                || v != SettingsFormat.Version)
                return null;

            return JsonSerializer.Deserialize<SettingsFile>(text, SettingsFormat.Options)?.Sound;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the switches. Returns false on any I/O failure and says nothing to the player:
    /// the game has no message for this, and inventing one would be inventing user-visible
    /// text. A failed write costs the player their mute next launch and nothing else.
    ///
    /// <para>
    /// Temp-file-then-move, as <see cref="SaveStore.Write"/> does, so a failure halfway
    /// through leaves the previous settings rather than a truncated file the next launch has
    /// to refuse.
    /// </para>
    /// </summary>
    public static bool Write(SoundSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var file = new SettingsFile
        {
            Version = SettingsFormat.Version,
            Game = SettingsFormat.Magic,
            SavedUtc = DateTimeOffset.UtcNow,
            Sound = settings,
        };

        var temp = Path + ".tmp";

        try
        {
            System.IO.Directory.CreateDirectory(SaveStore.Directory);
            File.WriteAllText(temp, JsonSerializer.Serialize(file, SettingsFormat.Options));
            File.Move(temp, Path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or NotSupportedException or System.Security.SecurityException)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { /* nothing left to do */ }
            return false;
        }
    }

    /// <summary>Removes the file. Used by the tests; nothing in the game deletes it.</summary>
    public static void Delete()
    {
        try { if (File.Exists(Path)) File.Delete(Path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* leave it */ }
    }
}
