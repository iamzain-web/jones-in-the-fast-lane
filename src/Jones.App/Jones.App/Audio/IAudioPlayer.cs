using System;

namespace Jones.App.Audio;

/// <summary>
/// Sound playback, behind an interface because the two heads cannot share an
/// implementation: Windows has System.Media/NAudio, Android has its own MediaPlayer, and
/// neither works on the other. Same split as the SmbSpace project's ISmbClient.
///
/// Nothing in the game logic references this â€” the core stays silent and headless.
/// </summary>
public interface IAudioPlayer
{
    /// <summary>
    /// Plays a speech clip by its original audio resource id (10..991). These are the
    /// shopkeepers' lines; `proc0_18` in the scripts picks the id, and each location has
    /// its own band.
    /// </summary>
    void PlaySpeech(int audioId);

    /// <summary>
    /// How far into the current speech clip playback actually is, or null when nothing
    /// is speaking.
    ///
    /// The lip sync MUST follow this rather than a wall clock started when Play() was
    /// called. Device wake-up and buffering mean audio can begin a second or more after
    /// the call returns, and a wall clock then runs the mouth to a standstill before a
    /// word comes out.
    /// </summary>
    TimeSpan? SpeechPosition { get; }

    /// <summary>
    /// Cuts the current line off mid-word. Every location script does this as its dialog
    /// closes â€” `(DoAudio audSTOP)` appears in all thirteen of them plus room1, weekend
    /// and goalsDefine (e.g. `employment.sc:346`, `bank.sc:134`, `market.sc:156`),
    /// immediately before `(gASong fade:)`. Walking out of a shop silences the shopkeeper.
    /// </summary>
    void StopSpeech();

    /// <summary>Starts a music track looping â€” the "bed" a location plays under itself.</summary>
    void PlayMusic(int soundResource, bool loop = true);

    /// <summary>`gASong fade:` â€” the five-second fade every location exit performs.</summary>
    void StopMusic();

    /// <summary>
    /// `gASong stop:` â€” a CUT, which is a different call from the fade and is used where
    /// something takes the screen over: `newspaper.sc:203`, `lottoScript.sc:42`,
    /// `muggedByMarket.sc:30` and `room1.sc:1499`.
    /// </summary>
    void CutMusic();

    /// <summary>
    /// `gASong pause: 1`. Paired with <see cref="PlayEffect"/>'s
    /// <c>resumeMusicWhenDone</c>, which is the `gASong` argument in
    /// `(gASoundEffect play: 44 gASong)` â€” the bed ducks under the sting and comes back
    /// when it ends.
    /// </summary>
    void PauseMusic();

    /// <summary>
    /// A one-shot effect: the button click, a sting, the eviction notice.
    ///
    /// <paramref name="loop"/> is `(gASoundEffect loop: -1 play: 25)`, ended by
    /// <see cref="EndEffectLoop"/>. <paramref name="resumeMusicWhenDone"/> is the second
    /// argument in `(gASoundEffect play: 44 gASong)`.
    /// </summary>
    void PlayEffect(int soundResource, bool loop = false, bool resumeMusicWhenDone = false);

    /// <summary>`(gASoundEffect loop: 1)` â€” `lottoScript.sc:232`.</summary>
    void EndEffectLoop();

    /// <summary>
    /// `gASoundEffect2 play:` â€” the second effect object (`Main.sc:1297`), used where an
    /// effect has to sound over one already playing. `room1.sc:1500` is its only call.
    /// </summary>
    void PlayEffect2(int soundResource);

    /// <summary>`(gASoundEffect stop:)` + `(gASoundEffect2 stop:)` â€” `winnerScript.sc:67-68`.</summary>
    void StopEffects();

    bool Enabled { get; set; }

    /// <summary>
    /// Switches between the music and effects written for this port and Sierra's arrangements on
    /// the emulated AdLib card. **TRUE IS NOW THE DEFAULT**: every resource the scripts can
    ///
    /// It exists so the two can be heard back to back on the same cue, in the room the cue
    /// belongs to, which is the only way to judge whether the replacement is actually
    /// better. Flipping it restarts the bed that is playing on the other synthesiser; it
    /// does not need a restart and does not reopen the audio device.
    ///
    /// A cue with no original version written yet still comes from the AdLib path even
    /// with this on, per cue, so the set can be filled in a batch at a time without any
    /// point at which half the game is silent.
    ///
    /// Defaulted here rather than required, so the network and silent players â€” which have
    /// no synthesiser of their own â€” do not have to care.
    /// </summary>
    bool UseOriginalAudio { get => true; set { } }
}

