using System;
using System.Collections.Generic;

namespace Jones.Core.Text;

/// <summary>One piece of a laid-out line: some text, and where its pen starts.</summary>
/// <param name="X">Pen position in game pixels, relative to the line's own left edge.</param>
public readonly record struct TextRun(string Text, double X);

/// <summary>
/// A line after layout: the runs to draw, and where the value column's right edge landed.
/// </summary>
/// <param name="Width">
/// The line's full width. Normally identical to the stop it was asked for, because the value
/// is right-aligned onto it; larger only when <paramref name="Overflowed"/>.
/// </param>
/// <param name="Overflowed">
/// True when the label was too wide for the value to reach its stop without colliding, so the
/// value was pushed right instead. Nothing in the shipped game does this in Quicksand, and
/// <c>ColumnStopTests</c> asserts as much for every shop, job list and menu in the game.
/// </param>
public sealed record LaidOutLine(IReadOnlyList<TextRun> Runs, double Width, bool Overflowed);

/// <summary>
/// Turns one of the game's padded interface strings into runs positioned by MEASUREMENT.
///
/// <para>
/// THE PROBLEM THIS SOLVES. Every price and wage column in Jones is aligned by padding baked
/// into the label — trailing spaces, leader dots, and `|`, which font 10 draws as a ONE PIXEL
/// BLANK. In any other face `|` is a visible vertical bar, so pouring the shipped strings into
/// Quicksand renders <c>Janitor ||| $7 Hr.</c>; and the padding's widths mean nothing in a
/// proportional face anyway, so the columns scatter — 23px of spread across the Factory's nine
/// wages, which is 7% of a 320-pixel screen.
/// </para>
///
/// <para>
/// THE RESOURCE STRINGS ARE NOT EDITED, and that is the whole design. `ItemText` holds them
/// byte for byte from the raw scripts and that fidelity is load-bearing (`CLAUDE.md` §2 — the
/// decompiler renders a space as `_`, and porting that literally once drew rows of dashes
/// across the shop). So the padding is stripped HERE, at render time, and only for the
/// proportional face: the bitmap path still draws the original bytes, unchanged, and the two
/// can still be compared against each other and against the resource.
/// </para>
///
/// <para>
/// WHERE THE COLUMN STOP COMES FROM. Not from a table. The caller measures the ORIGINAL,
/// unmodified string in font 10 and hands that width in as the stop, so the value's right edge
/// lands exactly where the shipped game puts it — including the places where the shipped game
/// is itself a pixel out, like Black's Market's "Food For 4 Weeks..". Nothing is typed by eye
/// and nothing has to be kept in step by hand.
/// </para>
/// </summary>
public static class ColumnLayout
{
    /// <summary>
    /// The three characters the game pads with. A space is 5px in font 10, `.` is 3px and `|`
    /// is a 1px blank; five pipes are worth one space, which is the fine adjustment.
    /// </summary>
    public const string PaddingChars = " .|";

    /// <summary>
    /// The smallest gap left between a label and the value to its right, in ems. Only ever
    /// reached if a label is wide enough to crowd its own column, which none currently is.
    /// </summary>
    private const double MinGapEm = 0.40;

    /// <summary>
    /// Splits a padded label into the text that is really the label and the padding after it.
    ///
    /// <para>
    /// A TRAILING FULL STOP IS NOT ALWAYS PADDING. The Rent Office's lines are sentences —
    /// raw script 201 has `Pay rent for 1 month.` followed by two spaces, and
    /// `Ask For More Time.` followed by seven — so stripping every trailing dot would silently
    /// delete the punctuation the game wrote. A run containing TWO OR MORE dots is a leader and
    /// goes; a single dot is kept and only the spaces and pipes after it are dropped.
    /// </para>
    /// </summary>
    public static (string Text, bool Leader) StripPadding(string label)
    {
        if (string.IsNullOrEmpty(label)) return (label ?? "", false);

        var end = label.Length;
        while (end > 0 && PaddingChars.IndexOf(label[end - 1]) >= 0) end--;

        var dots = 0;
        for (var i = end; i < label.Length; i++) if (label[i] == '.') dots++;

        if (dots >= 2) return (label[..end], true);

        // One dot or none: keep everything up to and including the last dot, drop the rest.
        var keep = label.Length;
        while (keep > 0 && (label[keep - 1] == ' ' || label[keep - 1] == '|')) keep--;
        return (label[..keep], false);
    }

    /// <summary>
    /// Lays one line out.
    /// </summary>
    /// <param name="label">The resource's label, padding and all. Never modified.</param>
    /// <param name="value">
    /// The value that sits in the right-hand column — `$89`, `$12 Hr.` — or empty for a line
    /// that has none. Leading spaces are dropped: they are the format string's own padding
    /// (text 206 index 0 spends a second space standing in for a missing digit) and mean
    /// nothing once the column is positioned by measurement.
    /// </param>
    /// <param name="stop">
    /// The value's right edge, in game pixels from the line's left — i.e. the width of the
    /// whole original string in font 10.
    /// </param>
    /// <param name="measure">Measures a string in the face being drawn.</param>
    /// <param name="emSize">The face's em size in game pixels, for the minimum gap.</param>
    public static LaidOutLine Lay(string label, string value, double stop,
                                  Func<string, double> measure, double emSize)
    {
        ArgumentNullException.ThrowIfNull(measure);

        var (text, leader) = StripPadding(label ?? "");
        value = (value ?? "").TrimStart(' ');

        var runs = new List<TextRun>(3);
        var textWidth = measure(text);
        if (text.Length > 0) runs.Add(new TextRun(text, 0));

        if (value.Length == 0)
            return new LaidOutLine(runs, textWidth, false);

        var valueWidth = measure(value);
        var minGap = MinGapEm * emSize;

        var valueX = stop - valueWidth;
        var overflow = false;

        if (valueX < textWidth + minGap)
        {
            valueX = textWidth + minGap;
            overflow = true;
        }

        if (leader) AddLeader(runs, textWidth, valueX, measure, emSize);

        runs.Add(new TextRun(value, valueX));
        return new LaidOutLine(runs, Math.Max(stop, valueX + valueWidth), overflow);
    }

    /// <summary>
    /// Refills a dot leader across the gap the measurement opened up.
    ///
    /// <para>
    /// The dots are INK, not padding: the shop lists read `Cheeseburger.......| $89` on screen
    /// and dropping the run would change what the game looks like, not just how it is spaced.
    /// So a label whose padding contained a leader gets one again, drawn in the new face at
    /// the new width. A label padded only with spaces and pipes — every job title, every bank
    /// and rent line — gets nothing, because it had nothing.
    /// </para>
    /// </summary>
    private static void AddLeader(List<TextRun> runs, double from, double to,
                                  Func<string, double> measure, double emSize)
    {
        var dot = measure(".");
        if (dot <= 0) return;

        var pad = emSize * 0.25;
        double start = from + pad, end = to - pad;

        var count = (int)Math.Floor((end - start) / dot);
        if (count < 2) return;

        runs.Add(new TextRun(new string('.', count), start));
    }
}
