using Jones.Core.Economy;
using Jones.Core.Model;
using Xunit;

namespace Jones.Tests;

/// <summary>
/// Pins `proc109_0` (`n109.sc`), the function behind every price in the game.
/// </summary>
public class PricingTests
{
    [Fact]
    public void AtANeutralEconomyThePriceIsTheBasePrice()
    {
        Assert.Equal(650, Pricing.Price(100, 650));
        Assert.Equal(35, Pricing.Price(100, 35));
    }

    [Theory]
    // The deviation from 100 is amplified by 5/3 before being applied as a percentage.
    // reading 70  -> effective 50  (the floor)   -> half price
    // reading 190 -> effective 250              -> two and a half times
    [InlineData(70, 100, 50)]
    [InlineData(190, 100, 250)]
    [InlineData(85, 100, 75)]
    [InlineData(130, 100, 150)]
    public void DeviationFromNeutralIsAmplified(int reading, int basePrice, int expected)
    {
        Assert.Equal(expected, Pricing.Price(reading, basePrice));
    }

    [Fact]
    public void EffectiveIndexIsFlooredAtFiftyButHasNoCeiling()
    {
        // Below the 70 reading floor the amplifier would go under 50, but it clamps.
        Assert.Equal(Pricing.Price(70, 200), Pricing.Price(60, 200));

        // Upward there is no clamp at all, so prices run to 2.5x base.
        Assert.Equal(500, Pricing.Price(190, 200));
    }

    [Fact]
    public void TheTensAndUnitsSplitNeverExceedsTheNaiveSingleExpression()
    {
        // The original splits the percentage into tens and units to keep intermediate
        // products inside 16 bits. That is not just an optimisation: truncating twice
        // loses up to a penny in the units term, so the split form is always <= the
        // single-expression form. Preserving it matters for shelf-price exactness.
        var diverged = false;

        for (var reading = 70; reading <= 190; reading++)
        {
            var effective = reading < 100
                ? reading - (100 - reading) * 2 / 3
                : reading + (reading - 100) * 2 / 3;
            if (effective < 50) effective = 50;

            var naive = 55 * effective / 100;
            var actual = Pricing.Price(reading, 55);

            Assert.True(actual <= naive,
                $"reading {reading}: split {actual} should never exceed naive {naive}");

            if (actual != naive) diverged = true;
        }

        Assert.True(diverged, "the two forms should differ somewhere in the legal band");
    }

    [Fact]
    public void NothingIsEverFree()
    {
        Assert.Equal(1, Pricing.Price(70, 1));
    }

    [Fact]
    public void TheComputerGetsCHEAPERAsTheEconomyBooms_A16BitOverflowInTheOriginal()
    {
        // The most expensive item in the game is Socket City's computer at base 1599.
        // The tens term is `Mul16(1599, effective/10)`, which wraps past 32767 once
        // effective reaches 210 — i.e. a goods index reading of 166. The original then
        // detects the negative product and substitutes 32767, capping the price.
        //
        // The consequence is visible in play and not subtle: as the economy strengthens
        // past a reading of 165, the computer's price FALLS. This is an original bug with
        // real gameplay impact (the best time to buy the most valuable item is during a
        // boom, backwards from every other item), so the port reproduces it.
        var at165 = Pricing.Price(165, 1599);
        var at166 = Pricing.Price(166, 1599);

        Assert.True(at166 < at165,
            $"expected the overflow to make the computer cheaper: 165 => {at165}, 166 => {at166}");

        // Above the threshold the price is pinned by the 32767 substitution.
        Assert.Equal(3276, at166);
        Assert.Equal(3276, Pricing.Price(190, 1599));
    }

    [Fact]
    public void PricesAreOtherwiseMonotonicInTheEconomy()
    {
        // Sanity check that the non-monotonicity above is specific to overflow, not
        // general. A cheap item never gets cheaper as the economy rises.
        var previous = 0;
        for (var reading = 70; reading <= 190; reading++)
        {
            var price = Pricing.Price(reading, 55);
            Assert.True(price >= previous, $"price fell at reading {reading}");
            previous = price;
        }
    }

    // -----------------------------------------------------------------------
    // Pawn shop
    // -----------------------------------------------------------------------

