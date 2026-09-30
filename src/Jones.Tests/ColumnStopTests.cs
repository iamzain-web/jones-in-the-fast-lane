using Jones.App;
using Jones.Core.Economy;
using Jones.Core.Model;
using Jones.Core.Text;

namespace Jones.Tests;

/// <summary>
/// THE COLUMN STOPS, ASSERTED RATHER THAN DESCRIBED.
///
/// <para>
/// Until now the alignment invariants of this game lived only in doc comments: `ItemText`
/// says Socket City stops at 179 and 112, `StoreLayout` says Monolith stops at 110 and 177,
/// and nothing anywhere checked it. Every one of those numbers is a consequence of a padded
/// string in `ItemText` measured in font 10, so deleting one space from one label would slide
/// a whole column and no test would notice.
/// </para>
///
/// <para>
/// These tests measure the game's OWN font resource and the port's OWN tables, and they cover
/// both faces: font 10's stops are checked against the numbers the source documents, and the
/// Quicksand layout is checked to land the value's right edge on exactly the same stop.
/// </para>
/// </summary>
public class ColumnStopTests
{
    // ---- the two faces, read from the repository ---------------------------

    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "assets", "raw", "font"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("could not find assets/raw/font above the test binary");
    }

    private static readonly Lazy<BitmapFontMetrics> Font10 = new(() =>
        BitmapFontMetrics.From(File.ReadAllBytes(
            Path.Combine(Root(), "assets", "raw", "font", "10.font")))!);

    /// <summary>
    /// The face as the game ships it — <c>src/Jones.App/Jones.App/Assets/fonts</c>, not
    /// <c>tools/fonts</c>, so that a test failure means the SHIPPED font changed.
    /// </summary>
    private static readonly Lazy<TrueTypeMetrics> Quicksand = new(() =>
        TrueTypeMetrics.From(
            File.ReadAllBytes(Path.Combine(Root(), "src", "Jones.App", "Jones.App",
                                           "Assets", "fonts", "Quicksand.ttf")),
            new Dictionary<string, double> { ["wght"] = 500 })!);

    private static double Em => Quicksand.Value.EmForCapHeight(Font10.Value.CapHeight);

    private static double Measure(string s) => Quicksand.Value.Measure(s, Em);

    private static LaidOutLine Lay(string full, string label, string value) =>
        ColumnLayout.Lay(label, value, Font10.Value.Measure(full), Measure, Em);

    /// <summary>Every line the game can draw, as (left edge, full string, label, value).</summary>
    private static IEnumerable<(int Left, string Full, string Label, string Value, string Where)> AllLines()
    {
        // The economy moves every price and wage, and the format strings BRANCH on the
        // figure — under $100 against at or above, under $10 against at or above, under
        // $1000 against at or above. Sweeping the index exercises both arms of each.
        int[] indices = [60, 80, 100, 120, 150];

        foreach (var index in indices)
        {
            foreach (var (store, stock, slots) in new (LocationId, StockItem[], StoreLayout.Slot[])[]
            {
                (LocationId.SocketCity, Catalogue.SocketCity, StoreLayout.SocketCity),
                (LocationId.QtClothing, Catalogue.QtClothing, StoreLayout.QtClothing),
                (LocationId.BlacksMarket, Catalogue.BlacksMarket, StoreLayout.BlacksMarket),
                (LocationId.MonolithBurgers, Catalogue.MonolithBurgers, StoreLayout.MonolithBurgers),
            })
            {
                var order = StoreLayout.ScriptOrder(store)!;
                var ordered = stock.OrderBy(s => Array.IndexOf(order, s.Name)).ToArray();

                for (var i = 0; i < ordered.Length && i < slots.Length; i++)
                {
                    var item = ordered[i];
                    var price = item.PriceAt(index);
                    var label = ItemText.For(store, item.Name);
                    var line = store == LocationId.BlacksMarket
                        ? ItemText.BlacksMarketLine(item.Name, label, price)
                        : store == LocationId.SocketCity
                            ? ItemText.ApplianceLine(label, price)
                            : ItemText.PriceLine(label, price);

                    yield return (slots[i].Left, line.Full, label, line.Value,
                                  $"{store} {item.Name} @{index}");
                }
            }

            // Z-Mart lays out at runtime (a random six of eighteen), so every one of its
            // eighteen lines has to hold the stop on its own.
            foreach (var item in Catalogue.ZMart)
            {
                var price = item.PriceAt(index);
                var label = ItemText.For(LocationId.ZMart, item.Name);
                var line = ItemText.ApplianceLine(label, price);
                yield return (11, line.Full, label, line.Value, $"Z-Mart {item.Name} @{index}");
            }

            // The nine job lists.
            foreach (var job in Jobs.All)
            {
                var wage = Pricing.Price(index, job.BaseWage);
                var label = ItemText.For(job);
                var line = ItemText.WageLine(label, wage);
                yield return (StoreLayout.JobLeft, line.Full, label, line.Value,
                              $"{job.Workplace} {job.Title} @{index}");
            }
        }

        // The Bank and the Rent Office: labels from the raw scripts, prices from the game.
        foreach (var (slot, price) in new (StoreLayout.Slot, int)[]
        {
            (StoreLayout.RentOffice[0], 325), (StoreLayout.RentOffice[2], 325),
            (StoreLayout.RentOffice[3], 475), (StoreLayout.RentOffice[4], 88),
            (StoreLayout.Bank[0], 5000), (StoreLayout.Bank[1], 5000),
        })
        {
            var line = ItemText.PriceLine(slot.Label, price);
            yield return (slot.Left, line.Full, slot.Label, line.Value, $"{slot.Label.Trim()} ${price}");
        }
    }

    // ---- the face itself ---------------------------------------------------

    [Fact]
    public void Font10_is_six_pixels_tall_with_a_five_pixel_capital()
    {
        Assert.Equal(6, Font10.Value.Height);
        Assert.Equal(5, Font10.Value.CapHeight);
        Assert.Equal(5, Font10.Value.Baseline);
    }

    /// <summary>
    /// `|` is a ONE PIXEL BLANK in font 10 and a visible bar everywhere else. This is the
    /// whole reason the padded strings cannot simply be poured into another face, so it is
    /// asserted rather than remembered.
    /// </summary>
    [Fact]
    public void Font10_draws_the_pipe_as_a_one_pixel_blank()
    {
        var (w, h, offset) = Font10.Value.Glyph('|');
        Assert.Equal(1, w);

        var ink = 0;
        for (var y = 0; y < h; y++) ink += Font10.Value.Data[offset + y] & 0x80;
        Assert.Equal(0, ink);

        Assert.Equal(5, Font10.Value.Glyph(' ').Width);
        Assert.Equal(3, Font10.Value.Glyph('.').Width);
    }

    /// <summary>
    /// Quicksand's variable file defaults to Light; the port asks for Medium and applies the
    /// font's own `HVAR` deltas to get its advances. These are FreeType's answers for the same
    /// instance, in units of 1/1000 em, and they pin the HVAR implementation to a second
    /// opinion rather than to itself. A full stop is 204 at Medium against 158 at Light — 29%
    /// — which is why reading `hmtx` alone would have scattered every leader in the game.
    /// </summary>
    [Theory]
    [InlineData('a', 614)] [InlineData('e', 573)] [InlineData('f', 386)]
    [InlineData('M', 824)] [InlineData('0', 601)] [InlineData('5', 556)]
    [InlineData('$', 574)] [InlineData('.', 204)] [InlineData('|', 219)]
    [InlineData(' ', 275)]
    public void Quicksand_advances_are_the_Medium_instances(char c, int units)
    {
        var got = Quicksand.Value.AdvanceEm(c) * Quicksand.Value.UnitsPerEm;
        Assert.Equal(units, (int)Math.Round(got));
    }

    [Fact]
    public void Quicksand_is_sized_from_font_10s_capital()
    {
        // The ink of `X`, which is what has to match font 10's five-row capital.
        Assert.Equal(0.704, Quicksand.Value.CapHeightEm, 3);
        Assert.Equal(7.10, Em, 2);

        // The letters the interface is made of. The line box the face DECLARES is 1.25 em —
        // 8.9px at this size, which would not fit the Factory's 8px rows however it were
        // positioned — but a capital-to-descender span is 0.904 em, 6.4px, and that is the
        // number the layout actually has to fit. Keeping both written down is the point.
        var letters = Quicksand.Value.InkOf("bdfhklt");
        Assert.Equal(0.740, letters.Ascent, 3);
        Assert.Equal(0.200, Quicksand.Value.InkOf("gjpqy,;").Descent, 3);
    }

    // ---- the stops the source documents ------------------------------------

    /// <summary>
    /// The absolute column stops `ItemText` and `StoreLayout` write down in prose, recomputed
    /// from the shipped strings and the shipped font. Every one of these is `nsLeft` plus the
    /// width of the whole formatted line in font 10.
    /// </summary>
    [Theory]
    // Socket City: the three at nsLeft 77 stop at 179, the six at nsLeft 10 at 112. All nine
    // lines measure exactly 102px once the shop's OWN format (res 208) is used; with
    // CostDItem's default they were 97 or 102 and the column was five pixels ragged.
    [InlineData(LocationId.SocketCity, 0, 179)]
    [InlineData(LocationId.SocketCity, 2, 179)]
    [InlineData(LocationId.SocketCity, 3, 112)]
    [InlineData(LocationId.SocketCity, 7, 112)]
    [InlineData(LocationId.SocketCity, 8, 112)]
    // QT Clothing: all three stop at 175.
    [InlineData(LocationId.QtClothing, 0, 175)]
    [InlineData(LocationId.QtClothing, 2, 175)]
    // Monolith: left column 110, right column 177.
    [InlineData(LocationId.MonolithBurgers, 0, 110)]
    [InlineData(LocationId.MonolithBurgers, 1, 110)]
    [InlineData(LocationId.MonolithBurgers, 2, 177)]
    [InlineData(LocationId.MonolithBurgers, 5, 177)]
    // Black's Market: 112 in the left column, 180 in the right.
    [InlineData(LocationId.BlacksMarket, 0, 112)]
    [InlineData(LocationId.BlacksMarket, 1, 112)]
    [InlineData(LocationId.BlacksMarket, 2, 180)]
    [InlineData(LocationId.BlacksMarket, 3, 180)]
    [InlineData(LocationId.BlacksMarket, 4, 180)]
    public void Font10_holds_the_documented_shop_stops(LocationId store, int row, int stop)
    {
        var stock = store switch
        {
            LocationId.SocketCity => Catalogue.SocketCity,
            LocationId.QtClothing => Catalogue.QtClothing,
            LocationId.BlacksMarket => Catalogue.BlacksMarket,
            _ => Catalogue.MonolithBurgers,
        };

        var order = StoreLayout.ScriptOrder(store)!;
        var item = stock.OrderBy(s => Array.IndexOf(order, s.Name)).ToArray()[row];
        var slot = StoreLayout.For(store)![row];

        var price = item.PriceAt(100);
        var label = ItemText.For(store, item.Name);
        var full = store switch
        {
            LocationId.BlacksMarket => ItemText.BlacksMarketPrice(item.Name, label, price),
            LocationId.SocketCity or LocationId.ZMart => ItemText.AppliancePrice(label, price),
            _ => ItemText.WithPrice(label, price),
        };

        Assert.Equal(stop, slot.Left + Font10.Value.Measure(full));
    }

    /// <summary>
    /// The Factory's nine wages all end at x=159 — the tightest list in the game and the one
    /// the whole exercise turns on. Checked at every economic index, because the wage crosses
    /// $10 and the format string changes with it.
    /// </summary>
    [Theory]
    [InlineData(60)] [InlineData(100)] [InlineData(150)]
    public void Font10_holds_the_Factory_wage_stop_at_159(int index)
    {
        foreach (var job in Jobs.At(Workplace.Factory))
        {
            var wage = Pricing.Price(index, job.BaseWage);
            var full = ItemText.WithWage(ItemText.For(job), wage);
            Assert.Equal(159, StoreLayout.JobLeft + Font10.Value.Measure(full));
        }
    }

    /// <summary>Every job list holds ONE stop across all of its rows, whatever the economy.</summary>
    [Fact]
    public void Font10_gives_every_job_list_a_single_wage_stop()
    {
        foreach (var index in new[] { 60, 80, 100, 120, 150 })
            foreach (var place in Enum.GetValues<Workplace>())
            {
                var stops = Jobs.At(place)
                    .Select(j => Font10.Value.Measure(
                        ItemText.WithWage(ItemText.For(j), Pricing.Price(index, j.BaseWage))))
                    .Distinct()
                    .ToArray();

                if (stops.Length == 0) continue;
                Assert.True(stops.Length == 1,
                    $"{place} at index {index} has {stops.Length} wage stops: " +
                    string.Join(", ", stops));
            }
    }

    // ---- the split, and the stripping --------------------------------------

    /// <summary>
    /// The formatted line and the value token are produced by two different expressions, so
    /// this is what stops them drifting apart: the value must always be the line's own suffix.
    /// </summary>
    [Fact]
    public void Every_formatted_line_ends_with_its_own_value()
    {
        foreach (var (_, full, _, value, where) in AllLines())
            Assert.True(full.EndsWith(value, StringComparison.Ordinal),
                $"{where}: \"{full}\" does not end with \"{value}\"");
    }

    /// <summary>
    /// Nothing the outline face draws may contain a `|`. The pipes are font 10's one-pixel
    /// blanks; any other face draws them as bars, which is what
    /// <c>Janitor ||| $7 Hr.</c> looks like on screen.
    /// </summary>
    [Fact]
    public void No_laid_out_run_contains_a_pipe_or_trailing_padding()
    {
        foreach (var (_, full, label, value, where) in AllLines())
        {
            var line = Lay(full, label, value);
            foreach (var run in line.Runs)
            {
                Assert.False(run.Text.Contains('|'), $"{where}: pipe survived in \"{run.Text}\"");
                Assert.False(run.Text.EndsWith(' '), $"{where}: trailing space in \"{run.Text}\"");
            }
        }
    }

    /// <summary>
    /// A TRAILING FULL STOP IS PUNCTUATION, NOT PADDING, when there is only one of it. The
    /// Rent Office's lines are sentences in raw script 201 and stripping their full stop would
    /// silently rewrite what the game says.
    /// </summary>
    [Theory]
    [InlineData("Pay rent for 1 month.  ", "Pay rent for 1 month.", false)]
    [InlineData("Ask For More Time.       ", "Ask For More Time.", false)]
    [InlineData("Rent Low-Cost Apartment  ", "Rent Low-Cost Apartment", false)]
    [InlineData("Deposit  ", "Deposit", false)]
    [InlineData("Cook               ||", "Cook", false)]
    [InlineData("Janitor            |||", "Janitor", false)]
    [InlineData("Cheeseburger.......|", "Cheeseburger", true)]
    [InlineData("Food For 4 Weeks..", "Food For 4 Weeks", true)]
    [InlineData("Stove|..............||", "Stove", true)]
    [InlineData("Concert Tickets|..|", "Concert Tickets", true)]
    [InlineData("Black & White TV||", "Black & White TV", false)]
    public void StripPadding_keeps_the_words_and_drops_the_shims(string raw, string text, bool leader)
    {
        var got = ColumnLayout.StripPadding(raw);
        Assert.Equal(text, got.Text);
        Assert.Equal(leader, got.Leader);
    }

    // ---- the measured layout -----------------------------------------------

    /// <summary>
    /// THE INVARIANT THE WHOLE CHANGE RESTS ON: in Quicksand, the value's right edge lands on
    /// the stop font 10 gives it, to within a rounding pixel, on every line in the game.
    /// </summary>
    [Fact]
    public void Quicksand_puts_every_value_on_font_10s_stop()
    {
        foreach (var (_, full, label, value, where) in AllLines())
        {
            if (value.Length == 0) continue;

            var stop = Font10.Value.Measure(full);
            var line = Lay(full, label, value);
            var last = line.Runs[^1];

            Assert.False(line.Overflowed, $"{where}: the label crowded its own column");
            Assert.Equal(value.TrimStart(' '), last.Text);
            Assert.Equal(stop, last.X + Measure(last.Text), 1);
        }
    }

    /// <summary>
    /// And therefore: every row of a shop or a job list shares one right edge, which is the
    /// property the padding was there to produce. Measured in Quicksand, at the store's own
    /// `nsLeft`, so a column that drifted would be caught here even if each individual line
    /// still matched its own font 10 stop.
    /// </summary>
    [Fact]
    public void Quicksand_holds_one_stop_per_column()
    {
        foreach (var index in new[] { 60, 100, 150 })
        {
            foreach (var place in Enum.GetValues<Workplace>())
            {
                var edges = Jobs.At(place).Select(j =>
                {
                    var wage = Pricing.Price(index, j.BaseWage);
                    var line = ItemText.WageLine(ItemText.For(j), wage);
                    var laid = Lay(line.Full, ItemText.For(j), line.Value);
                    return Math.Round(StoreLayout.JobLeft + laid.Runs[^1].X
                                      + Measure(laid.Runs[^1].Text));
                }).Distinct().ToArray();

                if (edges.Length == 0) continue;
                Assert.True(edges.Length == 1,
                    $"{place} at index {index}: {edges.Length} wage columns ({string.Join(", ", edges)})");
            }

            // Monolith's two columns, which is the case with two different stops on one screen.
            var monolith = Catalogue.MonolithBurgers
                .OrderBy(s => Array.IndexOf(StoreLayout.ScriptOrder(LocationId.MonolithBurgers)!, s.Name))
                .Select((s, i) =>
                {
                    var label = ItemText.For(LocationId.MonolithBurgers, s.Name);
                    var line = ItemText.PriceLine(label, s.PriceAt(index));
                    var laid = Lay(line.Full, label, line.Value);
                    return (StoreLayout.MonolithBurgers[i].Left,
                            Edge: Math.Round(StoreLayout.MonolithBurgers[i].Left
                                             + laid.Runs[^1].X + Measure(laid.Runs[^1].Text)));
                })
                .ToArray();

            Assert.Equal(110, monolith[0].Edge);
            Assert.Equal(110, monolith[1].Edge);
            foreach (var row in monolith.Skip(2)) Assert.Equal(177, row.Edge);
        }
    }

    /// <summary>
    /// EVERY STORE, BOTH FACES, ONE COLUMN PER nsLeft.
    ///
    /// <para>
    /// This is the invariant in its general form: where a group of lines shares one `doFormat`
    /// and one left edge, their VALUES MUST END ON ONE PIXEL. It is asserted against font 10,
    /// which is the original's own behaviour, and then against Quicksand, which is the claim
    /// the measured layout makes — and it is asserted for all five shops rather than for the
    /// one that happened to be looked at.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(LocationId.SocketCity, 60)] [InlineData(LocationId.SocketCity, 100)]
    [InlineData(LocationId.SocketCity, 150)]
    [InlineData(LocationId.QtClothing, 60)] [InlineData(LocationId.QtClothing, 100)]
    [InlineData(LocationId.QtClothing, 150)]
    [InlineData(LocationId.MonolithBurgers, 60)] [InlineData(LocationId.MonolithBurgers, 100)]
    [InlineData(LocationId.MonolithBurgers, 150)]
    [InlineData(LocationId.BlacksMarket, 60)] [InlineData(LocationId.BlacksMarket, 100)]
    [InlineData(LocationId.BlacksMarket, 150)]
    public void Every_shop_column_has_one_right_edge(LocationId store, int index)
    {
        var stock = store switch
        {
            LocationId.SocketCity => Catalogue.SocketCity,
            LocationId.QtClothing => Catalogue.QtClothing,
            LocationId.BlacksMarket => Catalogue.BlacksMarket,
            _ => Catalogue.MonolithBurgers,
        };

        var order = StoreLayout.ScriptOrder(store)!;
        var slots = StoreLayout.For(store)!;
        var ordered = stock.OrderBy(s => Array.IndexOf(order, s.Name)).ToArray();

        var columns = new Dictionary<int, List<(int Bitmap, double Outline, string Where)>>();

        for (var i = 0; i < ordered.Length && i < slots.Length; i++)
        {
            var item = ordered[i];
            var price = item.PriceAt(index);
            var label = ItemText.For(store, item.Name);
            var line = store switch
            {
                LocationId.BlacksMarket => ItemText.BlacksMarketLine(item.Name, label, price),
                LocationId.SocketCity => ItemText.ApplianceLine(label, price),
                _ => ItemText.PriceLine(label, price),
            };

            var left = slots[i].Left;
            var laid = Lay(line.Full, label, line.Value);
            var last = laid.Runs[^1];

            if (!columns.TryGetValue(left, out var rows)) columns[left] = rows = [];
            rows.Add((left + Font10.Value.Measure(line.Full),
                      left + last.X + Measure(last.Text),
                      $"{store} {item.Name} @{index}"));
        }

        foreach (var (left, rows) in columns)
        {
            // Black's Market puts its lottery line one pixel left of its neighbours
            // (nsLeft 74 against 75) precisely so that all three of its right-hand rows land
            // on 180, so grouping by nsLeft splits a column the game holds together. Group by
            // "within two pixels of each other" for that one store instead.
            if (store == LocationId.BlacksMarket && rows.Count == 1) continue;

            var bitmap = rows.Select(r => r.Bitmap).Distinct().ToArray();
            Assert.True(bitmap.Length == 1,
                $"font 10, nsLeft {left}: {bitmap.Length} stops ({string.Join(", ", bitmap)}) " +
                $"across {string.Join("; ", rows.Select(r => r.Where))}");

            var outline = rows.Select(r => Math.Round(r.Outline)).Distinct().ToArray();
            Assert.True(outline.Length == 1,
                $"Quicksand, nsLeft {left}: {outline.Length} stops ({string.Join(", ", outline)})");

            Assert.Equal(bitmap[0], outline[0], 1);
        }
    }

    /// <summary>
    /// Black's Market's right-hand column is the one the nsLeft grouping cannot see: the
    /// lottery line sits at 74 and its two neighbours at 75, which is exactly how all three
    /// land on 180. Asserted directly.
    /// </summary>
    [Theory]
    [InlineData(60)] [InlineData(100)] [InlineData(150)]
    public void Blacks_Market_right_column_lands_on_one_edge(int index)
    {
        foreach (var name in new[] { "Food For 4 Weeks", "10 Lottery Tickets", "Newspaper" })
        {
            var item = Catalogue.BlacksMarket.Single(s => s.Name == name);
            var slot = StoreLayout.BlacksMarket.Single(s => s.Label == name);
            var label = ItemText.For(LocationId.BlacksMarket, name);
            var line = ItemText.BlacksMarketLine(name, label, item.PriceAt(index));

            var laid = Lay(line.Full, label, line.Value);
            var last = laid.Runs[^1];

            // One pixel of tolerance, because "Food For 4 Weeks.." is one pixel short of its
            // neighbours in the SHIPPED GAME and is ported that way (see `StoreLayout`).
            Assert.InRange(slot.Left + Font10.Value.Measure(line.Full), 179, 180);
            Assert.InRange(slot.Left + last.X + Measure(last.Text), 178.5, 180.5);
        }
    }

    /// <summary>
    /// Z-MART IS RAGGED, AND IT IS RAGGED IN THE ORIGINAL. Its eighteen labels are not one
    /// width — 69px on the five whose price can reach four digits, 74px on the other thirteen
    /// — so with its own format (res 211) its lines measure 95 or 100 and the price column
    /// steps by five pixels depending on which six of the eighteen were rolled. Asserting the
    /// raggedness is the only honest thing to do: it is the shipped game, and a future change
    /// that "fixed" it would be inventing.
    /// </summary>
    [Fact]
    public void Z_Mart_is_five_pixels_ragged_as_the_original_is()
    {
        var widths = Catalogue.ZMart
            .Select(s => Font10.Value.Measure(
                ItemText.AppliancePrice(ItemText.For(LocationId.ZMart, s.Name), s.PriceAt(100))))
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        Assert.Equal([95, 100], widths);

        // And Quicksand reproduces it rather than tidying it away.
        foreach (var s in Catalogue.ZMart)
        {
            var label = ItemText.For(LocationId.ZMart, s.Name);
            var line = ItemText.ApplianceLine(label, s.PriceAt(100));
            var laid = Lay(line.Full, label, line.Value);
            Assert.Equal(Font10.Value.Measure(line.Full),
                         laid.Runs[^1].X + Measure(laid.Runs[^1].Text), 1);
        }
    }

    /// <summary>
    /// THE EMPLOYMENT OFFICE has no value column — nine plain labels at fixed `nsLeft` — so its
    /// invariant is the other one: no line may grow WIDER than font 10 drew it, or it runs off
    /// the panel the artist painted behind it. Quicksand is comfortably narrower, chiefly
    /// because font 10's space is a full five pixels.
    /// </summary>
    [Fact]
    public void Quicksand_never_widens_the_employer_list()
    {
        foreach (var (label, _, left, _, _) in StoreLayout.Employment)
        {
            var bitmap = Font10.Value.Measure(label);
            var outline = Measure(ColumnLayout.StripPadding(label).Text);

            Assert.True(outline <= bitmap,
                $"{label}: Quicksand {outline:F1}px against font 10's {bitmap}px");
            Assert.True(left + outline < 180, $"{label} runs past the panel");
        }
    }

    /// <summary>
    /// And the same for every job-list header and shop label, so nothing anywhere gets wider.
    /// </summary>
    [Fact]
    public void Quicksand_never_widens_a_job_list_header()
    {
        foreach (var place in Enum.GetValues<Workplace>())
        {
            var (header, left, _, _, _) = StoreLayout.JobList(place);
            var outline = Measure(header);
            Assert.True(outline <= Font10.Value.Measure(header), header);
            Assert.True(left + outline <= 320, header);
        }
    }

    // ---- the row spacing ---------------------------------------------------

    /// <summary>
    /// THE ROW SPACING DECISION, ASSERTED.
    ///
    /// <para>
    /// The Factory packs nine jobs into 8-pixel rows and it was thought the outline face could
    /// not fit: Quicksand declares an ascender of 1.0 em and a descender of 0.25, so its LINE
    /// BOX is 8.9 pixels at the em that matches font 10's capital, which overlaps the next row.
    /// Its INK is another matter — 0.740 em up and 0.200 em down, 6.7 pixels in all — and the
    /// port positions by baseline and ink, not by line box.
    /// </para>
    ///
    /// <para>
    /// So the coordinates do not move. Every `nsTop` in the scripts is kept, the baseline sits
    /// where font 10's sat, and the deepest descender still clears the next row's tallest
    /// ascender. This test is that clearance, and it is what would go red if the em, the face
    /// or the spacing were changed without thinking about it.
    /// </para>
    /// </summary>
    [Fact]
    public void Quicksand_clears_the_Factory_eight_pixel_rows()
    {
        var m = Quicksand.Value;
        var baseline = Font10.Value.Baseline;               // 5px below the row's own top
        var spacing = StoreLayout.JobList(Workplace.Factory).Spacing;

        Assert.Equal(8, spacing);

        // The ink of the rows THE FACTORY ACTUALLY DRAWS, not the worst case over all of
        // ASCII: no job line contains a brace or a parenthesis, and sizing the clearance to
        // characters the screen never shows would reject a layout that fits.
        var text = string.Concat(Jobs.At(Workplace.Factory)
            .Select(j => ItemText.WageLine(ItemText.For(j), Pricing.Price(100, j.BaseWage)).Full));
        var ink = m.InkOf(text);

        // Both measured downwards from THIS row's own top.
        var descender = baseline + ink.Descent * Em;
        var nextAscender = spacing + baseline - ink.Ascent * Em;

        Assert.True(nextAscender > descender,
            $"Quicksand collides at {spacing}px rows: ink reaches {descender:F2} and the next " +
            $"row starts at {nextAscender:F2}");

        // Recorded so a future change has to acknowledge how much room there actually is:
        // 0.76 of a game pixel, which is 8 device pixels at the 4K scale and 2.6 on a phone.
        // The tightest pair is not a letter at all — it is the `$` of one row's wage against
        // the `$` of the next, because Quicksand's dollar sign runs above the cap line and
        // below the baseline, and the two sit in the same column by construction.
        Assert.InRange(nextAscender - descender, 0.6, 1.0);

        // And the capital sits exactly on font 10's cap line, which is what keeps every
        // coordinate in the scripts meaning what it meant.
        Assert.Equal(baseline, m.CapHeightEm * Em, 2);
    }
}
