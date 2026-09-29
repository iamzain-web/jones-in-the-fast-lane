using Jones.Core.Sci;

namespace Jones.Core.Economy;

/// <summary>
/// The universal price function — a port of `proc109_0` in `n109.sc` (script 109).
///
/// Everything priced in Jones goes through here: shop shelves, investment values, pawn
/// offers and the uniform-affordability check when applying for a job. Get this wrong and
/// every number the player sees is wrong.
/// </summary>
public static class Pricing
{
    /// <summary>
    /// Converts an economic index reading into a price for <paramref name="basePrice"/>.
    ///
    /// Goods use the GOODS index (`global309`); each investment uses its own index
    /// (`global310`–`global314`); T-bills bypass this and sit at their base price.
    /// </summary>
    public static int Price(int indexReading, int basePrice)
    {
        // Step 1: amplify the deviation from neutral by 5/3, so the shelf swings harder
        // than the economy does. A reading of 70 becomes an effective 50; 190 becomes 250.
        var effective = indexReading < 100
            ? indexReading - SciMath.Div((100 - indexReading) * 2, 3)
            : indexReading + SciMath.Div((indexReading - 100) * 2, 3);

        // Floor of 50: nothing is ever sold below half price, however dire the economy.
        // Note there is no matching ceiling — prices can run to 2.5x.
        if (effective < 50) effective = 50;

        // Step 2: apply it as a percentage, split into tens and units. The original does
        // this to keep the intermediate products inside 16 bits, and the split is not
        // merely an optimisation — truncating twice gives different answers than
        // `basePrice * effective / 100` would. For basePrice 55 at effective 137 this
        // yields 74, where the single-expression form yields 75.
        var tensProduct = SciMath.Mul16(basePrice, SciMath.Div(effective, 10));

        // The original's own overflow guard. Because the multiply above is 16-bit, a
        // large basePrice (the pawn shop passes the price actually paid) can wrap
        // negative; the game then substitutes the maximum positive value.
        var tens = tensProduct < 0 ? 32767 : tensProduct;

        var price = SciMath.Div(tens, 10)
                  + SciMath.Div(SciMath.Mul16(basePrice, effective % 10), 100);

        // Nothing is ever free.
        if (price < 1) price = 1;

        return price;
    }

    /// <summary>
    /// What the pawn shop offers for an item: 40% of its economy-adjusted purchase price
    /// (`pawnShop.sc:664`). Pawning also costs 1 Happiness, and 1 more if it is a
    /// refrigerator while fresh food is held.
    /// </summary>
    public static int PawnOffer(int goodsIndexReading, int pricePaid) =>
        SciMath.Div(Price(goodsIndexReading, pricePaid) * 4, 10);
}