/// <summary>Does nothing. Used until a head supplies a real player, and in tests.</summary>
public sealed class SilentAudioPlayer : IAudioPlayer
{
    public void PlaySpeech(int audioId) { }
    public TimeSpan? SpeechPosition => null;
    public void StopSpeech() { }
    public void PlayMusic(int soundResource, bool loop = true) { }
    public void StopMusic() { }
    public void CutMusic() { }
    public void PauseMusic() { }
    public void PlayEffect(int soundResource, bool loop = false, bool resumeMusicWhenDone = false) { }
    public void EndEffectLoop() { }
    public void PlayEffect2(int soundResource) { }
    public void StopEffects() { }
    public bool Enabled { get; set; }
}

/// <summary>
/// The exact clip a location plays when you walk in, read from each script's greeting
/// call: `(if (proc0_14) (= tmp (Random lo hi)) (proc0_18 (+ tmp base) ...))`.
///
/// The offsets are NOT uniform. The Employment Office greets with
/// `420 + Random(6, 12)` â€” starting at offset SIX â€” because ids 420..425 are the
/// job-application outcomes. Assuming every band greets from its base is exactly how
/// you end up being congratulated on a job as you walk through the door.
///
/// `proc0_14` latches per player per location, so the greeting is heard only on a
/// player's FIRST visit to that building.
/// </summary>
public static class SpeechBands
{
    /// <summary>Inclusive range of greeting clips for a location.</summary>
    public sealed record Band(int First, int Last);

    public static Band? For(Core.Model.LocationId id) => id switch
    {
        Core.Model.LocationId.QtClothing       => new Band(10, 18),    // 10  + Random(0,8)
        Core.Model.LocationId.Factory          => new Band(50, 57),    // 50  + Random(0,7)
        Core.Model.LocationId.PawnShop         => new Band(70, 77),    // 70  + Random(0,7)
        Core.Model.LocationId.SocketCity       => new Band(100, 115),  // 100 + Random(0,15)
        Core.Model.LocationId.RentOffice       => new Band(160, 167),  // 160 + Random(0,7)
        Core.Model.LocationId.Bank             => new Band(300, 309),  // 300 + Random(0,9)
        Core.Model.LocationId.ZMart            => new Band(330, 340),  // 330 + Random(0,10)
        Core.Model.LocationId.HiTechU          => new Band(380, 401),  // 380 + Random(0,21)
        Core.Model.LocationId.EmploymentOffice => new Band(426, 432),  // 420 + Random(6,12)
        Core.Model.LocationId.BlacksMarket     => new Band(530, 543),  // 530 + Random(0,13)
        Core.Model.LocationId.MonolithBurgers  => new Band(610, 629),  // 610 + Random(0,19)

        // The two apartments have no shopkeeper and no greeting.
        _ => null,
    };
}

/// <summary>
/// What a shopkeeper says when you buy something, and when you cannot afford it.
///
/// `boughtItem` and `notEnoughCash` are `Obj` instances in every shop script and both are
/// SPOKEN â€” there is not one `Print` in discount.sc, appliance.sc, clothing.sc, market.sc
/// or fastFood.sc. The purchase line is drawn from a band and never repeats the previous
/// one (`while (== localN (= temp0 (Random lo hi))) 1`), which is why each band is stored
/// as the already-offset first and last clip rather than a base plus a range.
///
/// Three more locations sell nothing but still have a refusal line of their own.
/// </summary>
public static class ShopSpeech
{
    /// <param name="BoughtFirst">First purchase clip, or 0 where the location sells nothing.</param>
    public sealed record Lines(int BoughtFirst, int BoughtLast, int NotEnoughCash);

