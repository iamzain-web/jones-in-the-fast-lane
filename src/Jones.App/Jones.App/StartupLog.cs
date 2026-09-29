using System;

namespace Jones.App;

/// <summary>
/// Where the shared app reports its own startup, for a head that has somewhere to send it.
///
/// THIS EXISTS BECAUSE A SILENT DEATH IS UNDIAGNOSABLE. The desktop head fails in front of a
/// console and a debugger; the Android head fails inside a process the phone kills before
/// anything is drawn, and the only evidence anyone can collect afterwards is `adb logcat`.
/// The shared layers must therefore be able to say what step they were on without knowing
/// what platform they are on — so this is two delegates and nothing else. Both are null on a
/// head that has not set them (the desktop head does not), and every call site tolerates that.
///
/// See <c>Jones.App.Android.AndroidLog</c> for the Android end, which points these at
/// <c>Android.Util.Log</c> under the tag <c>JonesFastLane</c>.
/// </summary>
public static class StartupLog
{
    /// <summary>A step that went as expected.</summary>
    public static Action<string>? Info { get; set; }

    /// <summary>A step that threw. The message says which step; the exception says why.</summary>
    public static Action<string, Exception>? Error { get; set; }

    public static void Say(string message)
    {
        // A logger that throws must not be the thing that takes the game down.
        try { Info?.Invoke(message); } catch (Exception) { }
    }

    public static void Blame(string step, Exception e)
    {
        try { Error?.Invoke(step, e); } catch (Exception) { }
    }

    /// <summary>
    /// Runs a startup step that MUST succeed, reporting anything it throws before letting it
    /// through. The rethrow is deliberate: where a step is load-bearing, swallowing its
    /// failure only moves the crash somewhere less informative.
    /// </summary>
    public static T Watch<T>(string step, Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        try
        {
            var result = work();
            Say($"{step}: ok");
            return result;
        }
        catch (Exception e)
        {
            Blame(step, e);
            throw;
        }
    }

    /// <inheritdoc cref="Watch{T}(string, Func{T})"/>
    public static void Watch(string step, Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        try
        {
            work();
            Say($"{step}: ok");
        }
        catch (Exception e)
        {
            Blame(step, e);
            throw;
        }
    }

    /// <summary>
    /// Runs a startup step whose failure the game can survive, reporting it and carrying on.
    /// Used where the desktop head already tolerates the same loss — a missing asset, a dead
    /// audio device — so that the two heads degrade the same way rather than one crashing
    /// where the other shrugs.
    /// </summary>
    public static void Try(string step, Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        try
        {
            work();
            Say($"{step}: ok");
        }
        catch (Exception e)
        {
            Blame($"{step} (continuing without it)", e);
        }
    }
}
