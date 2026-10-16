using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jones.App;
using Jones.Core.Model;
using Jones.Core.Text;

namespace Jones.Tests;

/// <summary>
/// THE EMPLOYMENT OFFICE'S NINE EMPLOYER LABELS, against the names the rest of the game uses
/// and against the column they are drawn in.
///
/// <para>
/// WHY THIS EXISTS. The renamed board shipped with this menu still listing Sierra's nine —
/// "Z-Mart Discount", "Monolith Burgers", "Socket City Appliance" — while every sign outside
/// said something else, and 683 tests passed the whole time. Nothing compared these strings
/// to <see cref="CastNames"/>, so renaming a building and renaming its employer line were two
/// unrelated acts and one of them was simply never done.
/// </para>
///
/// <para>
/// WORSE, AND THE REASON THE SECOND TEST IS HERE: the table carried "Open Door University"
/// UNCONDITIONALLY. Running with Sierra's own art, her employment office offered a job at a
/// building that does not exist in her game. That is the defect <see cref="CastNames"/> was
/// written to remove, pointing the other way, and it survived because the one renamed string
/// looked like progress rather than like a bug.
/// </para>
///
/// <para>
/// AND THE THIRD: these nine <c>nsLeft</c> values are NOT a left margin. Each was measured so
/// its own label sits on the column's single optical centre — 58.5±0.5 in font 10. A rename
/// that does not re-measure its left silently moves that label off the centre, which is
/// exactly what a reader notices and no existing test could see.
/// </para>
/// </summary>
public class EmployerListTests
{
    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, "assets", "raw", "font"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("could not find assets/raw/font above the test binary");
    }

    private static readonly Lazy<BitmapFontMetrics> Font10 = new(() =>
        BitmapFontMetrics.From(File.ReadAllBytes(
            Path.Combine(Root(), "assets", "raw", "font", "10.font")))!);

    /// <summary>The centre the original holds, from the nine labels Sierra drew.</summary>
    private const double Centre = 58.5;

    /// <summary>
    /// The panel behind the column ends at 180; nothing may reach it.
    /// </summary>
    private const int PanelRight = 180;

    [Fact]
    public void SierrasOwnListIsUntouched()
    {
        // The port's fidelity claim about this menu: with no art set, these are her words.
        Assert.Equal(
            ["Z-Mart Discount", "Monolith Burgers", "QT Clothing", "Socket City Appliance",
             "Hi-Tech University", "Factory", "Bank", "Black's Market", "Rent Office"],
            StoreLayout.Employment.Select(e => e.Label));

        // And the selector must hand hers back for anything that is not our set, INCLUDING
        // null - a set asked for and not found leaves the player looking at her buildings.
        Assert.Same(StoreLayout.Employment, StoreLayout.EmploymentFor(null));
        Assert.Same(StoreLayout.Employment, StoreLayout.EmploymentFor("sierra"));
        Assert.Same(StoreLayout.EmploymentCast0, StoreLayout.EmploymentFor("cast0"));
    }

    /// <summary>
    /// EVERY EMPLOYER LINE SAYS WHAT THE REST OF THE GAME CALLS THAT BUILDING. This is the
    /// test that was missing: the board said RENTFLIX in paint, `WhereText` said Rentflix in
    /// words, and this menu said "Rent Office", and nothing in the suite related the three.
    /// </summary>
    [Fact]
    public void OurListAgreesWithTheNamesTheGamePrints()
    {
        foreach (var (label, place, _, _, _) in StoreLayout.EmploymentCast0)
        {
            var location = Board.ForWorkplace(place);
            Assert.NotNull(location);

            // CastNames is the single answer to "what is this building called"; the employer
            // line is one of the places that answer is drawn.
            var expected = CastNameTable.NameIn("cast0", location!.Id) ?? location.Name;
            Assert.Equal(expected, label);
        }
    }

    /// <summary>
    /// EACH LABEL SITS ON THE COLUMN'S OPTICAL CENTRE, hers and ours alike. A rename without
    /// a re-measured `nsLeft` fails here rather than on a screenshot.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("cast0")]
    public void EveryLabelIsCentredInItsColumn(string? set)
    {
        foreach (var (label, _, left, _, _) in StoreLayout.EmploymentFor(set))
        {
            var width = Font10.Value.Measure(label);
            var centre = left + width / 2.0;

            Assert.True(Math.Abs(centre - Centre) <= 0.5,
                $"'{label}' is {width}px at nsLeft {left}, centre {centre:0.0} — " +
                $"the column's centre is {Centre}±0.5. nsLeft should be " +
                $"{Math.Round(Centre - width / 2.0)}.");
        }
    }

    /// <summary>
    /// AND NOTHING REACHES THE PANEL'S EDGE. "The Industrial Revolution" is 111px against the
    /// longest line Sierra ever drew here at 92, so this is the claim that keeps a longer name
    /// from running off the box even when it is correctly centred.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("cast0")]
    public void NoLabelOverrunsThePanel(string? set)
    {
        foreach (var (label, _, left, _, _) in StoreLayout.EmploymentFor(set))
        {
            Assert.True(left >= 0, $"'{label}' starts at nsLeft {left}, outside the dialog.");
            Assert.True(left + Font10.Value.Measure(label) <= PanelRight,
                $"'{label}' ends at {left + Font10.Value.Measure(label)}, past {PanelRight}.");
        }
    }

    /// <summary>
    /// THE ACCELERATORS ARE THE ORIGINAL'S AND DO NOT MOVE WITH THE NAMES. `Item::handleEvent`
    /// matches on the key, not the label, so a rename must not renumber them.
    /// </summary>
    [Fact]
    public void RenamingDoesNotRenumberTheAccelerators()
    {
        Assert.Equal(
            StoreLayout.Employment.Select(e => (e.Place, e.Key, e.Top)),
            StoreLayout.EmploymentCast0.Select(e => (e.Place, e.Key, e.Top)));
    }
}
