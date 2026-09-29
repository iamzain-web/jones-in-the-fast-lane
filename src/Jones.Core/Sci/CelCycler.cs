namespace Jones.Core.Sci;

/// <summary>
/// `Cycle` (`Motion.sc:8-58`) and `Fwd` (`Motion.sc:75-89`), for the actors that drive their
/// cels off `ticksToDo`. This decides HOW FAST THE PICTURE CHANGES and nothing else — no
/// position, no duration, no game state.
///
/// <para>
/// THE PORT MODELLED THIS NOWHERE. It knew about `cycleSpeed` and counted game cycles for it,
/// which is right for the actors that declare one; every actor that declares `ticksToDo`
/// instead had its cels tied to whatever timer happened to be driving it — the walker to the
/// marble's 25ms step, Willy to his own 100ms move cadence. Neither number is the game's.
/// </para>
///
/// <para>
/// THE RULE, from `Cycle::nextCel` (`Motion.sc:26-44`):
/// <code>
/// (method (nextCel)
///     (if ticksToDo
///         (if (or (u&lt; (+ ticksToDo lastTime) (GetTime)) …)
///             (if (&amp; (client signal:) $1000)
///                 (client cel:)                    ; frozen by setCel:
///             else
///                 (= lastTime (GetTime))
///                 (self changeCel:)                ; cel + cycleDir
///             )
///         else
///             (client cel:)                        ; too early — same cel again
///         )
///     else
///         (++ cycleCnt)                            ; the cycleSpeed branch
///         …
/// </code>
/// Three things follow, and all three are why this class exists:
/// <list type="number">
/// <item>The gate is <b>`GetTime`</b>, the interpreter's wall clock in 60ths of a second. It
///   is not the game cycle, so the cel rate is independent of how fast anything moves.</item>
/// <item>The comparison is <b>strict</b> (`u&lt;`), so the cel is still current when
///   `GetTime` equals `lastTime + ticksToDo`: a cel holds `ticksToDo + 1` whole ticks.</item>
/// <item>`ticksToDo` of <b>0</b> is not "instant", it selects the entirely different
///   `cycleSpeed` branch — a count of game cycles. That branch is NOT this class's job; the
///   port already counts cycles for the actors that use it (the shop `items` parade at 300,
///   the diploma at 1, the win sequence's stars at 4-6). Constructing this with 0 throws
///   rather than quietly animating something the original animates another way.</item>
/// </list>
/// </para>
///
/// <para>
/// WHAT THE PORT DOES WITH IT. The cel is advanced from the wall clock at draw time, exactly
/// as <c>MainViewModel.WalkerFrame</c> already did for the walker: the stepping timers, the
/// hours a journey costs and the real time it takes are all untouched, and only the choice of
/// picture changes. <see cref="Advance"/> is therefore safe to call at any rate — it is
/// idempotent between cel boundaries and catches up across them.
/// </para>
///
/// <para>
/// THE CLOCK IS KEPT IN TICKS, NOT MILLISECONDS, because `GetTime` is an integer count of
/// 60ths and a cel boundary landing exactly on a tick has to fire. Held as milliseconds it did
/// not: 11 ticks is 183.333…ms, which no `double` represents, so the 24th boundary of a walk
/// came out a fraction of a nanosecond short and the walker lost a cel. Counting what the
/// original counts makes the arithmetic exact and the question disappear.
/// </para>
///
/// <para>
/// ONE DELIBERATE DIFFERENCE, and it is in the port's favour: the original resamples
/// `(= lastTime (GetTime))` when a cel changes, so it loses the remainder and drifts by up to
/// one polling interval per cel. Its interpreter polls at 60Hz, so that is at most one tick.
/// This advances `lastTime` by whole cels instead, because the port's poll is its redraw and
/// resampling would make the animation rate depend on the frame rate — which is the class of
/// bug this whole file exists to remove.
/// </para>
/// </summary>
public sealed class CelCycler
{
    /// <summary>One `GetTime` tick. SCI counts 60ths of a second.</summary>
    public const double TickMs = 1000.0 / 60.0;

    /// <summary>
    /// How long one cel holds, in milliseconds. `ticksToDo + 1` because `Cycle::nextCel`'s
    /// comparison is strict — see the class remarks.
    /// </summary>
    public static double CelMs(int ticksToDo) => (ticksToDo + 1) * TickMs;

    /// <summary>
    /// `GetTime`: the port's millisecond wall clock counted the way the interpreter counts,
    /// in whole 60ths of a second. Truncating, because a tick has not happened until it has.
    /// </summary>
    public static long Ticks(long nowMs) => Math.Max(0, nowMs) * 60 / 1000;

    /// <summary>
    /// A stall this many cels long stops being caught up and is resynchronised instead, so a
    /// debugger break or a suspended phone does not spin through thousands of cel changes on
    /// resume. The original could not fall behind at all; this is the port's own guard.
    /// </summary>
    private const long CatchUpLimit = 600;