    public static Lines? For(Core.Model.LocationId id) => id switch
    {
        Core.Model.LocationId.QtClothing      => new( 19,  39,  40), // clothing.sc   10 + Random(9,29)
        Core.Model.LocationId.SocketCity      => new(116, 146, 147), // appliance.sc 100 + Random(16,46)
        Core.Model.LocationId.PawnShop        => new( 78,  85,  86), // pawnShop.sc   70 + Random(8,15)
        Core.Model.LocationId.ZMart           => new(341, 356, 357), // discount.sc  330 + Random(11,26)
        Core.Model.LocationId.BlacksMarket    => new(545, 576, 577), // market.sc    530 + Random(15,46)
        Core.Model.LocationId.MonolithBurgers => new(630, 664, 665), // fastFood.sc  610 + Random(20,54)

        Core.Model.LocationId.RentOffice      => new(0, 0, 183),     // rentOffice.sc
        Core.Model.LocationId.Bank            => new(0, 0, 310),     // bank.sc
        Core.Model.LocationId.HiTechU         => new(0, 0, 409),     // university.sc

        _ => null,
    };
}

/// <summary>Music resource each location plays on entry (`gASong playBed:`).</summary>
public static class LocationMusic
{
    public static int For(Core.Model.LocationId id) => id switch
    {
        Core.Model.LocationId.SecurityApartments => 34,
        Core.Model.LocationId.RentOffice         => 35,
        Core.Model.LocationId.LowCostHousing     => 36,
        Core.Model.LocationId.ZMart              => 37,
        Core.Model.LocationId.MonolithBurgers    => 38,
        Core.Model.LocationId.QtClothing         => 39,
        Core.Model.LocationId.SocketCity         => 40,
        Core.Model.LocationId.HiTechU            => 41,
        Core.Model.LocationId.EmploymentOffice   => 43,
        Core.Model.LocationId.Factory            => 46,
        Core.Model.LocationId.Bank               => 47,
        Core.Model.LocationId.BlacksMarket       => 49,
        Core.Model.LocationId.PawnShop           => 50,
        _ => 0,
    };

    /// <summary>The town board's own theme.</summary>
    public const int Board = 5;

    /// <summary>The universal button click.</summary>
    public const int ButtonClick = 23;

    /// <summary>The winner's fanfare â€” `(gASong play: 7)`, `winnerScript.sc:69`.</summary>
    public const int Winner = 7;

    /// <summary>The weekend dialog's own track â€” `(gASong play: 9)`, `weekend.sc:183`.</summary>
    public const int Weekend = 9;

    /// <summary>The diploma sting â€” `(gASong loop: 1 play: 42 â€¦)`, `diploma.sc:59`.</summary>
    public const int Diploma = 42;
}

/// <summary>
/// The one-shot sound resources, each named for the call site that plays it. Every one is
/// a literal `(gASoundEffect play: n)` in the scripts; nothing here is a guess about what
/// a resource "sounds like".
/// </summary>
public static class SoundEffects
{
    /// <summary>Wild Willy â€” `muggedByMarket.sc:31` and `:95`, both muggings.</summary>
    public const int Mugging = 20;

    /// <summary>The ambulance â€” `startTrn.sc:1078`, `moveAmbulance` state 0.</summary>
    public const int Ambulance = 21;

    /// <summary>The newspaper unfolding â€” `newspaper.sc:204`.</summary>
    public const int Newspaper = 8;

    /// <summary>The lotto machine, played LOOPING â€” `lottoScript.sc:43`.</summary>
    public const int Lotto = 25;

    /// <summary>The eviction notice â€” `startTrn.sc:485`, ducking the song.</summary>
    public const int EvictionNotice = 27;

    /// <summary>The week's 60 Hours are gone â€” `room1.sc:1500`, on `gASoundEffect2`.</summary>
    public const int WeekOver = 29;

    /// <summary>
    /// Sacked. `n108.sc:29` when dependability has fallen too far to keep the job, and
    /// `startTrn.sc:705/737/770/803` when a market scandal costs a player theirs.
    /// </summary>
    public const int Sacked = 30;

    /// <summary>The work clock spinning down the shift â€” `WButton.sc:308`.</summary>
    public const int WorkClock = 31;

    /// <summary>Bad news, under a refusal â€” `employment.sc:36`, `rentOffice.sc:266`.</summary>
    public const int BadNews = 44;

    /// <summary>Good news, under a hire or a raise â€” `employment.sc:53`, `rentOffice.sc:259`.</summary>
    public const int GoodNews = 45;
}

