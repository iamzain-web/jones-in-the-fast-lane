using System;
using System.IO;
using System.Linq;
using System.Text;
using Jones.Core.Sci;

namespace Jones.Tests;

/// <summary>
/// The menu bar against `MenuBar::init` (`Menu.sc:121-137`).
///
/// The point of these is that the four <c>AddMenu</c> strings in
/// <see cref="SciMenuBar"/> are measured against the GAME'S OWN BYTES —
/// `assets/raw/script/997.script` — rather than against the decompiled listing, which
/// renders a space inside a string literal as <c>_</c> and would have silently eaten the
/// trailing space on three of these items (CLAUDE.md §2).
/// </summary>
public class SciMenuTests
{
    private static string AssetRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "assets");
            if (Directory.Exists(Path.Combine(candidate, "raw", "script"))) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("could not find assets/raw/script above the test binary");
    }

    /// <summary>
    /// Script 997's bytes as Latin-1, so a byte-for-byte substring search works and the
    /// <c>0x01</c> in the first menu's title survives the round trip.
    /// </summary>
    private static string RawScript997() =>
        Encoding.Latin1.GetString(
            File.ReadAllBytes(Path.Combine(AssetRoot(), "raw", "script", "997.script")));

    // ------------------------------------------------------------------
    // The strings are the script's
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(" \u0001 ")]
    [InlineData("About JONES `#0:Help `#1")]
    [InlineData(" Game ")]
    [InlineData("Save Game`#5:Set Save Directory `^Y:Restore Game`#7:--!:Quit `^Q:Restart `#9")]
    [InlineData(" Options ")]
    [InlineData("Delete Current Player `^Z:Change Animation Speed `^S:Graphics Detail Level `^T :--!:Change Volume `^V:Turn Messages Off `#8")]
    [InlineData(" Status ")]
    [InlineData("Statistics`#4:Goals`#6")]
    public void Every_AddMenu_string_appears_verbatim_in_script_997(string literal) =>
        Assert.Contains(literal, RawScript997(), StringComparison.Ordinal);

    [Fact]
    public void The_four_bar_titles_are_the_ones_the_script_passes()
    {
        var bar = SciMenuBar.Build();

        Assert.Equal(4, bar.Count);
        Assert.Equal(" \u0001 ", bar[0].Title);
        Assert.Equal(" Game ", bar[1].Title);
        Assert.Equal(" Options ", bar[2].Title);
        Assert.Equal(" Status ", bar[3].Title);
    }

    /// <summary>
    /// The first menu's title is not a word. `Menu.sc:121` passes <c>{ \01 }</c> — a space,
    /// byte <c>0x01</c> and a space — which is glyph 1 of font 0, a 9x8 shape.
    /// </summary>
    [Fact]
    public void The_first_title_is_character_one_and_not_a_letter()
    {
        var title = SciMenuBar.Build()[0].Title;

        Assert.Equal(3, title.Length);
        Assert.Equal('\u0001', title[1]);
    }

    // ------------------------------------------------------------------
    // The ids
    // ------------------------------------------------------------------

    /// <summary>
    /// Every id the rest of the game refers to, against the item it must land on. These are
    /// the numbers in `Menu.sc`'s own `SetMenu`, `GetMenu` and `switch` branches, so if the
    /// <c>(menu &lt;&lt; 8) | item</c> arithmetic were wrong, this is where it would show.
    /// </summary>
    [Theory]
    [InlineData(257, "About JONES ")]
    [InlineData(258, "Help ")]
    [InlineData(513, "Save Game")]
    [InlineData(514, "Set Save Directory ")]
    [InlineData(515, "Restore Game")]
    [InlineData(517, "Quit ")]
    [InlineData(518, "Restart ")]
    [InlineData(769, "Delete Current Player ")]
    [InlineData(770, "Change Animation Speed ")]
    [InlineData(771, "Graphics Detail Level ")]
    [InlineData(773, "Change Volume ")]
    [InlineData(774, "Turn Messages Off ")]
    [InlineData(1025, "Statistics")]
    [InlineData(1026, "Goals")]
    public void Each_id_lands_on_its_item(int id, string text) =>
        Assert.Equal(text, Find(id).Text);

    /// <summary>
    /// THE SEPARATOR CONSUMES AN ITEM NUMBER. This is the whole reason Quit is 517 rather
    /// than 516 and Change Volume is 773 rather than 772, and getting it wrong would put
    /// every handler in the Game and Options menus one line out.
    /// </summary>
    [Theory]
    [InlineData(516)]
    [InlineData(772)]
    public void The_separators_take_their_own_ids(int id)
    {
        var item = Find(id);

        Assert.True(item.IsSeparator);
        Assert.Equal("", item.Text);
    }

    [Fact]
    public void The_only_separators_are_those_two()
    {
        var separators = SciMenuBar.Build()
            .SelectMany(m => m.Items)
            .Where(i => i.IsSeparator)
            .Select(i => i.Id);

        Assert.Equal([516, 772], separators.ToArray());
    }

    // ------------------------------------------------------------------
    // The accelerators
    // ------------------------------------------------------------------

    /// <summary>
    /// The function keys, which are what made these commands look keyboard-only. Note 257:
    /// `` `#0 `` is F10, and text 997[6]'s "F10- About Jones" is the game agreeing.
    /// </summary>
    [Theory]
    [InlineData(257, 10)]
    [InlineData(258, 1)]
    [InlineData(513, 5)]
    [InlineData(515, 7)]
    [InlineData(518, 9)]
    [InlineData(774, 8)]
    [InlineData(1025, 4)]
    [InlineData(1026, 6)]
    public void The_function_key_accelerators(int id, int fkey)
    {
        var item = Find(id);

        Assert.Equal(SciMenuAccelerator.Function, item.Accelerator);
        Assert.Equal(fkey, item.FunctionKey);
    }

    [Theory]
    [InlineData(514, 'Y')]
    [InlineData(517, 'Q')]
    [InlineData(769, 'Z')]
    [InlineData(770, 'S')]
    [InlineData(771, 'T')]
    [InlineData(773, 'V')]
    public void The_control_key_accelerators(int id, char letter)
    {
        var item = Find(id);

        Assert.Equal(SciMenuAccelerator.Ctrl, item.Accelerator);
        Assert.Equal(letter, item.CtrlKey);
    }

    /// <summary>
    /// Text 997[6] and 997[7] are the game's own statement of its keyboard map, so the two
    /// have to agree: every accelerator the bar declares must be named in the help, and the
    /// help must name no key the bar does not bind. This is the check that caught nothing
    /// yet and would catch a mistyped item string instantly.
    /// </summary>
    [Fact]
    public void Every_accelerator_is_a_key_the_help_text_names()
    {
        var expected = new[]
        {
            "F1", "F4", "F5", "F6", "F7", "F8", "F9", "F10",
            "Ctrl-Q", "Ctrl-S", "Ctrl-T", "Ctrl-V", "Ctrl-Y", "Ctrl-Z",
        };

        var actual = SciMenuBar.Build()
            .SelectMany(m => m.Items)
            .Select(i => i.Accelerator switch
            {
                SciMenuAccelerator.Function => $"F{i.FunctionKey}",
                SciMenuAccelerator.Ctrl => $"Ctrl-{i.CtrlKey}",
                _ => null,
            })
            .Where(k => k is not null)
            .OrderBy(k => k)
            .ToArray();

        Assert.Equal(expected.OrderBy(k => k).ToArray(), actual);
    }

    // ------------------------------------------------------------------
    // The spaces, and what this build does NOT have
    // ------------------------------------------------------------------

    /// <summary>
    /// Three items end in a space and three do not, in the same menu. The decompiled
    /// listing draws a space inside a literal as <c>_</c>, so this is exactly the
    /// difference CLAUDE.md §2 exists to protect.
    /// </summary>
    [Fact]
    public void The_trailing_spaces_are_kept_exactly_where_the_bytes_have_them()
    {
        Assert.EndsWith(" ", Find(514).Text, StringComparison.Ordinal);
        Assert.EndsWith(" ", Find(771).Text, StringComparison.Ordinal);
        Assert.EndsWith(" ", Find(769).Text, StringComparison.Ordinal);

        Assert.Equal("Save Game", Find(513).Text);
        Assert.Equal("Restore Game", Find(515).Text);
        Assert.Equal("Statistics", Find(1025).Text);
        Assert.Equal("Goals", Find(1026).Text);
    }

    /// <summary>
    /// The five `MenuBar::init` switches off the instant it has built the bar
    /// (`Menu.sc:132-136`) — the same five `room1.sc:1296-1300` switches back on with the
    /// town board.
    /// </summary>
    [Fact]
    public void The_five_items_that_start_disabled()
    {
        Assert.Equal([769, 513, 515, 1025, 1026], SciMenuBar.DisabledUntilTheBoard.ToArray());

        foreach (var id in SciMenuBar.DisabledUntilTheBoard)
            Assert.False(Find(id).IsSeparator);
    }

    /// <summary>
    /// THIS IS THE CD BUILD'S BAR. The floppy's Options menu opens with "Change Reading
    /// Speed `^R" and ends with "Turn Music Off `#2" and "Turn Sound Effects Off `#3"
    /// (`jones-dos-1.000.060/src/Menu.sc:128`); the CD build has none of the three, which
    /// is why its own help text, 997[6], lists no F2 and no F3.
    /// </summary>
    [Theory]
    [InlineData("Reading")]
    [InlineData("Music")]
    [InlineData("Sound Effects")]
    public void The_floppy_only_options_are_not_on_this_bar(string fragment)
    {
        var texts = SciMenuBar.Build().SelectMany(m => m.Items).Select(i => i.Text);

        Assert.DoesNotContain(texts, t => t.Contains(fragment, StringComparison.Ordinal));
    }

    [Fact]
    public void Nothing_on_the_bar_is_reachable_by_two_accelerators()
    {
        var keys = SciMenuBar.Build()
            .SelectMany(m => m.Items)
            .Where(i => i.Accelerator != SciMenuAccelerator.None)
            .Select(i => (i.Accelerator, i.FunctionKey, i.CtrlKey))
            .ToArray();

        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    private static SciMenuItem Find(int id) =>
        SciMenuBar.Build().SelectMany(m => m.Items).Single(i => i.Id == id);
}