    private long _lastTick;
    private bool _celFixed;

    /// <param name="ticksToDo">The actor's declared `ticksToDo`. Must be positive.</param>
    public CelCycler(int ticksToDo)
    {
        if (ticksToDo <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(ticksToDo), ticksToDo,
                "ticksToDo 0 selects Cycle::nextCel's cycleSpeed branch (Motion.sc:45-57), "
                + "which counts game cycles and is not modelled here.");

        TicksToDo = ticksToDo;
    }

    /// <summary>The actor's declared `ticksToDo`.</summary>
    public int TicksToDo { get; }

    /// <summary>The cel showing now. `client cel:`.</summary>
    public int Cel { get; private set; }

    /// <summary>
    /// True while the cycler is free to change the cel — false after <see cref="FixCel"/>,
    /// which is the `(&amp; (client signal:) $1000)` branch.
    /// </summary>
    public bool Cycling => !_celFixed;

    /// <summary>
    /// `Prop::setCycle` with a non-zero cycler type (`Actor.sc:219-231`): it clears signal
    /// $1000 and then `Cycle::init` (`Motion.sc:19-24`) resamples `lastTime`.
    ///
    /// <para>
    /// IT DOES NOT RESET THE CEL, and that is not an oversight in this port — `Cycle::init`
    /// touches `client`, `ticksToDo`, `cycleCnt` and `lastTime`, and none of those is the
    /// cel. Where a script wants cel 0 it says so separately, as `muggedByBank` state 4 does
    /// with `(willy setLoop: 4 cel: 0 setCycle: Fwd)`; `muggedByMarket` state 4 does NOT say
    /// it, so Willy's cel really does carry over from the previous loop there.
    /// </para>
    /// </summary>
    public void Restart(long nowMs)
    {
        _celFixed = false;
        _lastTick = Ticks(nowMs);
    }

    /// <summary>
    /// A direct `cel: n` property write, which is what the scripts use next to `setCycle:`.
    /// Unlike <see cref="FixCel"/> it does not stop the cycler, because it does not set
    /// signal $1000.
    /// </summary>
    public void PutCel(int cel) => Cel = Math.Max(0, cel);

    /// <summary>
    /// `Actor::setCel` (`Actor.sc:143-163`): clamps to `lastCel` and sets signal $1000, which
    /// makes `Cycle::nextCel` return the same cel for ever after. The two poses Willy freezes
    /// in are this, and `(willy setLoop: 7 setCel: 1)` asks for cel 1 of a one-cel loop —
    /// the original clamps it to 0 here rather than drawing a hole.
    /// </summary>
    public void FixCel(int cel, int cels)
    {
        Cel = Math.Clamp(cel, 0, Math.Max(0, cels - 1));
        _celFixed = true;
    }

    /// <summary>
    /// `Fwd::doit` (`Motion.sc:78-84`) via `Cycle::nextCel`. Returns the cel to draw.
    /// </summary>
    /// <param name="nowMs">The port's wall clock, standing in for `GetTime`.</param>
    /// <param name="cels">`NumCels` of the loop the actor is in RIGHT NOW. It changes under
    /// the cycler when a script calls `setLoop:` without a new `setCycle:`, which
    /// `muggedByBank` states 1 and 2 both do.</param>
    public int Advance(long nowMs, int cels)
    {
        if (cels <= 0) return Cel = 0;

        // `(if (> (= newCel (self nextCel:)) (client lastCel:)) (self cycleDone:))`, and
        // `Fwd::cycleDone` is `(client cel: 0)`. This runs whether or not the clock fired,
        // so a setLoop: into a shorter loop snaps to cel 0 on the very next doit.
        if (Cel > cels - 1) Cel = 0;

        if (_celFixed) return Cel;

        // `(u< (+ ticksToDo lastTime) (GetTime))`, strict — so the cel holds `ticksToDo + 1`
        // whole ticks. All integers, exactly as the interpreter has it.
        var hold = TicksToDo + 1;
        var now = Ticks(nowMs);
        var behind = now - _lastTick;
        if (behind < hold) return Cel;

        var steps = behind / hold;

        if (steps > CatchUpLimit)
        {
            _lastTick = now;
            steps %= cels;
        }
        else
        {
            _lastTick += steps * hold;
        }

        Cel = (int)((Cel + steps) % cels);
        return Cel;
    }

    /// <summary>
    /// How far through the current cel the wall clock is, 0 to just under 1.
    ///
    /// PORT-ONLY, and not part of the original: it exists so that the interpolated in-between
    /// walk art can be picked. Nothing the game decides reads it, and a head that draws only
    /// the original's cels can ignore it entirely.
    /// </summary>
    public double Phase(long nowMs)
    {
        if (_celFixed) return 0;

        var t = (nowMs - _lastTick * TickMs) / CelMs(TicksToDo);
        if (t <= 0) return 0;
        return t >= 1 ? 1 : t;
    }
}
