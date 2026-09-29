using System;
using AContent = global::Android.Content;

namespace Jones.App.Android;

/// <summary>
/// Everything the Windows head does in <c>Program.Main</c> before Avalonia starts, done
/// where an Android app can do it: from the Application object, which is the first thing
/// the process creates and the only place with a Context before there is an Activity.
///
/// The two heads must stay recognisably the same list, so this is deliberately in the
/// same order as Program.Main and says where and why it differs.
/// </summary>
internal static class JonesRuntime
{
    private static bool _loaded;

    /// <summary>The live player, or null between a finish and the next start.</summary>
    public static AndroidAudioPlayer? Sound { get; private set; }

    public static void Start(AContent.Context context)
    {
        LoadOnce(context);
        StartAudio(context);
    }

    /// <summary>
    /// The parts that only ever have to happen once in a process: they load static caches
    /// that nothing invalidates.
    /// </summary>
    private static void LoadOnce(AContent.Context context)
    {
        if (_loaded) return;
        _loaded = true;

        // HOUSE RULE, applied per head rather than in the core so that Jones.Core stays a
        // faithful port and its tests keep measuring Sierra's game rather than ours. See
        // Program.Main and Player.StartingFoodWeeks for the full citation trail: without
        // this, a fresh game opens by taking 20 of the week's 60 hours for hunger.
        //
        // Set this to false to play Sierra's version exactly.
        Jones.Core.Model.TurnStart.SkipWeekOneHardship = true;

        // The assets. The desktop head walks up the tree looking for assets/audio/speech
        // and gets null if it is not there; here they travel in the APK, so the equivalent
        // step is unpacking the small ones. GameAssets swallows its own failures, so a
        // broken unpack leaves the same state a desktop build with no assets/ is in: the
        // game runs, silent, printing nothing it cannot find.
        GameAssets.Unpack(context);
        var assets = GameAssets.Root(context);

        // The game's own text resources are not audio and not platform-specific: every
        // head needs the strings the game PRINTS.
        SciText.Load(assets);

        // NOT CALLED: RenderScale.UseAssetRoot. The 24MB of upscaled art is not in the
        // APK (see the csproj), and RenderScale falls back to Factor 1 - the original's
        // own pixels, scaled up by the Viewbox with interpolation None - when it cannot
        // find a png{N}x directory. Calling it with a root that has no png4x in it would
        // change nothing; leaving it out says so.

        Audio.LipSync.SetDirectory(System.IO.Path.Combine(assets, "audio", "sync"));
        Audio.Subtitles.Load(assets);

        // The touch affordances in MainView. There is no hover on a phone and no right or
        // middle button, so the two mouse chords that reach the Goals and Statistics
        // screens need somewhere to be pressed. Set before the UI is built: MainView reads
        // it once, through x:Static.
        TouchUi.Enabled = true;
    }

    /// <summary>
    /// Stands the audio up, or stands it back up after <see cref="Stop"/>. Speech comes
    /// from the APK's assets rather than from the unpacked root - it is the one part of
    /// the asset set that is never unpacked.
    /// </summary>
    private static void StartAudio(AContent.Context context)
    {
        if (Sound is not null) return;

        Sound = new AndroidAudioPlayer(context.Assets!, GameAssets.Root(context));
        ViewModels.MainViewModel.Sound = Sound;

        // Program.Main's last step before Avalonia starts, done in the same place relative to
        // the player: the master switch lives on the player, so the settings can only be
        // applied once there is one. Unlike the desktop head this runs again after a
        // Stop()/Start(), which is correct - Stop() installs a fresh SilentAudioPlayer whose
        // Enabled is back to its default, and this puts the player's own choice back on it.
        //
        // WHERE THE FILE GOES. SaveStore.Directory is ApplicationData, which on Android is
        // the app's own private files directory, so this head needs no override - the same
        // reason the save game needs none.
        ViewModels.MainViewModel.LoadSoundSettings();
    }

    /// <summary>
    /// Backgrounded. Nothing of the original's is playing that should follow the player
    /// out of the app, and the three calls here are the ones the scripts themselves use to
    /// clear each channel.
    ///
    /// The bed is CUT rather than faded, and does not come back on resume: the game starts
    /// a bed when it enters a location, so it returns at the next screen change rather
    /// than the moment the app does. Restarting it here would mean this head deciding when
    /// music plays, which is the game's decision and not the port's.
    /// </summary>
    public static void Pause()
    {
        if (Sound is not { } sound) return;
        sound.StopSpeech();
        sound.CutMusic();
        sound.StopEffects();
    }

    /// <summary>
    /// The counterpart of Program.Main's teardown, for a real exit rather than a
    /// backgrounding. Android will reclaim the process anyway, but handing the AudioTracks
    /// back properly beats leaving the mixer to find out.
    /// </summary>
    public static void Stop()
    {
        Sound?.Dispose();
        Sound = null;

        // The shared app is silent by default and every call site goes through this
        // property, so putting the do-nothing player back means a view model that outlives
        // the activity cannot fault on a disposed track.
        ViewModels.MainViewModel.Sound = new Jones.App.Audio.SilentAudioPlayer();
    }
}