    [Fact]
    public void PawnShopOffersFortyPercentOfCurrentValue()
    {
        // At a neutral economy, 40% of what you paid.
        Assert.Equal(260, Pricing.PawnOffer(100, 650));
    }

    [Fact]
    public void PawnOffersTrackTheEconomy()
    {
        var slump = Pricing.PawnOffer(70, 650);   // half value, so 20% of price paid
        var boom = Pricing.PawnOffer(190, 650);   // 2.5x value, so 100% of price paid

        Assert.Equal(130, slump);
        Assert.Equal(650, boom);
        Assert.True(boom > slump);
    }

    // -----------------------------------------------------------------------
    // Catalogue cross-checks. These verify the extracted base prices reproduce the
    // discount percentages the game is documented as having.
    // -----------------------------------------------------------------------

    [Theory]
    // The documented figures are rounded, so allow a point of slack. The real savings are
    // 25.8, 33.5, 33.3, 24.9 and 14.0 percent respectively.
    [InlineData("Refrigerator", 25)]
    [InlineData("Color TV", 33)]
    [InlineData("Microwave", 33)]
    [InlineData("Vcr", 25)]
    [InlineData("Stove", 14)]
    public void ZMartDiscountsMatchTheDocumentedPercentages(string name, int documented)
    {
        var zmart = Catalogue.ZMart.Single(i => i.Name == name).BasePrice;
        var socket = Catalogue.SocketCity.Single(i => i.Name == name).BasePrice;

        var saving = 100.0 * (socket - zmart) / socket;
        Assert.True(Math.Abs(saving - documented) <= 1.0,
            $"{name}: extracted prices give {saving:F1}%, documented {documented}%");
    }

    [Fact]
    public void TheStereoIsGenuinelyDearerAtZMart()
    {
        // The one item where the "discount store" is a trap, and the wiki flags it too.
        var zmart = Catalogue.ZMart.Single(i => i.Name == "Stereo").BasePrice;
        var socket = Catalogue.SocketCity.Single(i => i.Name == "Stereo").BasePrice;

        Assert.True(zmart > socket, "Z-Mart's stereo should cost MORE than Socket City's");
        Assert.Equal(450, zmart);
        Assert.Equal(412, socket);
    }

    [Fact]
    public void ClothingDiscountsAtZMartMatchQtClothing()
    {
        var zCasual = Catalogue.ZMart.Single(i => i.Name == "Casual Clothes").BasePrice;
        var qCasual = Catalogue.QtClothing.Single(i => i.Name == "Casual Clothes").BasePrice;
        Assert.Equal(52, (int)Math.Round(100.0 * (qCasual - zCasual) / qCasual));

        var zDress = Catalogue.ZMart.Single(i => i.Name == "Leisure Suit").BasePrice;
        var qDress = Catalogue.QtClothing.Single(i => i.Name == "Leisure Suit").BasePrice;
        Assert.Equal(28, (int)Math.Round(100.0 * (qDress - zDress) / qDress));
    }

    [Fact]
    public void FreezerHotTubAndComputerAreSocketCityExclusives()
    {
        // Each of these changes a RULE rather than just adding happiness: the freezer
        // doubles food storage, the hot tub stops relaxation decay entirely, the computer
        // earns money and grants extra credit. Z-Mart cannot supply any of them.
        foreach (var name in (string[])["Freezer", "Hot Tub", "Computer"])
        {
            Assert.Contains(Catalogue.SocketCity, i => i.Name == name);
            Assert.DoesNotContain(Catalogue.ZMart, i => i.Name == name);
        }
    }

    [Fact]
    public void JunkItemsHaveNoInventoryIdSoTheyCannotBeKept()
    {
        var junk = Catalogue.ZMart.Where(i => i.Type == GoodsType.Junk).ToList();
        Assert.Equal(3, junk.Count);
        Assert.All(junk, i => Assert.Null(i.ItemId));
    }

    [Fact]
    public void BulkFoodIsCheaperPerWeek()
    {
        var market = Catalogue.BlacksMarket;
        var perWeek = market.Select(i => (double)i.BasePrice / i.Quantity).ToList();

        Assert.Equal(55, perWeek[0]);
        Assert.Equal(50, perWeek[1]);
        Assert.Equal(47.5, perWeek[2]);
    }
}
