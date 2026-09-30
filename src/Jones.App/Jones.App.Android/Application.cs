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

        /// <summary>
        /// The first managed code in the process, and therefore the only place that can put a
        /// logger in front of everything else. See <see cref="AndroidLog"/> for why a head
        /// with no logging is a head whose failures cannot be investigated.
        /// </summary>
        public override void OnCreate()
        {
            AndroidLog.Install();
            AndroidLog.Info("Application.OnCreate");

            try
            {
                base.OnCreate();
                AndroidLog.Info("Application.OnCreate: done");
            }
            catch (System.Exception e)
            {
                AndroidLog.Error("Application.OnCreate", e);
                throw;
            }
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            // Installed here as well as in OnCreate: the order in which Android calls the two
            // is the runtime's business, not ours, and Install() is idempotent.
            AndroidLog.Install();
            AndroidLog.Info("CustomizeAppBuilder");

            // The Android equivalent of everything Program.Main does before
            // StartWithClassicDesktopLifetime: the house rule, the assets, the text
            // resources, the lip sync, the subtitles and the sound. It has to run before
            // the App is built, because App.OnFrameworkInitializationCompleted constructs
            // the MainViewModel, and the view model reads MainViewModel.Sound and the
            // loaded SciText the moment it builds its first screen.
            //
            // NOT ALLOWED TO TAKE THE APP DOWN. Every step inside is individually survivable
            // and says so in the log; this catch is the backstop for the ones that are not,
            // because a game that opens silent and wordless is worth far more than a game
            // that does not open.
            try
            {
                JonesRuntime.Start(this);
            }
            catch (System.Exception e)
            {
                AndroidLog.Error("JonesRuntime.Start", e);
            }

            return base.CustomizeAppBuilder(builder)
            .WithInterFont();
        }
    }
}
