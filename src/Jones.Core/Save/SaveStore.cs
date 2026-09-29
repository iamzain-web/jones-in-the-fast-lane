using System.Text.Json;

namespace Jones.Core.Save;

/// <summary>What a restore attempt found on disk. The three cases are the original's three.</summary>
public enum RestoreOutcome
{
    /// <summary>A save was read. `RestoreGame` succeeded (`Save.sc:56`).</summary>
    Ok,

    /// <summary>
    /// Nothing to restore. `GetSaveFiles` returned 0 (`Save.sc:53`), which prints
    /// text 990[3] — "Can't Restore. No previously saved game was found."
    /// </summary>
    NoSaveFound,

    /// <summary>
    /// A file is there and cannot be used: unparseable, wrong game, or a version this
    /// build does not know. `CheckSaveGame` false (`Save.sc:55`), which prints text 990[2]
    /// — and note the original's own list of reasons ends "…or The game was saved under a
    /// different interpreter", i.e. the original refused an unknown format too.
    /// </summary>
    Unreadable,
}

/// <summary>
/// Where a saved game lives, and the only code that touches the disk.
///
/// NOT NEXT TO THE EXECUTABLE. <see cref="Environment.SpecialFolder.ApplicationData"/> is
/// the per-user application-data directory on every platform the port targets — on Windows
/// %APPDATA%, on Linux and macOS ~/.config, and on Android the app's own private files
/// directory — so the desktop head and an Android head both get a writable location without
/// either knowing about the other. A head that needs somewhere else can set
/// <see cref="DirectoryOverride"/> before the first save.
///
/// ONE SLOT, because the game has one: `Save.sc` passes the literal save number 1 to
/// `SaveGame`, `CheckSaveGame` and `RestoreGame` alike, and never shows a file list.
///
/// THE RANDOM STREAM IS NOT SAVED, and this is a decision rather than an omission.
/// <c>SciRandom</c> wraps <see cref="System.Random"/>, whose internal state is not
/// reachable and whose sequence .NET does not guarantee across runtimes, so there is
/// nothing stable to write down. Nor did the original save one: `SaveGame` snapshots the
/// SCI heap, and the interpreter's random seed is not in it — a restored 1990 game rolled
/// fresh numbers too. A restored game therefore continues with a new stream, which means
/// reloading and replaying the same week can produce a different economy, the same as it
/// always could. Reproducibility that matters to the PORT — every rule test — is obtained
/// from <c>ScriptedRandom</c>, not from saved state.
/// </summary>
public static class SaveStore
{
    /// <summary>Set by a head that must put saves somewhere else. Null uses the default.</summary>
    public static string? DirectoryOverride { get; set; }

    /// <summary>The folder saves live in. Created on demand by <see cref="Write"/>.</summary>
    public static string Directory =>
        DirectoryOverride
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
                                      Environment.SpecialFolderOption.Create),
            "JonesInTheFastLane");

    /// <summary>The one slot.</summary>
    public static string SlotPath => Path.Combine(Directory, "savegame.json");

    /// <summary>
    /// `GetSaveFiles` (`Save.sc:53`) used as the original uses it: purely a test for
    /// whether there is anything to restore.
    /// </summary>
    public static bool Exists() => File.Exists(SlotPath);

    /// <summary>
    /// Writes the slot, replacing whatever was there — which is what text 997[11] warns
    /// about before the Save menu item does anything.
    ///
    /// Returns false on any I/O failure, which is the `(not (SaveGame …))` branch at
    /// `Save.sc:28` and prints text 990[0]. The write goes to a temporary file first and is
    /// moved into place, so a failure halfway through leaves the previous save intact
    /// rather than destroying it — the one way this improves on "The disk is full. You must
    /// use another disk. The Save has been aborted."
    /// </summary>
    public static bool Write(GameSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var file = new SaveFile
        {
            Version = SaveFormat.Version,
            Game = SaveFormat.Magic,
            SavedUtc = DateTimeOffset.UtcNow,
            State = state,
        };

        var temp = SlotPath + ".tmp";

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(temp, JsonSerializer.Serialize(file, SaveFormat.Options));
            File.Move(temp, SlotPath, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or NotSupportedException or System.Security.SecurityException)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { /* nothing left to do */ }
            return false;
        }
    }

    /// <summary>
    /// Reads the slot. <paramref name="state"/> is null unless the result is
    /// <see cref="RestoreOutcome.Ok"/>.
    ///
    /// The version is checked BEFORE the state is deserialised, from the raw document, so
    /// an unknown version can never be half-loaded into a live game — the whole point of
    /// versioning the format from the first release. A file that is unreadable for any
    /// reason leaves the game running exactly as it was.
    /// </summary>
    public static RestoreOutcome Read(out GameSnapshot? state)
    {
        state = null;

        string text;
        try
        {
            if (!File.Exists(SlotPath)) return RestoreOutcome.NoSaveFound;
            text = File.ReadAllText(SlotPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or NotSupportedException or System.Security.SecurityException)
        {
            return RestoreOutcome.Unreadable;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return RestoreOutcome.Unreadable;

            if (!root.TryGetProperty("game", out var magic)
                || magic.ValueKind != JsonValueKind.String
                || magic.GetString() != SaveFormat.Magic)
                return RestoreOutcome.Unreadable;

            if (!root.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var v)
                || v != SaveFormat.Version)
                return RestoreOutcome.Unreadable;

            var file = JsonSerializer.Deserialize<SaveFile>(text, SaveFormat.Options);
            if (file?.State is null) return RestoreOutcome.Unreadable;

            state = file.State;
            return RestoreOutcome.Ok;
        }
        catch (JsonException)
        {
            return RestoreOutcome.Unreadable;
        }
    }

    /// <summary>Removes the slot. Used by the tests; no menu item deletes a save.</summary>
    public static void Delete()
    {
        try { if (File.Exists(SlotPath)) File.Delete(SlotPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* leave it */ }
    }
}
