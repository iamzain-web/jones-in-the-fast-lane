using System;
using System.IO;
using System.IO.Compression;
using AContent = global::Android.Content;

namespace Jones.App.Android;

/// <summary>
/// The Android answer to <c>Program.FindAssetRoot</c>.
///
/// The desktop head walks up from the executable until it finds <c>assets/</c>. There is
/// no such tree inside an APK, so this head carries the assets with it and hands the
/// shared loaders a directory they can read with <see cref="File"/> - which is what they
/// all do: <c>SciText.Load</c>, <c>LipSync.SetDirectory</c>, <c>Subtitles.Load</c> and
/// <c>SciSoundLibrary.FromAssetRoot</c> take a path and call <c>File.ReadAllBytes</c>.
///
/// Only the small resources are unpacked (see the csproj for the split and why). The
/// 21.5MB of speech stays in the APK and is read through the AssetManager by
/// <see cref="AndroidAudioPlayer"/>, which is the only thing that reads it.
/// </summary>
internal static class GameAssets
{
    /// <summary>The build-time zip, packed by the csproj's PackJonesData target.</summary>
    private const string DataZipAsset = "jones-data.zip";

    /// <summary>Where the speech clips sit inside the APK's assets.</summary>
    public const string SpeechAssetDir = "speech";

    private static string? _root;

    /// <summary>
    /// The unpacked asset root, created on demand. Under <c>FilesDir</c>, which is app
    /// private storage: no permission needed, wiped with the app, and not visible to the
    /// gallery scanner.
    /// </summary>
    public static string Root(AContent.Context context)
    {
        if (_root is not null) return _root;

        var root = Path.Combine(context.FilesDir!.AbsolutePath, "gamedata");
        Directory.CreateDirectory(root);
        return _root = root;
    }

    /// <summary>
    /// Unpacks the data zip if it has not been unpacked for THIS build of the APK.
    ///
    /// The stamp is the APK's own last-write time rather than a version number, so
    /// re-deploying a debug build during development re-unpacks without anyone having to
    /// remember to bump anything. A missing or unreadable stamp means unpack.
    ///
    /// Runs on the startup thread. It is about 300KB across ~620 files, which is a few
    /// tens of milliseconds - the 21.5MB that would have made this a background job is
    /// deliberately not here.
    /// </summary>
    public static void Unpack(AContent.Context context)
    {
        var root = Root(context);
        var stampFile = Path.Combine(root, ".unpacked");
        var stamp = ApkStamp(context);

        try
        {
            if (stamp is not null && File.Exists(stampFile) &&
                File.ReadAllText(stampFile) == stamp)
                return;
        }
        catch (IOException)
        {
            // Unreadable stamp: unpack again. Doing the work twice is harmless.
        }

        try
        {
            // ZipArchive in Read mode needs to seek, and an AssetManager stream cannot.
            // Buffering the whole zip is fine at this size; it is the reason the speech
            // is not in it.
            using var buffered = new MemoryStream();
            using (var asset = context.Assets!.Open(DataZipAsset))
                asset.CopyTo(buffered);

            buffered.Position = 0;
            using var zip = new ZipArchive(buffered, ZipArchiveMode.Read);

            foreach (var entry in zip.Entries)
            {
                // A directory entry has an empty Name and nothing to write.
                if (entry.Name.Length == 0) continue;

                var destination = Path.GetFullPath(
                    Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));

                // Zip-slip guard. This zip is ours, built two lines of MSBuild away, but
                // an extractor that will write outside its root is a bug whoever reads it
                // next has to reason about.
                if (!destination.StartsWith(root, StringComparison.Ordinal)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }

            if (stamp is not null) File.WriteAllText(stampFile, stamp);
            AndroidLog.Info($"unpacked {zip.Entries.Count} entries to {root}");
        }
        catch (Exception e)
        {
            // A failed unpack must not stop the game starting. Every shared loader treats
            // a missing resource as "show nothing" already, so the worst case is a silent
            // game with no printed strings - which is exactly what the desktop head does
            // when it cannot find assets/ either.
            //
            // LOGGED, THOUGH. Swallowed silently, this failure presents as a game with no
            // text and no sound and no reason given, which is the hardest kind to diagnose
            // from a phone - see AndroidLog.
            AndroidLog.Error("GameAssets.Unpack (the game will print nothing and stay silent)", e);
        }
    }

    /// <summary>
    /// The installed APK's last-write time. <c>ApplicationInfo.SourceDir</c> is the APK's
    /// path on disk, so this needs no PackageManager call and no deprecated flags
    /// overload.
    /// </summary>
    private static string? ApkStamp(AContent.Context context)
    {
        try
        {
            var apk = context.ApplicationInfo?.SourceDir;
            if (string.IsNullOrEmpty(apk) || !File.Exists(apk)) return null;
            return File.GetLastWriteTimeUtc(apk).Ticks.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
