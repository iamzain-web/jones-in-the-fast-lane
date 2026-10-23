using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jones.Tests;

/// <summary>
/// THE THREE HEADS AGREE ABOUT HOW THE GAME LOOKS.
///
/// <para>
/// Zain: "Whenever changes are made, ALL versions need to look the same. I don't want the
/// Mobile version and the Web Version to look different because testers are going to be
/// testing all 3 platforms."
/// </para>
///
/// <para>
/// He is describing a failure that had already happened twice. The week-one house rule was
/// set by Desktop and Android and NOT by the browser, so the web build alone opened on
/// Sierra's starvation notice and he took it for an old build. Smooth motion was turned off
/// on the browser alone as a performance workaround, so the marble glided on two platforms
/// and stepped on the third. Neither was noticed by anything; both were found by a person
/// looking at two builds and seeing different games.
/// </para>
///
/// <para>
/// A COMMENT SAYING "KEEP THESE IN STEP" WOULD NOT HAVE CAUGHT EITHER. These settings live
/// in three different startup files, in three projects, written months apart - the only
/// thing that can see all three at once is something that reads all three. So this does
/// exactly that: it parses the heads' startup sources and compares the switches that decide
/// what the player sees.
/// </para>
///
/// <para>
/// IT IS A SOURCE-LEVEL TEST AND THAT IS DELIBERATE. Jones.Tests cannot reference Jones.App
/// - net8.0 and toolkit-free, by design - and even if it could, these are assignments made
/// during startup rather than values it could read. Reading the text is the only instrument
/// available, and a crude instrument that fires beats a precise one that does not exist.
/// </para>
/// </summary>
public class PlatformParityTests
{
    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, "src", "Jones.App"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("could not find src/Jones.App above the test binary");
    }

    private static readonly (string Head, string Path)[] Heads =
    [
        ("Desktop", Path.Combine("src", "Jones.App", "Jones.App.Desktop", "Program.cs")),
        ("Android", Path.Combine("src", "Jones.App", "Jones.App.Android", "JonesRuntime.cs")),
        ("Browser", Path.Combine("src", "Jones.App", "Jones.App.Browser", "Program.cs")),
    ];

    private static string Source(string relative) => File.ReadAllText(Path.Combine(Root(), relative));

    /// <summary>
    /// What a head ASSIGNS to a setting, ignoring anything commented out, or null when it
    /// leaves the shared default alone.
    /// </summary>
    private static string? Assigned(string source, string setting)
    {
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("//", StringComparison.Ordinal)) continue;

            var m = Regex.Match(line, Regex.Escape(setting) + @"\s*=\s*([A-Za-z0-9_.]+)\s*;");
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    /// <summary>
    /// THE SETTINGS THAT CHANGE WHAT THE PLAYER SEES. Every one of these must read the same
    /// on all three heads - either assigned the same value, or assigned by none of them and
    /// left at the shared default.
    /// </summary>
    [Theory]
    [InlineData("TurnStart.SkipWeekOneHardship")]   // whether week one opens on starvation
    [InlineData("MainViewModel.SmoothMotion")]      // whether the marble glides or steps
    [InlineData("PanelScale.Requested")]            // how big an interior is drawn
    [InlineData("UiFont.Requested")]                // which typeface the game is set in
    public void EveryHeadAgrees(string setting)
    {
        var answers = Heads
            .Select(h => (h.Head, Value: Assigned(Source(h.Path), setting)))
            .ToArray();

        var distinct = answers.Select(a => a.Value ?? "<default>").Distinct().ToArray();

        Assert.True(distinct.Length == 1,
            $"the heads disagree about {setting}: "
            + string.Join(", ", answers.Select(a => $"{a.Head}={a.Value ?? "<default>"}"))
            + ". All three are tested by the same people on the same build; a difference "
            + "here is reported as a bug on whichever platform they saw second.");
    }

    /// <summary>
    /// TOUCH IS THE ONE LEGITIMATE DIFFERENCE, and it is pinned here so that it stays the
    /// only one and stays deliberate.
    ///
    /// <para>
    /// A phone has no mouse pointer: the tap targets and the menu bar are sized for a
    /// finger, which is an INPUT affordance rather than a look. Forcing finger-sized
    /// controls onto the desktop, or taking them off the phone, makes one platform worse to
    /// use in order to make a screenshot match. If that trade is ever wanted the other way,
    /// this test is where to say so.
    /// </para>
    /// </summary>
    [Fact]
    public void TouchIsOnWhereThereIsNoPointerAndNowhereElse()
    {
        string? Touch(string head) =>
            Assigned(Source(Heads.Single(h => h.Head == head).Path), "TouchUi.Enabled");

        Assert.Null(Touch("Desktop"));          // a mouse: leave it off
        Assert.Equal("true", Touch("Android"));
        Assert.Equal("true", Touch("Browser")); // opened on a phone more often than not
    }
}
