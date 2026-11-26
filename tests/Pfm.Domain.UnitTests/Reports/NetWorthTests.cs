using Pfm.Domain.Reports;

namespace Pfm.Domain.UnitTests.Reports;

public class NetWorthTests
{
    private static readonly DateOnly EndOfMarch = new(2026, 3, 31);

    [Fact]
    public void Net_worth_is_the_assets_minus_the_liabilities_owed()
    {
        var netWorth = NetWorthOn.Of(EndOfMarch,
        [
            new NetWorthBalance(HoldsAmountOwed: false, Balance: 1_000_000m),
            new NetWorthBalance(HoldsAmountOwed: false, Balance: 250_000m),
            new NetWorthBalance(HoldsAmountOwed: true, Balance: -300_000m),
            new NetWorthBalance(HoldsAmountOwed: true, Balance: -50_000m),
        ]);

        Assert.Equal((EndOfMarch, 1_250_000m, 350_000m, 900_000m, 0), (netWorth.Date, netWorth.Assets, netWorth.Liabilities, netWorth.NetWorth, netWorth.NotConverted));
    }

    [Fact]
    public void An_overdrawn_account_lowers_the_assets_and_a_card_paid_beyond_what_was_owed_lowers_the_liabilities()
    {
        var netWorth = NetWorthOn.Of(EndOfMarch,
        [
            new NetWorthBalance(HoldsAmountOwed: false, Balance: -100_000m),
            new NetWorthBalance(HoldsAmountOwed: true, Balance: 20_000m),
        ]);

        Assert.Equal((-100_000m, -20_000m, -80_000m), (netWorth.Assets, netWorth.Liabilities, netWorth.NetWorth));
    }

    [Fact]
    public void A_balance_that_is_not_converted_is_left_out_and_counted()
    {
        var netWorth = NetWorthOn.Of(EndOfMarch,
        [
            new NetWorthBalance(HoldsAmountOwed: false, Balance: 1_000_000m),
            new NetWorthBalance(HoldsAmountOwed: false, Balance: null),
            new NetWorthBalance(HoldsAmountOwed: true, Balance: null),
        ]);

        Assert.Equal((1_000_000m, 0m, 1_000_000m, 2), (netWorth.Assets, netWorth.Liabilities, netWorth.NetWorth, netWorth.NotConverted));
    }

    [Fact]
    public void With_nothing_held_net_worth_is_zero() =>
        Assert.Equal((0m, 0m, 0m, 0), NetWorthOn.Of(EndOfMarch, []) is var empty ? (empty.Assets, empty.Liabilities, empty.NetWorth, empty.NotConverted) : default);
}
