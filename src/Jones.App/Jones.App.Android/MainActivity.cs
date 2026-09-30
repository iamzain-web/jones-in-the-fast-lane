using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace Jones.App.Android;

/// <summary>
/// SCREEN SHAPE. The game is 320x200 - 8:5 - and a phone is nothing like it: a modern
/// handset is about 20:9 in landscape and the reciprocal in portrait, so there is always
/// a wide band of screen the playfield cannot use.
///
/// MainView's Viewbox is Stretch="Uniform" and the control's background is black, so the
/// playfield is fitted to whichever dimension runs out first and the remainder is black
/// bars - pillarbox in landscape, letterbox in portrait. That is the right trade and it is
/// the one the desktop head already makes when the window is dragged off 8:5: distorting
/// Sierra's pixels to fill the last few rows would be worse than a margin.
///
/// ScreenOrientation.FullUser rather than a forced landscape: the game plays in both, the
/// composition is identical either way because the whole 320x200 space scales as one unit,
/// and forcing an orientation on someone holding a phone one-handed is not a decision this
/// port needs to make. Landscape simply uses more of the glass.
///
/// ConfigurationChanges keeps the activity alive across a rotation, so the view models -
/// which hold the entire game state - are not torn down and rebuilt when the phone turns.
/// </summary>
[Activity(
    Label = "Jones in the Fast Lane",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ScreenOrientation = ScreenOrientation.FullUser,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>
    /// Watched rather than left bare: this is where Avalonia stands the view up, so it is
    /// where a fault in the view, the view model or the renderer surfaces — and an activity
    /// that throws here is an app that "installs and does not open". The rethrow is
    /// deliberate; there is no half-started activity worth keeping. See
    /// <see cref="AndroidLog"/>.
    /// </summary>
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        AndroidLog.Install();
        AndroidLog.Info("MainActivity.OnCreate");

        // The menu's Quit. FinishAndRemoveTask rather than Finish, so a game the player
        // quit does not sit in the recent-apps list looking as if it were still open.
        // OnDestroy then sees IsFinishing and gives the audio devices back.
        Views.MainView.QuitWithoutWindow = FinishAndRemoveTask;

        try
        {
            base.OnCreate(savedInstanceState);
            AndroidLog.Info("MainActivity.OnCreate: done");
        }
        catch (System.Exception e)
        {
            AndroidLog.Error("MainActivity.OnCreate", e);
            throw;
        }
    }

    protected override void OnResume()
    {
        AndroidLog.Info("MainActivity.OnResume");
        base.OnResume();
    }

    protected override void OnPause()
    {
        // Backgrounded: nothing of the game's should keep playing out of the app.
        JonesRuntime.Pause();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        // IsFinishing distinguishes the user actually leaving from a teardown Android
        // intends to follow with a rebuild. Only the first should give the audio devices
        // back; the second would silence a game that is about to carry on.
        if (IsFinishing) JonesRuntime.Stop();
        base.OnDestroy();
    }
}
