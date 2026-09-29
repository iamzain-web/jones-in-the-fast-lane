using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Jones.App.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            // The Android equivalent of everything Program.Main does before
            // StartWithClassicDesktopLifetime: the house rule, the assets, the text
            // resources, the lip sync, the subtitles and the sound. It has to run before
            // the App is built, because App.OnFrameworkInitializationCompleted constructs
            // the MainViewModel, and the view model reads MainViewModel.Sound and the
            // loaded SciText the moment it builds its first screen.
            JonesRuntime.Start(this);

            return base.CustomizeAppBuilder(builder)
            .WithInterFont();
        }
    }
}
