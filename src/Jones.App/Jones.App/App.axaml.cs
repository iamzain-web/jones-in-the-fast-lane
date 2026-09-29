using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Jones.App.ViewModels;
using Jones.App.Views;

namespace Jones.App;

public partial class App : Application
{
    public override void Initialize()
    {
        StartupLog.Say("App.Initialize: loading XAML");
        AvaloniaXamlLoader.Load(this);
        StartupLog.Say("App.Initialize: XAML loaded");

        AttachDeveloperToolsWhereTheyWork();
    }

    /// <summary>
    /// `AvaloniaUI.DiagnosticsSupport`, on the heads that can actually host it.
    ///
    /// NOT A PLAIN `#if DEBUG`, AND THE REASON MATTERS. This assembly targets plain
    /// <c>net10.0</c> and is compiled ONCE for every head, so there is no <c>ANDROID</c>
    /// compile constant here to test — the Android head consumes this same DLL. The gate has
    /// to be a RUNTIME one.
    ///
    /// Developer Tools is desktop tooling: it stands up a connection bridge to a separate
    /// tools process on the same machine. On a phone there is no such process, and this app's
    /// manifest deliberately requests no permissions at all — including INTERNET — so
    /// anything it does with a socket fails, and it fails inside <c>Initialize</c>, before
    /// there is a window, a view or a log line. That is the shape of "installs and does not
    /// open", so it is gated off rather than left to chance, and wrapped besides: a debugging
    /// aid must never be the thing that stops the game starting.
    /// </summary>
    private void AttachDeveloperToolsWhereTheyWork()
    {
#if DEBUG
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() ||
            OperatingSystem.IsTvOS() || OperatingSystem.IsBrowser())
        {
            StartupLog.Say("App.Initialize: developer tools skipped (mobile/browser head)");
            return;
        }

        StartupLog.Try("App.Initialize: developer tools", () => this.AttachDeveloperTools());
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        StartupLog.Say(
            $"App.OnFrameworkInitializationCompleted: lifetime is {ApplicationLifetime?.GetType().FullName ?? "<null>"}");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = NewGameScreen()
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            // THE LAMBDA RUNS LATER, on the activity's own schedule, and nothing above it
            // catches what it throws — a view model constructor that faults here kills the
            // process with no window ever having existed. So the whole of it is watched.
            singleViewFactoryApplicationLifetime.MainViewFactory =
                () => StartupLog.Watch("MainViewFactory", () => new MainView { DataContext = NewGameScreen() });
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = NewGameScreen()
            };
        }

        base.OnFrameworkInitializationCompleted();
        StartupLog.Say("App.OnFrameworkInitializationCompleted: done");
    }

    /// <summary>
    /// The game state and its first screen. Watched because this is where everything the
    /// heads set up is first READ — the text resources, the sound player, the art — so a head
    /// that got one of them wrong faults here rather than where it went wrong.
    /// </summary>
    private static MainViewModel NewGameScreen() =>
        StartupLog.Watch("new MainViewModel", () =>
        {
            // Network play, when the command line asked for it — see Net.NetLaunch. A
            // joiner has no game of its own; a host is the ordinary game plus a listener.
            if (Net.NetLaunch.JoinAddress is { } address)
                return Net.NetClient.Start(address, Net.NetLaunch.Port);

            var vm = new MainViewModel();
            if (Net.NetLaunch.Host) _host = Net.NetHost.Start(vm, Net.NetLaunch.Port);
            return vm;
        });

    /// <summary>Held for the life of the app, so the listener is not collected.</summary>
    private static Net.NetHost? _host;
}
