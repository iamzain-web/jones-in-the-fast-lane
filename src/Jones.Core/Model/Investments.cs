using Jones.Core.Economy;
using Jones.Core.Sci;

namespace Jones.Core.Model;

public enum Instrument { TBills, Gold, Silver, Pork, BlueChip, Penny }

/// <summary>
/// A player's investment holdings, ported from the `I` class (`room1.sc:576`) and the
/// pricing in `broker.sc`.
///
/// T-bills are the odd one out: they are valued at a flat base price and never move, so
/// they are a place to park cash rather than an investment. Everything else tracks its own
/// economic index, and penny stocks (risk 10) swing hardest.
/// </summary>
public sealed class Holdings
{
    private readonly Dictionary<Instrument, int> _shares = [];

    public int SharesOf(Instrument i) => _shares.GetValueOrDefault(i);

    public void Add(Instrument i, int shares) =>
        _shares[i] = SharesOf(i) + shares;

    public IEnumerable<(Instrument Instrument, int Shares)> AllHeld =>
        _shares.Where(kv => kv.Value > 0).Select(kv => (kv.Key, kv.Value));

    /// <summary>Base price before the economy is applied (`room1.sc:674`).</summary>
    public static int BasePrice(Instrument i) => i switch
    {
        Instrument.TBills => 100,
        Instrument.Gold => 413,
        Instrument.Silver => 14,
        Instrument.Pork => 20,
        Instrument.BlueChip => 49,
        Instrument.Penny => 7,
        _ => 0,
    };

    /// <summary>
    /// Unit price now. T-bills bypass the pricing function entirely and sit at base;
    /// the rest use their own index reading.
    /// </summary>
    public static int UnitPrice(Instrument i, EconomyState econ)
    {
        if (i == Instrument.TBills) return BasePrice(i);

        var reading = i switch
        {
            Instrument.Gold => econ.Gold.Reading,
            Instrument.Silver => econ.Silver.Reading,
            Instrument.Pork => econ.Pork.Reading,
            Instrument.BlueChip => econ.BlueChip.Reading,
            Instrument.Penny => econ.Penny.Reading,
            _ => 100,
        };

        return Pricing.Price(reading, BasePrice(i));
    }

    /// <summary>Total value of everything held.</summary>
    public long TotalValue(EconomyState econ) =>
        _shares.Sum(kv => (long)kv.Value * UnitPrice(kv.Key, econ));
}

/// <summary>Buying and selling at the Bank's brokerage window.</summary>
public static class Broker
{
    /// <summary>Buys as many shares as requested, if affordable. Returns shares bought.</summary>
    public static int Buy(Player p, Instrument i, int shares, EconomyState econ)
    {
        if (shares <= 0) return 0;

        var unit = UnitPriceFor(i, econ);
        var cost = (long)unit * shares;
        if (cost > p.Cash) return 0;

        p.Cash -= cost;
        p.Holdings.Add(i, shares);
        return shares;
    }

    /// <summary>
    /// Sells shares at the current price. Returns cash received.
    ///
    /// There is no commission anywhere in the game EXCEPT a flat $3 charged on each
    /// T-bill sold (`broker.sc:574`), so a $100 T-bill returns $97. Buying and selling
    /// otherwise use the same price — no spread.
    /// </summary>
    public const int TBillSaleFee = 3;

    public static long Sell(Player p, Instrument i, int shares, EconomyState econ)
    {
        if (shares <= 0) return 0;

        var held = p.Holdings.SharesOf(i);
        if (shares > held) shares = held;
        if (shares == 0) return 0;

        var proceeds = (long)UnitPriceFor(i, econ) * shares;
        p.Holdings.Add(i, -shares);
        p.Cash += proceeds;

        if (i == Instrument.TBills)
        {
            var fee = TBillSaleFee * shares;
            p.Cash -= fee;
            proceeds -= fee;
        }

        return proceeds;
    }

    private static int UnitPriceFor(Instrument i, EconomyState econ) =>
        Holdings.UnitPrice(i, econ);
}

/// <summary>
/// The Bank, ported from `bank.sc`.
///
/// Worth understanding before playing: savings earn NOTHING. The only writes to the bank
/// balance in the entire game are deposit, withdraw, and a severity-1 crash zeroing it.
/// The bank is not an investment — it is a safe that protects cash from Wild Villy, who
/// can only take what you are carrying.
/// </summary>
public static class Bank
{
    /// <summary>Deposits and withdrawals move at most $100 at a time.</summary>
    public const int TransferLimit = 100;

    /// <summary>A loan payment costs this much cash...</summary>
    public const int PaymentCost = 50;

    /// <summary>...but only clears this much principal. The $5 gap is the interest.</summary>
    public const int PaymentPrincipal = 45;

