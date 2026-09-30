using System;
using System.Text;
using System.Threading.Tasks;
using ARuntime = global::Android.Runtime;
using AUtil = global::Android.Util;

namespace Jones.App.Android;

/// <summary>
/// THE ONE THING THIS HEAD DID NOT HAVE: somewhere for a startup failure to be seen.
///
/// An Avalonia Android app that faults before its first view exists dies inside a process
/// Android has already torn down, with nothing on screen and nothing written anywhere the
/// person holding the phone can reach. The only evidence that survives is `adb logcat`, so
/// everything the startup path does is reported there under one tag.
///
/// <para>
/// THE TAG IS <see cref="Tag"/> AND IT IS 23 CHARACTERS OR FEWER, because Android's logging
/// truncates longer ones and a truncated tag is a filter that silently matches nothing.
/// `tools/android-debug.ps1` filters on exactly this string.
/// </para>
///
/// <para>
/// THREE HANDLERS, because managed exceptions reach Android by three different routes and
/// only one of them is the ordinary one:
/// <list type="bullet">
/// <item><c>AndroidEnvironment.UnhandledExceptionRaiser</c> — the Java/managed boundary,
/// which is where an exception thrown inside an Activity callback actually surfaces. This is
/// the one that catches a startup fault, and it fires BEFORE the runtime decides to abort.</item>
/// <item><c>AppDomain.CurrentDomain.UnhandledException</c> — a managed thread dying,
/// including the audio start-up thread.</item>
/// <item><c>TaskScheduler.UnobservedTaskException</c> — a faulted Task nobody awaited, which
/// otherwise says nothing at all until a GC gets round to it.</item>
/// </list>
/// None of them can stop the crash. They exist so that the crash explains itself.
/// </para>
/// </summary>
internal static class AndroidLog
{
    /// <summary>The logcat tag. Every line this app writes carries it.</summary>
    public const string Tag = "JonesFastLane";

    private static bool _installed;

    /// <summary>
    /// Points <see cref="StartupLog"/> at logcat and hooks the three unhandled-exception
    /// routes. Called first thing in <c>Application.OnCreate</c>, before anything that can
    /// fail — if this is not the first thing, the first failure is the one that goes unheard.
    /// </summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;

        StartupLog.Info = Info;
        StartupLog.Error = (step, e) => Error(step, e);

        ARuntime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
        {
            Error("UNHANDLED (Android)", e.Exception);

            // Left unhandled deliberately. Marking it handled lets a process whose startup
            // has already failed limp on in a state nothing has reasoned about, which is a
            // worse thing to debug than a clean crash with a logged cause.
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Error("UNHANDLED (AppDomain)", ex);
            else Error($"UNHANDLED (AppDomain): {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Error("UNOBSERVED (Task)", e.Exception);
            e.SetObserved();
        };

        Info($"logging installed; tag={Tag}");
    }

    public static void Info(string message)
    {
        try { AUtil.Log.Info(Tag, message); } catch (Exception) { }
    }

    public static void Error(string message)
    {
        try { AUtil.Log.Error(Tag, message); } catch (Exception) { }
    }

    /// <summary>
    /// The exception and every inner one, because the interesting line is almost never the
    /// outer message — a TypeInitializationException says nothing, and the thing that threw
    /// inside the static constructor says everything.
    /// </summary>
    public static void Error(string step, Exception e)
    {
        try
        {
            var text = new StringBuilder();
            text.Append("FAILED: ").Append(step);

            var depth = 0;
            for (Exception? at = e; at is not null && depth < 8; at = at.InnerException, depth++)
            {
                text.AppendLine();
                text.Append(depth == 0 ? "  " : "  caused by: ")
                    .Append(at.GetType().FullName)
                    .Append(": ")
                    .Append(at.Message);

                if (at.StackTrace is { Length: > 0 } stack)
                    text.AppendLine().Append(stack);
            }

            AUtil.Log.Error(Tag, text.ToString());
        }
        catch (Exception)
        {
            // Nothing left that could report this.
        }
    }
}
