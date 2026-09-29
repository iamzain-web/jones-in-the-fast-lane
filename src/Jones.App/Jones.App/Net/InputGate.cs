using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Jones.App.Net;

/// <summary>
/// Decides whether THIS machine's mouse and keyboard may act right now.
///
/// <para>
/// In a networked game a seat belongs either to the host's machine or to a joiner. While a
/// joiner's player is acting, the host's own clicks must do nothing — the joiner is the one
/// playing — and while anyone else is acting, the joiner's input is refused at the host (see
/// <see cref="NetHost"/>). The screen is identical on both, so the host still SEES the
/// joiner's every move; it just cannot make one.
/// </para>
///
/// <para>
/// Every clickable element's command goes through <see cref="Command"/>, so the gate sits at
/// the one place a click turns into an action. Timers and the game's own internal calls never
/// pass through here, which is what keeps an animation that finishes during a joiner's turn
/// from being blocked.
/// </para>
///
/// Outside a networked game <see cref="LocalMayAct"/> is always true and nothing changes.
/// </summary>
public static class InputGate
{
    /// <summary>Set by <see cref="NetHost"/>. True unless a connected joiner owns the acting seat.</summary>
    public static Func<bool> LocalMayAct { get; set; } = () => true;

    /// <summary>
    /// Non-zero while the host is carrying out a joiner's input, which has already been
    /// checked against the joiner's own seat.
    /// </summary>
    [ThreadStatic] private static int _remoteDepth;

    public static bool Allows => _remoteDepth > 0 || LocalMayAct();

    /// <summary>Runs a joiner's already-authorised input as if it had been clicked here.</summary>
    public static void AsRemote(Action act)
    {
        _remoteDepth++;
        try { act(); }
        finally { _remoteDepth--; }
    }

    /// <summary>A click handler that does nothing when this machine may not act.</summary>
    public static ICommand Command(Action run) => new RelayCommand(() =>
    {
        if (Allows) run();
    });

    public static ICommand Command(Action run, Func<bool> canExecute) => new RelayCommand(() =>
    {
        if (Allows) run();
    }, canExecute);
}