    /// <summary>Moves up to $100 from pocket to savings. Returns the amount moved.</summary>
    public static long Deposit(Player p)
    {
        var amount = Math.Min(p.Cash, TransferLimit);
        if (amount <= 0) return 0;

        p.Cash -= amount;
        p.BankBal += amount;
        return amount;
    }

    /// <summary>Moves up to $100 from savings to pocket. Returns the amount moved.</summary>
    public static long Withdraw(Player p)
    {
        var amount = Math.Min(p.BankBal, TransferLimit);
        if (amount <= 0) return 0;

        p.BankBal -= amount;
        p.Cash += amount;
        return amount;
    }

    /// <summary>
    /// The loan the bank will offer, or 0 for none (`bank.sc:346`):
    ///   capacity = wage + liquidAssets/1000
    ///   demands  = 5 + latePay + loanBal/100 + (loanBal ? 1 : 0)
    ///   offer    = (capacity - demands) * 100, when positive and paySched >= 0
    ///
    /// Note employment is NOT a hard requirement: a player with no wage but over $6000
    /// in liquid assets still qualifies. And every late payment permanently costs $100
    /// of future borrowing.
    /// </summary>
    public static int LoanOffer(Player p)
    {
        if (p.PaySched < 0) return 0;

        var capacity = p.Wage + (int)(p.LqAss / 1000);
        var demands = 5 + p.LatePay + p.LoanBal / 100 + (p.LoanBal != 0 ? 1 : 0);
        if (capacity <= demands) return 0;

        return (capacity - demands) * 100;
    }

    /// <summary>
    /// Takes the offered loan. +5 Happiness (`bank.sc:377`).
    ///
    /// The penalty below is for INELIGIBILITY, not for changing your mind: `bank.sc:389-395`
    /// runs `proc0_13 -1` when the bank will not lend, and a SECOND `-1` when you have no
    /// existing loan — so -2 debt-free, -1 already indebted. A player who is offered a loan
    /// and declines it pays nothing at all (`bank.sc:383-386`). The values were right; the
    /// comment that called this a refusal penalty was not.
    /// </summary>
    public static int TakeLoan(Player p)
    {
        var offer = LoanOffer(p);
        if (offer <= 0)
        {
            p.HapStat -= p.LoanBal > 0 ? 1 : 2;
            return 0;
        }

        p.LoanBal += offer;
        p.Cash += offer;
        p.HapStat += 5;
        return offer;
    }

    /// <summary>
    /// Makes one payment: $50 out of pocket, $45 off the balance. When the balance is
    /// under $50 it settles exactly, with no interest on the last payment.
    /// </summary>
    public static bool MakePayment(Player p)
    {
        if (p.LoanBal <= 0) return false;

        var cost = p.LoanBal < PaymentCost ? p.LoanBal : PaymentCost;
        if (cost > p.Cash) return false;

        p.Cash -= cost;
        p.LoanBal -= p.LoanBal < PaymentCost ? p.LoanBal : PaymentPrincipal;
        if (p.LoanBal < 0) p.LoanBal = 0;

        p.PaySched++;
        p.MadePay = true;
        if (p.LoanBal == 0) p.PaySched = 0;
        return true;
    }

    /// <summary>
    /// Wild Willy outside the bank: from week 4, a 1-in-31 roll ON ARRIVING, but only if
    /// you are carrying cash. `bank.sc:120-122`, inside the dialog's `init`:
    ///
    ///     (if (and (>= global372 4) (not (Random 0 30)) (> (proc0_11) 0)) (= global446 2))
    ///
    /// The roll is all that happens here. See <see cref="ApplyMugging"/> for the rest.
    /// </summary>
    public static bool RollMugging(Player p, IRandomSource rng, int week)
    {
        if (week < 4 || p.Cash <= 0) return false;
        return rng.Next(0, 30) == 0;
    }

    /// <summary>
    /// The SAME roll outside Black's Market, and the only difference is the odds:
    /// `market.sc:142-144` is the identical line with `(Random 0 50)` — 1-in-51 against the
    /// bank's 1-in-31 — and sets `global446` to 1 rather than 2, which is what picks
    /// `muggedByMarket` over `muggedByBank` in script 114.
    /// </summary>
    public static bool RollMuggingAtMarket(Player p, IRandomSource rng, int week)
    {
        if (week < 4 || p.Cash <= 0) return false;
        return rng.Next(0, 50) == 0;
    }

    /// <summary>
    /// He takes EVERYTHING in your pocket and nothing from your savings — which is the
    /// whole argument for banking it — and it costs 3 Happiness (`room1.sc:236`).
    ///
    /// This happens when the bank DIALOG CLOSES (`bank.sc:131-133`), not on arrival, so
    /// anything you deposit during the visit is out of his reach.
    /// </summary>
    public static void ApplyMugging(Player p)
    {
        p.Cash = 0;
        p.HapStat -= 3;
    }
}
