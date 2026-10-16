using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Jones.App;

/// <summary>
/// A bound collection that can be REBUILT without being torn down.
///
/// <para>
/// WHY THIS EXISTS. <c>BuildScreen</c> cleared nine <see cref="ObservableCollection{T}"/>s
/// and refilled them. <c>Clear()</c> raises a Reset, and an <c>ItemsControl</c> answers a
/// Reset by destroying every container it has and building them all again — so a screen
/// with sixty sprites destroyed and recreated sixty controls, with their templates,
/// bindings and images, every single time anything on it moved.
/// </para>
///
/// <para>
/// WHAT THAT COST, MEASURED RATHER THAN SUPPOSED. While the marble walks, BuildScreen runs
/// from TWO timers — the walk tick at 40Hz and the smooth tick at ~60Hz — about a hundred
/// full teardowns a second. The desktop absorbed it. The browser has one thread with the
/// event loop on it, and the marble advances one path step PER TICK, so ticks that arrive
/// late are a journey that takes longer: "the mans walk is very slow, and it takes a very
/// long time for him to get to where he is going."
/// </para>
///
/// <para>
/// WHAT IT DOES. Between <see cref="BeginBuild"/> and <see cref="EndBuild"/> every add is
/// held in a staging list and nothing is raised at all. <see cref="EndBuild"/> then walks
/// the two lists together: equal items are LEFT ALONE, differing ones are replaced in place
/// (one Replace, which an ItemsControl answers by handing the existing container a new
/// item), and only the difference in length is added or removed. A screen where nothing
/// moved raises nothing; a screen where the marble moved raises one notification.
/// </para>
///
/// <para>
/// <c>Count</c> IS SHADOWED, DELIBERATELY, AND THAT IS NOT DECORATION. <c>BuildBoard</c>
/// records <c>_clockSpriteIndex = Sprites.Count</c> midway through a build to remember
/// where it put the clock, so during a build <c>Count</c> must mean "how many have been
/// added so far", not "how many were on screen before this started". The <c>new</c> member
/// is what every call site through this type sees; Avalonia reaches the collection through
/// <see cref="System.Collections.ICollection"/> and gets the real one, which is correct for
/// it because nothing is in the collection yet.
/// </para>
/// </summary>
public sealed class BuildList<T> : ObservableCollection<T>
{
    private readonly List<T> _staged = [];
    private bool _building;

    /// <summary>
    /// How many items this list holds — during a build, how many have been added BY the
    /// build. See the type's remarks: <c>_clockSpriteIndex = Sprites.Count</c> depends on it.
    /// </summary>
    public new int Count => _building ? _staged.Count : base.Count;

    /// <summary>
    /// Start a rebuild. Replaces <c>Clear()</c>: the items on screen stay exactly where they
    /// are, and what the build produces is compared against them at the end.
    /// </summary>
    public void BeginBuild()
    {
        _staged.Clear();
        _building = true;
    }

    protected override void InsertItem(int index, T item)
    {
        // Mid-build adds are staged and raise nothing. Outside a build this is an ordinary
        // collection, which is what ReplaceSprite and the balloon rely on.
        if (_building) { _staged.Add(item); return; }
        base.InsertItem(index, item);
    }

    /// <summary>
    /// Commit the rebuild, touching only what actually differs.
    /// </summary>
    /// <param name="comparer">
    /// How to decide that an item has not changed. The default is the type's own
    /// <c>Equals</c> — see <c>SpriteVm</c>, which treats two sprites as the same when they
    /// draw the same bitmap at the same place AND NEITHER TAKES CLICKS. That last clause is
    /// not fussiness: a clickable sprite carries a freshly wrapped command and registers
    /// itself as a touch target, so keeping the old instance would leave the screen holding
    /// a control the touch layout no longer knows about.
    /// </param>
    public void EndBuild(IEqualityComparer<T>? comparer = null)
    {
        if (!_building) return;
        _building = false;

        var same = comparer ?? EqualityComparer<T>.Default;
        var shared = Math.Min(base.Count, _staged.Count);

        for (var i = 0; i < shared; i++)
            if (!same.Equals(base[i], _staged[i]))
                base.SetItem(i, _staged[i]);

        // Shrink from the end: removing from the end moves nothing after it.
        while (base.Count > _staged.Count)
            base.RemoveItem(base.Count - 1);

        for (var i = base.Count; i < _staged.Count; i++)
            base.InsertItem(base.Count, _staged[i]);

        _staged.Clear();
    }
}
