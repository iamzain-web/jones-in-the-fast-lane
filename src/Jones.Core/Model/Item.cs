namespace Jones.Core.Model;

/// <summary>
/// One entry in a player's consumables, durables or education list. Mirrors the original's
/// item objects, which carry an index number, a quantity, the price actually paid and a
/// bitfield of attributes.
/// </summary>
public sealed class Item
{
    public int IndexNum { get; init; }
    public int Quantity { get; set; }
    public int PricePaid { get; set; }
    public DurableAttributes Attributes { get; set; }

    /// <summary>
    /// Education items only: lessons needed to graduate. 10 by default, reduced by extra
    /// credit to a floor of 8. A degree is held when <see cref="Quantity"/> reaches this.
    /// </summary>
    public int UnitsToGraduate { get; set; }

    /// <summary>
    /// Durables only: what the pawn shop wants to sell it back for — half the price paid,
    /// set when the item is pawned along with bits 3-4 of <see cref="Attributes"/>
    /// (`pawnShop.sc:687`). Those bits then count down over the turns he has to redeem it.
    /// Zero on anything that has never been pawned (`Goods.sc:31`).
    /// </summary>
    public int RedemptionPrice { get; set; }

    public Item(int indexNum, int quantity = 0)
    {
        IndexNum = indexNum;
        Quantity = quantity;
    }
}

/// <summary>
/// A player's item collection. The original looks items up by index number constantly
/// (`objectAtIndex:` returns the item, `objectAtIndexQuan:` returns it only if the quantity
/// is non-zero), so both forms are provided with those semantics preserved.
/// </summary>
public sealed class ItemList
{
    private readonly List<Item> _items = [];

    public int Count => _items.Count;
    public IReadOnlyList<Item> Items => _items;

    /// <summary>`objectAtIndex:` — the item if present, regardless of quantity.</summary>
    public Item? At(int indexNum) => _items.FirstOrDefault(i => i.IndexNum == indexNum);

    /// <summary>`objectAtIndexQuan:` — the item only if it is present AND held.</summary>
    public Item? AtHeld(int indexNum)
    {
        var item = At(indexNum);
        return item is { Quantity: > 0 } ? item : null;
    }

    public bool Holds(int indexNum) => AtHeld(indexNum) is not null;

    /// <summary>`add:` — puts an existing item object into this list.</summary>
    public void Add(Item item) => _items.Add(item);

    /// <summary>`delete:` — takes an item object out of this list entirely.</summary>
    public void Remove(Item item) => _items.Remove(item);

    /// <summary>`recieve:` (sic, in the original) — add quantity, creating the item if new.</summary>
    public Item Receive(int indexNum, int quantity)
    {
        var item = At(indexNum);
        if (item is null)
        {
            item = new Item(indexNum);
            _items.Add(item);
        }
        item.Quantity += quantity;
        return item;
    }
}
