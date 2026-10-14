using System;
using System.IO;

namespace Jones.App;

/// <summary>
/// WHERE THE UPSCALED ART LIVES — the one thing that differs between a head with a
/// filesystem and a head without one.
///
/// <see cref="RenderScale"/> decides WHICH art to draw (the factor, the set and their
/// search order) and <see cref="SciArt"/> decides WHEN; neither needs to know whether a
/// cel is a file on disk or an entry in an APK. Everything they need is three questions:
/// is this art directory here, does it hold this cel, and give me its bytes.
///
/// THE DESKTOP HEAD HAS A FILESYSTEM and uses <see cref="FileSystem"/>, which is exactly
/// the code that was inline in RenderScale and SciArt before this existed — same walk up
/// the tree, same File.Exists, same order.
///
/// THE ANDROID HEAD HAS NO TREE TO WALK. Its art rides inside the APK and is read through
/// the AssetManager. The alternative was unpacking 57MB onto the phone on first run, which
/// costs the storage twice and a visible wait, to arrive at bytes the APK could already
/// hand over — assets are stored uncompressed for .png, so reading one is a mapped read.
///
/// <para>A DIRECTORY IS AN OPAQUE TOKEN. <see cref="Locate"/> returns whatever string the
/// store wants to be handed back in <see cref="Exists"/> and <see cref="Open"/> — a full
/// path on disk, an asset prefix in an APK. Nothing outside a store may build one, take it
/// apart or print it as if it were a path.</para>
/// </summary>
public abstract class ArtStore
{
    /// <summary>
    /// The token for the art directory called <paramref name="name"/>
    /// (<c>png4x</c>, <c>png12x-cast0</c>), or null when this store has no such directory —
    /// which is what sends <see cref="RenderScale"/> one rung down its ladder.
    /// </summary>
    public abstract string? Locate(string name);

    /// <summary>Whether <paramref name="directory"/> holds <paramref name="file"/>.</summary>
    public abstract bool Exists(string directory, string file);

    /// <summary>
    /// The cel's bytes, or null if it is not there. SEEKABLE: Avalonia's decoder seeks,
    /// and an AssetManager stream cannot, so a store that cannot promise seeking must
    /// buffer before returning.
    /// </summary>
    public abstract Stream? Open(string directory, string file);

    /// <summary>
    /// Whether <see cref="Open"/>'s null meant "NOT HERE" or "NOT YET".
    ///
    /// <para>
    /// A FILESYSTEM HAS NO THIRD ANSWER and this is false for every store that reads one.
    /// A browser does: its only way to get bytes is <c>fetch</c>, which is asynchronous, on
    /// a thread that must not block. So its <see cref="Open"/> returns null the first time
    /// a cel is asked for, starts the request, and answers properly once it lands.
    /// </para>
    ///
    /// <para>
    /// IT MATTERS BECAUSE <c>SciArt</c> CACHES THE ANSWER. A twin that resolves to null is
    /// recorded as "this cel has no high-resolution version" and never asked for again —
    /// correct for a missing file, and permanent 1990 pixels for one that was merely still
    /// in flight. A store that returns true here is saying: do not write that down.
    /// </para>
    /// </summary>
    public virtual bool Pending(string directory, string file) => false;

    /// <summary>
    /// Raised by a store when bytes it was waiting for have arrived, so whatever is on
    /// screen can be drawn again now that there is something better to draw it with.
    ///
    /// <para>
    /// Null on every head that cannot answer "not yet", because nothing will ever raise it.
    /// </para>
    /// </summary>
    public static event Action? Arrived;

    /// <summary>For a store to call once a batch of fetches has landed.</summary>
    protected static void NotifyArrived() => Arrived?.Invoke();

    /// <summary>The store every head gets unless it installs its own.</summary>
    public static readonly ArtStore FileSystem = new FileSystemArtStore();
}

/// <summary>
/// Art as files under an <c>assets/</c> directory: the desktop head, the tools, and the
/// tests. The root is either the one a head handed over or found by walking up from the
/// executable, exactly as <c>Program.FindAssetRoot</c> walks up looking for
/// <c>assets\audio\speech</c>, so a build run from anywhere under the repo finds it.
/// </summary>
internal sealed class FileSystemArtStore : ArtStore
{
    /// <summary>Set by <see cref="RenderScale.UseAssetRoot"/>; null means walk.</summary>
    public string? ExplicitRoot { get; set; }

    /// <remarks>
    /// EVERY STEP IS GUARDED. <c>AppContext.BaseDirectory</c> is not always a repository
    /// checkout and may not be a usable path at all, and <c>Directory.GetParent</c> throws
    /// on an empty one rather than returning null. A head with no upscaled art must get to
    /// factor 1 by finding nothing, never by throwing on the way.
    /// </remarks>
    public override string? Locate(string name)
    {
        try
        {
            if (ExplicitRoot is { Length: > 0 } root)
            {
                var direct = Path.Combine(root, name);
                if (Directory.Exists(direct)) return direct;
            }

            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                var candidate = Path.Combine(dir, "assets", name);
                if (Directory.Exists(candidate)) return candidate;
                dir = Directory.GetParent(dir)?.FullName;
            }
        }
        catch (Exception e)
        {
            StartupLog.Blame($"RenderScale.Locate({name}) (falling back to the 1x art)", e);
        }

        return null;
    }

    public override bool Exists(string directory, string file)
        => File.Exists(Path.Combine(directory, file));

    public override Stream? Open(string directory, string file)
    {
        var path = Path.Combine(directory, file);
        try
        {
            return File.Exists(path) ? File.OpenRead(path) : null;
        }
        catch
        {
            // A cel that will not open is a cel the screen omits, same as a missing one.
            return null;
        }
    }
}
