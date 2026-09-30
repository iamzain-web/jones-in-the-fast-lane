using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Jones.Core.Economy;
using Jones.Core.Model;

namespace Jones.App.Desktop;

/// <summary>
/// Draws the interface-font comparison sheets THROUGH THE GAME'S OWN RENDERER.
///
/// <para>
/// Run with <c>JONES_FONT_SHEET=&lt;dir&gt;</c>. Avalonia is set up without opening a window,
/// every line is built exactly as <c>MainViewModel</c> builds it — <c>ItemText</c> for the
/// strings, <c>StoreLayout</c> for the coordinates, <c>MenuLineVm</c> for the bitmap — and the
/// result is written to PNG. Nothing here re-implements the layout; if this sheet is wrong,
/// the game is wrong in the same way, which is the entire point of composing the frames here
/// rather than screenshotting a running window.
/// </para>
///
/// <para>
/// It draws BOTH faces in one pass by toggling <see cref="UiFont.Current"/> between them, and
/// it draws the red column stop that font 10 gives each row, so a value that has missed its
/// column is visible rather than inferred.
/// </para>
/// </summary>
internal static class FontSheet
{
    /// <summary>The dialog background, sampled from the shops' own panel art (index 101).</summary>
    private static readonly Color PanelColour = Color.FromRgb(0x98, 0xA8, 0xB0);

    public static void Write(string directory)
    {
        Directory.CreateDirectory(directory);

        // WHICH INSTANCE OF THE VARIABLE FONT THE TOOLKIT ACTUALLY GAVE US. Quicksand ships
        // one file with a `wght` axis whose default is 300; the port asks for Medium and
        // measures Medium's advances out of `HVAR`, so if the toolkit hands back Light the
        // columns are still exactly right and the strokes are thinner than intended. Printing
        // it is the difference between knowing and assuming.
        foreach (var family in new[] { "Quicksand", "Quicksand Medium", "Quicksand Light" })
            foreach (var weight in new[] { FontWeight.Light, FontWeight.Normal, FontWeight.Medium,
                                           FontWeight.SemiBold, FontWeight.Bold })
            {
                var ok = FontManager.Current.TryGetGlyphTypeface(
                    new Typeface(new FontFamily($"avares://Jones.App/Assets/fonts#{family}"),
                                 FontStyle.Normal, weight),
                    out var gt);
                Console.WriteLine($"  #{family,-16} ask {(int)weight,4} -> " +
                                  (ok ? $"{gt!.FamilyName} {(int)gt.Weight}" : "NOT FOUND"));
            }

        Sheet(directory, "app_font_job_list.png", "The Factory - nine jobs, 8px rows", 200, 100,
              FactoryJobs());
        Sheet(directory, "app_font_shop_list.png", "Monolith Burgers - two price columns", 200, 112,
              MonolithShop());
        Sheet(directory, "app_font_employers.png", "The Employment Office - nine workplaces", 130, 115,
              Employers());
        Sheet(directory, "app_font_socket_city.png", "Socket City - res 208's own format", 200, 112,
              SocketCity());

        Console.WriteLine($"font sheets written to {directory}");
    }

    // ---- the lines, built the way the game builds them ---------------------

    private sealed record Row(string Full, string? Value, int Left, int Top);

    private static List<Row> FactoryJobs()
    {
        var (_, _, _, firstTop, spacing) = StoreLayout.JobList(Workplace.Factory);
        return Jobs.At(Workplace.Factory).Select((j, i) =>
        {
            var line = ItemText.WageLine(ItemText.For(j), Pricing.Price(100, j.BaseWage));
            return new Row(line.Full, line.Value, StoreLayout.JobLeft, firstTop + i * spacing);
        }).ToList();
    }

    private static List<Row> MonolithShop() => Shop(LocationId.MonolithBurgers);
    private static List<Row> SocketCity() => Shop(LocationId.SocketCity);

    private static List<Row> Shop(LocationId store)
    {
        var stock = store == LocationId.SocketCity ? Catalogue.SocketCity : Catalogue.MonolithBurgers;
        var order = StoreLayout.ScriptOrder(store)!;
        var slots = StoreLayout.For(store)!;

        return stock.OrderBy(s => Array.IndexOf(order, s.Name)).Select((s, i) =>
        {
            var label = ItemText.For(store, s.Name);
            var price = s.PriceAt(100);
            var line = store == LocationId.SocketCity
                ? ItemText.ApplianceLine(label, price)
                : ItemText.PriceLine(label, price);
            return new Row(line.Full, line.Value, slots[i].Left, slots[i].Top);
        }).ToList();
    }

