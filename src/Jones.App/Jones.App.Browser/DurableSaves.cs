using System;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Jones.Core.Save;

namespace Jones.App.Browser;

/// <summary>
/// MAKES SAVED GAMES AND SETTINGS SURVIVE THE TAB.
///
/// <para>
/// THE FAULT. <c>SaveStore</c> and <c>SettingsStore</c> write through <c>System.IO</c> to
/// <see cref="Environment.SpecialFolder.ApplicationData"/>, and on every head but this one
/// that is a real disk. In WebAssembly it is Emscripten's filesystem, which is RAM wearing
/// a filesystem's clothes. Every call SUCCEEDS: the write returns true, <c>Exists</c> says
/// yes, restoring in the same session gives the game back. Reload the page and it has never
/// existed - Restore Game greys itself out, correctly, because there is nothing there.
/// </para>
///
/// <para>
/// It was invisible for exactly that reason. Nothing failed, nothing logged, and the page
/// had written NOT ONE BYTE of browser storage: no localStorage, no IndexedDB, no cookie.
/// </para>
///
/// <para>
/// THE FIX IS A MIRROR, NOT A REWRITE. The stores keep writing files, which keeps the
/// temp-file-then-move atomicity and keeps all three heads on one code path; this copies
/// the two files into <c>localStorage</c> after each write, and copies them back into the
/// filesystem before the game looks. The quantities are tiny - one save slot and one
/// settings file - and localStorage is synchronous, which matters because everything
/// reading these is.
/// </para>
///
/// <para>
/// WHY NOT IndexedDB, which is bigger: it is asynchronous, and the restore has to be
/// finished before <c>SaveStore.Exists()</c> is first called or the game decides there is
/// no save. localStorage's few megabytes are more than a one-slot save needs.
/// </para>
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class DurableSaves
{
    /// <summary>
    /// Where the files live inside the in-memory filesystem. Set explicitly rather than
    /// left to <c>ApplicationData</c>, which in this runtime resolves somewhere
    /// unremarkable but unpredictable; the mirror needs a path it can state.
    /// </summary>
    private const string Directory = "/jones-save";

    // The two files, and their keys. Named rather than enumerated so a stray file in the
    // directory can never end up in the player's browser storage.
    private const string SaveFile = "savegame.json";
    private const string SettingsFile = "settings.json";
    private const string SaveKey = "jones.savegame";
    private const string SettingsKey = "jones.settings";

    [JSImport("globalThis.jonesStorageGet")]
    private static partial string? Get(string key);

    [JSImport("globalThis.jonesStorageSet")]
    private static partial bool Set(string key, string value);

    [JSImport("globalThis.jonesStorageRemove")]
    private static partial void Remove(string key);

    /// <summary>
    /// Points the stores at <see cref="Directory"/>, restores whatever the browser is
    /// holding, and arranges for every later write to be mirrored back.
    ///
    /// <para>
    /// CALLED BEFORE AVALONIA STARTS and does not await anything: localStorage is
    /// synchronous, so by the time this returns the save is on the "disk" and
    /// <c>SaveStore.Exists()</c> is already telling the truth. That ordering is the whole
    /// point - a restore that finished later would be a Restore Game item that greys itself
    /// out and then silently becomes available.
    /// </para>
    /// </summary>
    public static void Install()
    {
        SaveStore.DirectoryOverride = Directory;

        try { System.IO.Directory.CreateDirectory(Directory); }
        catch (Exception) { return; }   // no filesystem, nothing to mirror into

        Restore(SaveKey, SaveFile);
        Restore(SettingsKey, SettingsFile);

        SaveStore.Written += () => Mirror(SaveKey, SaveFile);
        SettingsStore.Written += () => Mirror(SettingsKey, SettingsFile);
    }

    /// <summary>Browser storage to filesystem, at startup.</summary>
    private static void Restore(string key, string file)
    {
        try
        {
            if (Get(key) is not { Length: > 0 } text) return;
            File.WriteAllText(Path.Combine(Directory, file), text);
        }
        catch (Exception)
        {
            // A browser that refuses storage - private windows do - leaves the game exactly
            // as it behaved before this class existed: saving works until the tab closes.
        }
    }

    /// <summary>Filesystem to browser storage, after a write or a delete.</summary>
    private static void Mirror(string key, string file)
    {
        try
        {
            var path = Path.Combine(Directory, file);
            if (File.Exists(path)) Set(key, File.ReadAllText(path));
            else Remove(key);
        }
        catch (Exception)
        {
            // Quota, a private window, storage disabled. The in-session save is unaffected.
        }
    }
}
