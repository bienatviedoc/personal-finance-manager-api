using Pfm.Domain.Accounts;

namespace Pfm.Domain.UnitTests.Accounts;

public class AccountTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = Monday.AddDays(1);
    private static readonly DateOnly FirstOfNovember = new(2026, 11, 1);

    [Fact]
    public void A_new_account_is_active_and_its_balance_is_its_opening_balance()
    {
        var institution = Guid.CreateVersion7();

        var account = Account.Create("  Everyday  ", AccountType.Current, institution, Currency.VND, 1_500_000m, FirstOfNovember, Monday);

        Assert.Equal(("Everyday", AccountType.Current, (Guid?)institution, Currency.VND), (account.Name, account.Type, account.InstitutionId, account.Currency));
        Assert.Equal((1_500_000m, FirstOfNovember), (account.OpeningBalance, account.OpeningDate));
        Assert.Equal(1_500_000m, account.BalanceWith(netTransactions: 0m));
        Assert.Null(account.AmountOwed);
        Assert.False(account.IsArchived);
        Assert.Equal((Monday, Monday), (account.CreatedAt, account.UpdatedAt));
        Assert.Equal(7, account.Id.Version);
    }

    [Theory]
    [InlineData(AccountType.CreditCard)]
    [InlineData(AccountType.Loan)]
    public void A_credit_card_or_loan_opens_with_the_amount_owed_which_it_holds_as_a_negative_balance(AccountType type)
    {
        var account = Account.Create("Visa", type, null, Currency.USD, 250.75m, FirstOfNovember, Monday);

        Assert.Equal(-250.75m, account.OpeningBalance);
        Assert.Equal(-250.75m, account.BalanceWith(netTransactions: 0m));
        Assert.Equal(250.75m, account.AmountOwed);
    }

    [Theory]
    [InlineData(AccountType.Current)]
    [InlineData(AccountType.Savings)]
    [InlineData(AccountType.Cash)]
    [InlineData(AccountType.EWallet)]
    public void Every_other_type_opens_with_its_opening_balance_as_entered(AccountType type)
    {
        var account = Account.Create("Money", type, null, Currency.USD, 250.75m, FirstOfNovember, Monday);

        Assert.Equal(250.75m, account.OpeningBalance);
        Assert.Null(account.AmountOwed);
    }

    [Fact]
    public void The_balance_is_the_opening_balance_plus_what_the_accounts_transactions_come_to()
    {
        var current = Account.Create("Everyday", AccountType.Current, null, Currency.USD, 100m, FirstOfNovember, Monday);
        var card = Account.Create("Visa", AccountType.CreditCard, null, Currency.USD, 100m, FirstOfNovember, Monday);

        Assert.Equal(60.5m, current.BalanceWith(netTransactions: -39.5m));
        Assert.Equal(-139.5m, card.BalanceWith(netTransactions: -39.5m));
    }

    [Fact]
    public void An_overdrawn_account_can_open_below_zero_but_an_amount_owed_is_never_negative()
    {
        var overdrawn = Account.Create("Everyday", AccountType.Current, null, Currency.USD, -20m, FirstOfNovember, Monday);

        Assert.Equal(-20m, overdrawn.OpeningBalance);
        Assert.Throws<ArgumentException>(() => Account.Create("Visa", AccountType.CreditCard, null, Currency.USD, -20m, FirstOfNovember, Monday));
    }

    [Theory]
    [InlineData("", AccountType.Current)]
    [InlineData("   ", AccountType.Current)]
    [InlineData("ok", (AccountType)99)]
    public void An_account_cannot_be_given_a_blank_name_or_a_type_that_does_not_exist(string name, AccountType type)
    {
        Assert.ThrowsAny<ArgumentException>(() => Account.Create(name, type, null, Currency.VND, 0m, FirstOfNovember, Monday));
    }

    [Theory]
    [InlineData(Currency.VND, "0.5")]
    [InlineData(Currency.USD, "0.005")]
    [InlineData(Currency.EUR, "1000000000000000")]
    public void An_amount_has_no_more_decimals_than_its_currency_and_at_most_15_digits_before_them(Currency currency, string amount)
    {
        Assert.Throws<ArgumentException>(() => Account.Create("Everyday", AccountType.Current, null, currency, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), FirstOfNovember, Monday));
    }

    [Fact]
    public void Editing_changes_every_field_and_the_balance_follows_the_new_opening_balance()
    {
        var institution = Guid.CreateVersion7();
        var account = Account.Create("Everyday", AccountType.Current, null, Currency.VND, 1_000m, FirstOfNovember, Monday);

        account.Edit("Rainy day", AccountType.Savings, institution, Currency.USD, 2_500.5m, FirstOfNovember.AddDays(1), hasTransactions: false, Tuesday);

        Assert.Equal(("Rainy day", AccountType.Savings, (Guid?)institution, Currency.USD), (account.Name, account.Type, account.InstitutionId, account.Currency));
        Assert.Equal((2_500.5m, FirstOfNovember.AddDays(1)), (account.OpeningBalance, account.OpeningDate));
        Assert.Equal(2_500.5m, account.BalanceWith(netTransactions: 0m));
        Assert.Equal((Monday, Tuesday), (account.CreatedAt, account.UpdatedAt));
    }

    [Fact]
    public void Editing_an_account_into_a_credit_card_takes_the_amount_as_the_amount_owed()
    {
        var account = Account.Create("Everyday", AccountType.Current, null, Currency.USD, 100m, FirstOfNovember, Monday);

        account.Edit("Visa", AccountType.CreditCard, null, Currency.USD, 300m, FirstOfNovember, hasTransactions: false, Tuesday);

        Assert.Equal((-300m, (decimal?)300m), (account.OpeningBalance, account.AmountOwed));
    }

    [Fact]
    public void An_account_with_transactions_cannot_change_currency_but_everything_else_still_can()
    {
        var account = Account.Create("Everyday", AccountType.Current, null, Currency.VND, 1_000m, FirstOfNovember, Monday);

        var violation = Assert.Throws<AccountRuleViolation>(() =>
            account.Edit("Everyday", AccountType.Current, null, Currency.USD, 1_000m, FirstOfNovember, hasTransactions: true, Tuesday));
        account.Edit("Daily", AccountType.Current, null, Currency.VND, 2_000m, FirstOfNovember, hasTransactions: true, Tuesday);

        Assert.Equal(nameof(Account.Currency), violation.Property);
        Assert.Equal(("Daily", Currency.VND, 2_000m), (account.Name, account.Currency, account.OpeningBalance));
    }

    [Fact]
    public void An_account_can_be_archived_at_any_balance_and_archiving_again_changes_nothing()
    {
        var account = Account.Create("Old card", AccountType.CreditCard, null, Currency.USD, 80m, FirstOfNovember, Monday);

        account.Archive(Monday);
        account.Archive(Tuesday);

        Assert.True(account.IsArchived);
        Assert.Equal(-80m, account.BalanceWith(netTransactions: 0m));
        Assert.Equal(Monday, account.UpdatedAt);
    }
}