    private static List<Row> Employers() =>
        StoreLayout.Employment.Select(e => new Row(e.Label, null, e.Left, e.Top)).ToList();

    // ---- drawing -----------------------------------------------------------

    /// <summary>
    /// The scale the sheet is drawn at. 12 is the 4K factor. Set <c>JONES_FONT_SHEET_SCALE</c>
    /// to <b>3.375</b> — together with <c>JONES_ART_SCALE=1</c>, which is the Android head's
    /// own <see cref="RenderScale.Factor"/> — to get the PHONE case exactly: 1080/320 is
    /// 3.375 device pixels per game pixel, the glyphs are rasterised at
    /// <see cref="UiFont.TextScale"/> (4x, because the head has no upscaled art) and fitted
    /// down by the same resample the Viewbox applies there.
    /// </summary>
    private static double SheetScale =>
        double.TryParse(Environment.GetEnvironmentVariable("JONES_FONT_SHEET_SCALE"),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0
            ? s
            : 12;

    private static void Sheet(string dir, string name, string title, int w, int h, List<Row> rows)
    {
        var scale = SheetScale;
        var panels = new List<(string Caption, Bitmap Image)>();

        var was = UiFont.Current;

        foreach (var face in new[] { UiFont.Face.Bitmap, UiFont.Face.Quicksand })
        {
            UiFont.Current = face;
            panels.Add(($"{title}  -  {(face == UiFont.Face.Bitmap ? "DEFAULT: the game's font 10" : "QUICKSAND MEDIUM, MEASURED")}",
                        DrawPanel(rows, w, h, scale)));
        }

        UiFont.Current = was;

        var totalH = panels.Sum(p => (int)p.Image.Size.Height) + 30 * panels.Count;
        var target = new RenderTargetBitmap(new PixelSize((int)(w * scale), totalH), new Vector(96, 96));

        using (var ctx = target.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.WhiteSmoke, new Rect(0, 0, w * scale, totalH));

            var y = 0.0;
            foreach (var (caption, image) in panels)
            {
                ctx.DrawText(
                    new FormattedText(caption, System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, Typeface.Default, 16, Brushes.Black),
                    new Point(8, y + 6));
                y += 30;
                ctx.DrawImage(image, new Rect(0, y, image.Size.Width, image.Size.Height));
                y += image.Size.Height;
            }
        }

        target.Save(Path.Combine(dir, name));
    }

    private static Bitmap DrawPanel(List<Row> rows, int w, int h, double scale)
    {
        var bmp = new RenderTargetBitmap(new PixelSize((int)(w * scale), (int)(h * scale)), new Vector(96, 96));
        using var ctx = bmp.CreateDrawingContext();

        ctx.FillRectangle(new SolidColorBrush(PanelColour), new Rect(0, 0, w * scale, h * scale));

        var colours = StoreLayout.JobListColours;
        var stopPen = new Pen(Brushes.Red, Math.Max(1, scale / 6.0));

        foreach (var row in rows)
        {
            // The real view model, with the real colours: this is a shop line.
            var vm = new ViewModels.MenuLineVm(row.Full, row.Left, row.Top, () => { },
                colours.Text, colours.Shadow, colours.Flash, value: row.Value);

            if (vm.Normal is { } image)
            {
                // EXACTLY WHAT THE VIEW DOES, or the sheet lies about the one thing it is
                // for: the high-resolution twin `RenderScale.HighRes` would swap in, and this
                // line's own `Interpolation` — `None` for the game's pixels, a resample for
                // the outline face. Drawing the 1x bitmap smoothly up to 12x, which is what
                // this did first, made font 10 look like a blurred photograph of itself.
                using (ctx.PushRenderOptions(new RenderOptions
                       {
                           BitmapInterpolationMode = UiFont.InterpolationFor(vm.Smooth),
                       }))
                {
                    ctx.DrawImage(SciArt.HighRes(image), new Rect(
                        (row.Left + vm.ImageDx) * scale, (row.Top + vm.ImageDy) * scale,
                        vm.ImageW * scale, vm.ImageH * scale));
                }
            }

            // The stop font 10 gives this row, which is what the value must right-align onto.
            var stop = (row.Left + (SciFont.Interface?.Measure(row.Full) ?? 0)) * scale;
            ctx.DrawLine(stopPen, new Point(stop, row.Top * scale), new Point(stop, (row.Top + 7) * scale));
        }

        return bmp;
    }
}
