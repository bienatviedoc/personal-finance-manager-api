using Pfm.Domain.Accounts;
using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

public class TransactionTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly Guid Everyday = Guid.CreateVersion7();
    private static readonly Category Salary = Category.Create("Salary", CategoryKind.Income, "briefcase", Color.Parse("#16a34a"), Monday);
    private static readonly Category Groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ea580c"), Monday);
    private static readonly Category Household = Category.Create("Household", CategoryKind.Expense, "home", Color.Parse("#ea580c"), Monday);

    [Fact]
    public void A_recorded_transaction_holds_what_was_entered_in_the_currency_of_its_account()
    {
        var transaction = Transaction.Record(
            TransactionType.Expense, Everyday, Currency.VND, 125_000m, Today.AddDays(-3), Groceries, "  Co.opmart  ", "  weekly shop ", Today, Monday);

        Assert.Equal((TransactionType.Expense, Everyday, Currency.VND, 125_000m), (transaction.Type, transaction.AccountId, transaction.Currency, transaction.Amount));
        Assert.Equal((Today.AddDays(-3), Groceries.Id, "Co.opmart", "weekly shop"), (transaction.Date, transaction.CategoryId, transaction.Payee, transaction.Note));
        Assert.Equal((Monday, Monday), (transaction.CreatedAt, transaction.UpdatedAt));
        Assert.False(transaction.IsDeleted);
        Assert.Equal(7, transaction.Id.Version);
    }

    [Fact]
    public void A_payee_and_a_note_are_optional_and_blank_ones_are_kept_as_none()
    {
        var transaction = Transaction.Record(TransactionType.Income, Everyday, Currency.USD, 12.5m, Today, Salary, "   ", null, Today, Monday);

        Assert.Null(transaction.Payee);
        Assert.Null(transaction.Note);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void An_amount_must_be_greater_than_zero(string amount)
    {
        var violation = Assert.Throws<TransactionRuleViolation>(() => Spend(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(nameof(Transaction.Amount), violation.Property);
    }

    [Theory]
    [InlineData(Currency.VND, "0.5")]
    [InlineData(Currency.USD, "0.005")]
    [InlineData(Currency.EUR, "1000000000000000")]
    public void An_amount_has_no_more_decimals_than_the_accounts_currency_and_at_most_15_digits_before_them(Currency currency, string amount)
    {
        var violation = Assert.Throws<TransactionRuleViolation>(() =>
            Transaction.Record(TransactionType.Expense, Everyday, currency, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Today, Groceries, null, null, Today, Monday));

        Assert.Equal(nameof(Transaction.Amount), violation.Property);
    }

    [Fact]
    public void A_transaction_can_be_dated_today_or_earlier_but_not_in_the_future()
    {
        Assert.Equal(Today, Spend(10m, date: Today).Date);
        Assert.Equal(new DateOnly(2019, 2, 28), Spend(10m, date: new DateOnly(2019, 2, 28)).Date);

        var violation = Assert.Throws<TransactionRuleViolation>(() => Spend(10m, date: Today.AddDays(1)));
        Assert.Equal(nameof(Transaction.Date), violation.Property);
    }

    [Fact]
    public void An_income_transaction_takes_only_an_income_category_and_an_expense_only_an_expense_category()
    {
        var incomeAsGroceries = Assert.Throws<TransactionRuleViolation>(() =>
            Transaction.Record(TransactionType.Income, Everyday, Currency.VND, 10m, Today, Groceries, null, null, Today, Monday));
        var expenseAsSalary = Assert.Throws<TransactionRuleViolation>(() =>
            Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, 10m, Today, Salary, null, null, Today, Monday));

        Assert.Equal(nameof(Transaction.CategoryId), incomeAsGroceries.Property);
        Assert.Equal(nameof(Transaction.CategoryId), expenseAsSalary.Property);
        Assert.Equal(Salary.Id, Transaction.Record(TransactionType.Income, Everyday, Currency.VND, 10m, Today, Salary, null, null, Today, Monday).CategoryId);
    }

    [Fact]
    public void A_subcategory_can_be_used_like_any_category_of_its_kind()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", Color.Parse("#ea580c"), Monday);
        var coffee = Category.Create("Coffee", CategoryKind.Expense, "coffee", Color.Parse("#ea580c"), Monday, parent: food);

        Assert.Equal(coffee.Id, Spend(45_000m, category: coffee).CategoryId);
    }

    [Fact]
    public void An_archived_category_takes_no_new_transactions_but_a_transaction_keeps_the_one_it_has()
    {
        var gym = Category.Create("Gym", CategoryKind.Expense, "dumbbell", Color.Parse("#ea580c"), Monday);
        var old = Category.Create("Old", CategoryKind.Expense, "archive", Color.Parse("#ea580c"), Monday);
        var transaction = Spend(300_000m, category: gym);
        gym.Archive([], Monday);
        old.Archive([], Monday);

        transaction.Edit(TransactionType.Expense, Everyday, Currency.VND, 350_000m, Today, gym, null, null, Today, Tuesday);

        Assert.Equal((350_000m, gym.Id), (transaction.Amount, transaction.CategoryId));
        Assert.Equal(nameof(Transaction.CategoryId), Assert.Throws<TransactionRuleViolation>(() => Spend(10m, category: gym)).Property);
        Assert.Equal(
            nameof(Transaction.CategoryId),
            Assert.Throws<TransactionRuleViolation>(() => transaction.Edit(TransactionType.Expense, Everyday, Currency.VND, 350_000m, Today, old, null, null, Today, Tuesday)).Property);
    }

    [Theory]
    [InlineData(201, 0)]
    [InlineData(0, 1001)]
    public void A_payee_holds_at_most_200_characters_and_a_note_1000(int payeeLength, int noteLength)
    {
        Assert.Throws<ArgumentException>(() =>
            Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, 10m, Today, Groceries, new string('p', payeeLength), new string('n', noteLength), Today, Monday));
    }

    [Fact]
    public void A_transaction_needs_an_account_and_a_type_that_exists()
    {
        Assert.Throws<ArgumentException>(() => Transaction.Record(TransactionType.Expense, Guid.Empty, Currency.VND, 10m, Today, Groceries, null, null, Today, Monday));
        Assert.Throws<ArgumentOutOfRangeException>(() => Transaction.Record((TransactionType)9, Everyday, Currency.VND, 10m, Today, Groceries, null, null, Today, Monday));
        Assert.Throws<ArgumentOutOfRangeException>(() => Transaction.Record(TransactionType.Expense, Everyday, (Currency)9, 10m, Today, Groceries, null, null, Today, Monday));
    }

    [Fact]
    public void Editing_changes_every_field_and_moving_it_to_another_account_takes_that_accounts_currency()
    {
        var dollars = Guid.CreateVersion7();
        var transaction = Spend(125_000m);

        transaction.Edit(TransactionType.Income, dollars, Currency.USD, 99.95m, Today.AddDays(-1), Salary, "Acme", "bonus", Today, Tuesday);

        Assert.Equal((TransactionType.Income, dollars, Currency.USD, 99.95m), (transaction.Type, transaction.AccountId, transaction.Currency, transaction.Amount));
        Assert.Equal((Today.AddDays(-1), Salary.Id, "Acme", "bonus"), (transaction.Date, transaction.CategoryId, transaction.Payee, transaction.Note));
        Assert.Equal((Monday, Tuesday), (transaction.CreatedAt, transaction.UpdatedAt));
    }

    [Fact]
    public void An_edit_is_held_to_the_same_rules_and_a_refused_one_changes_nothing()
    {
        var transaction = Spend(125_000m);

        Assert.Throws<TransactionRuleViolation>(() => transaction.Edit(TransactionType.Expense, Everyday, Currency.VND, 0m, Today, Groceries, null, null, Today, Tuesday));
        Assert.Throws<TransactionRuleViolation>(() => transaction.Edit(TransactionType.Expense, Everyday, Currency.VND, 10m, Today.AddDays(1), Groceries, null, null, Today, Tuesday));
        Assert.Throws<TransactionRuleViolation>(() => transaction.Edit(TransactionType.Income, Everyday, Currency.VND, 10m, Today, Groceries, null, null, Today, Tuesday));

        Assert.Equal((TransactionType.Expense, 125_000m, Today, Monday), (transaction.Type, transaction.Amount, transaction.Date, transaction.UpdatedAt));
    }

    [Fact]
    public void The_account_balance_is_its_opening_balance_plus_income_minus_expenses()
    {
        var account = Account.Create("Everyday", AccountType.Current, null, Currency.VND, 1_000_000m, new DateOnly(2026, 11, 1), Monday);
        Transaction[] transactions =
        [
            Transaction.Record(TransactionType.Income, account.Id, Currency.VND, 15_000_000m, Today, Salary, null, null, Today, Monday),
            Transaction.Record(TransactionType.Expense, account.Id, Currency.VND, 125_000m, Today, Groceries, null, null, Today, Monday),
            Transaction.Record(TransactionType.Expense, account.Id, Currency.VND, 2_500_000m, Today, Groceries, null, null, Today, Monday),
        ];

        Assert.Equal(13_375_000m, account.BalanceWith(transactions.Sum(transaction => transaction.SignedAmount)));
        Assert.Equal(1_000_000m, account.BalanceWith(0m));
    }

    [Fact]
    public void Expenses_take_an_amount_owed_further_below_zero_and_income_brings_it_back()
    {
        var card = Account.Create("Visa", AccountType.CreditCard, null, Currency.USD, 100m, new DateOnly(2026, 11, 1), Monday);
        Transaction[] transactions =
        [
            Transaction.Record(TransactionType.Expense, card.Id, Currency.USD, 39.5m, Today, Groceries, null, null, Today, Monday),
            Transaction.Record(TransactionType.Income, card.Id, Currency.USD, 10.25m, Today, Salary, null, null, Today, Monday),
        ];

        Assert.Equal(-129.25m, card.BalanceWith(transactions.Sum(transaction => transaction.SignedAmount)));
    }

    [Fact]
    public void A_refund_is_an_expense_that_raises_the_account_balance()
    {
        var account = Account.Create("Everyday", AccountType.Current, null, Currency.VND, 1_000_000m, new DateOnly(2026, 11, 1), Monday);
        var shoes = Spend(800_000m);
        var refund = Refund(300_000m);

        Assert.Equal((TransactionType.Expense, true, 300_000m), (refund.Type, refund.IsRefund, refund.Amount));
        Assert.False(shoes.IsRefund);
        Assert.Equal(300_000m, refund.SignedAmount);
        Assert.Equal(500_000m, account.BalanceWith(shoes.SignedAmount + refund.SignedAmount));
    }

    [Fact]
    public void A_deleted_refund_leaves_the_balance()
    {
        var refund = Refund(300_000m);

        refund.Delete(Monday);

        Assert.Equal(0m, refund.SignedAmount);
    }

    [Fact]
    public void Only_an_expense_can_be_flagged_as_a_refund()
    {
        var violation = Assert.Throws<TransactionRuleViolation>(() =>
            Transaction.Record(TransactionType.Income, Everyday, Currency.VND, 10m, Today, Salary, null, null, Today, Monday, isRefund: true));

        Assert.Equal(nameof(Transaction.IsRefund), violation.Property);
    }

    [Fact]
    public void An_edit_can_flag_an_expense_as_a_refund_or_take_the_flag_away_and_neither_converts_it_again()
    {
        var transaction = Spend(125_000m);
        transaction.ConvertInto(Currency.USD, 5m, 0.00004m, Today);

        transaction.Edit(TransactionType.Expense, Everyday, Currency.VND, 125_000m, Today, Groceries, null, null, Today, Tuesday, isRefund: true);
        Assert.Equal((true, 125_000m, true), (transaction.IsRefund, transaction.SignedAmount, transaction.IsConverted));

        transaction.Edit(TransactionType.Expense, Everyday, Currency.VND, 125_000m, Today, Groceries, null, null, Today, Tuesday);
        Assert.Equal((false, -125_000m, true), (transaction.IsRefund, transaction.SignedAmount, transaction.IsConverted));

        Assert.Equal(
            nameof(Transaction.IsRefund),
            Assert.Throws<TransactionRuleViolation>(() =>
                transaction.Edit(TransactionType.Income, Everyday, Currency.VND, 125_000m, Today, Salary, null, null, Today, Tuesday, isRefund: true)).Property);
    }

    [Fact]
    public void A_deleted_transaction_leaves_the_balance_at_once_and_deleting_it_again_changes_nothing()
    {
        var account = Account.Create("Everyday", AccountType.Current, null, Currency.VND, 1_000_000m, new DateOnly(2026, 11, 1), Monday);
        var kept = Spend(100_000m);
        var deleted = Spend(250_000m);

        deleted.Delete(Monday);
        deleted.Delete(Tuesday);

        Assert.True(deleted.IsDeleted);
        Assert.Equal((Monday, Monday), (deleted.DeletedAt, deleted.UpdatedAt));
        Assert.Equal(900_000m, account.BalanceWith(kept.SignedAmount + deleted.SignedAmount));
        Assert.Throws<InvalidOperationException>(() => deleted.Edit(TransactionType.Expense, Everyday, Currency.VND, 10m, Today, Groceries, null, null, Today, Tuesday));
    }

    [Fact]
    public void A_deleted_transaction_restored_within_30_days_is_back_in_the_balance_as_it_was()
    {
        var account = Account.Create("Everyday", AccountType.Current, null, Currency.VND, 1_000_000m, new DateOnly(2026, 11, 1), Monday);
        var transaction = Spend(250_000m);
        transaction.Delete(Monday);
        var restoredAt = Monday + Transaction.RecoveryPeriod;

        Assert.True(transaction.IsRecoverableAt(restoredAt));
        transaction.Restore(restoredAt);

        Assert.False(transaction.IsDeleted);
        Assert.Equal(((DateTimeOffset?)null, restoredAt), (transaction.DeletedAt, transaction.UpdatedAt));
        Assert.Equal(750_000m, account.BalanceWith(transaction.SignedAmount));
    }

    [Fact]
    public void A_transaction_deleted_more_than_30_days_ago_can_no_longer_be_restored()
    {
        var transaction = Spend(250_000m);
        transaction.Delete(Monday);
        var tooLate = Monday + Transaction.RecoveryPeriod + TimeSpan.FromSeconds(1);

        Assert.False(transaction.IsRecoverableAt(tooLate));
        Assert.Throws<InvalidOperationException>(() => transaction.Restore(tooLate));
        Assert.Equal(Monday, transaction.DeletedAt);
    }

    [Fact]
    public void Restoring_a_transaction_that_is_not_deleted_changes_nothing()
    {
        var transaction = Spend(250_000m);

        transaction.Restore(Tuesday);

        Assert.Equal(((DateTimeOffset?)null, Monday), (transaction.DeletedAt, transaction.UpdatedAt));
        Assert.False(transaction.IsRecoverableAt(Tuesday));
    }

    [Fact]
    public void A_recorded_transaction_is_not_converted_until_its_amount_is_converted_into_the_base_currency()
    {
        var dollars = Transaction.Record(TransactionType.Expense, Everyday, Currency.USD, 100m, Today, Groceries, null, null, Today, Monday);
        Assert.False(dollars.IsConverted);

        dollars.ConvertInto(Currency.VND, 2_500_000m, 25_000m, Today.AddDays(-2));

        Assert.True(dollars.IsConverted);
        Assert.Equal(BaseCurrencyConversion.Converted(Currency.VND, 2_500_000m, 25_000m, Today.AddDays(-2)), dollars.Conversion);
        Assert.Equal((100m, Currency.USD), (dollars.Amount, dollars.Currency));
    }

    [Fact]
    public void An_amount_is_converted_once_and_a_second_conversion_into_the_same_base_currency_is_refused()
    {
        var dollars = Transaction.Record(TransactionType.Expense, Everyday, Currency.USD, 100m, Today, Groceries, null, null, Today, Monday);
        dollars.ConvertInto(Currency.VND, 2_500_000m, 25_000m, Today);

        Assert.True(dollars.IsConvertedInto(Currency.VND));
        Assert.Throws<InvalidOperationException>(() => dollars.ConvertInto(Currency.VND, 2_600_000m, 26_000m, Today));
        Assert.Throws<InvalidOperationException>(() => dollars.FlagNotConverted(Currency.VND));
        Assert.Equal((2_500_000m, 25_000m), (dollars.Conversion?.Amount, dollars.Conversion?.ExchangeRate));
    }

    [Fact]
    public void With_no_exchange_rate_a_transaction_is_flagged_not_converted_in_the_base_currency_and_converted_when_one_arrives()
    {
        var dollars = Transaction.Record(TransactionType.Expense, Everyday, Currency.USD, 100m, Today, Groceries, null, null, Today, Monday);

        dollars.FlagNotConverted(Currency.VND);

        Assert.False(dollars.IsConverted);
        Assert.False(dollars.IsConvertedInto(Currency.VND));
        Assert.Equal(BaseCurrencyConversion.NotConverted(Currency.VND), dollars.Conversion);

        dollars.ConvertInto(Currency.VND, 2_500_000m, 25_000m, Today);

        Assert.Equal(2_500_000m, dollars.Conversion?.Amount);
    }

    [Fact]
    public void An_edit_that_leaves_the_amount_date_and_account_alone_keeps_the_converted_amount()
    {
        var dollars = Transaction.Record(TransactionType.Expense, Everyday, Currency.USD, 100m, Today, Groceries, null, null, Today, Monday);
        dollars.ConvertInto(Currency.VND, 2_500_000m, 25_000m, Today);
        var eatingOut = Category.Create("Eating out", CategoryKind.Expense, "utensils", Color.Parse("#ea580c"), Monday);

        dollars.Edit(TransactionType.Expense, Everyday, Currency.USD, 100m, Today, eatingOut, "Pizza 4P's", "dinner", Today, Tuesday);

        Assert.Equal(BaseCurrencyConversion.Converted(Currency.VND, 2_500_000m, 25_000m, Today), dollars.Conversion);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("date")]
    [InlineData("account")]
    public void An_edit_of_the_amount_the_date_or_the_account_drops_the_converted_amount_so_it_is_converted_again(string changed)
    {
        var dollars = Transaction.Record(TransactionType.Expense, Everyday, Currency.USD, 100m, Today, Groceries, null, null, Today, Monday);
        dollars.ConvertInto(Currency.VND, 2_500_000m, 25_000m, Today);

        dollars.Edit(
            TransactionType.Expense, changed == "account" ? Guid.CreateVersion7() : Everyday, Currency.USD, changed == "amount" ? 120m : 100m,
            changed == "date" ? Today.AddDays(-1) : Today, Groceries, null, null, Today, Tuesday);

        Assert.False(dollars.IsConverted);
        Assert.Equal(BaseCurrencyConversion.NotConverted(Currency.VND), dollars.Conversion);
        dollars.ConvertInto(Currency.VND, 3_000_000m, 25_000m, Today.AddDays(-1));
        Assert.Equal(3_000_000m, dollars.Conversion?.Amount);
    }

    [Fact]
    public void A_refused_edit_keeps_the_converted_amount()
    {
        var dollars = Transaction.Record(TransactionType.Expense, Everyday, Currency.USD, 100m, Today, Groceries, null, null, Today, Monday);
        dollars.ConvertInto(Currency.VND, 2_500_000m, 25_000m, Today);

        Assert.Throws<TransactionRuleViolation>(() => dollars.Edit(TransactionType.Expense, Everyday, Currency.USD, 120m, Today.AddDays(1), Groceries, null, null, Today, Tuesday));

        Assert.Equal(2_500_000m, dollars.Conversion?.Amount);
    }

    [Fact]
    public void A_converted_amount_can_be_converted_into_another_base_currency_or_flagged_not_converted_in_it()
    {
        var dollars = Transaction.Record(TransactionType.Expense, Everyday, Currency.USD, 100m, Today, Groceries, null, null, Today, Monday);
        dollars.ConvertInto(Currency.VND, 2_500_000m, 25_000m, Today);

        dollars.ConvertInto(Currency.EUR, 80m, 0.8m, Today);
        Assert.Equal(BaseCurrencyConversion.Converted(Currency.EUR, 80m, 0.8m, Today), dollars.Conversion);

        dollars.FlagNotConverted(Currency.VND);
        Assert.Equal(BaseCurrencyConversion.NotConverted(Currency.VND), dollars.Conversion);
    }

    [Fact]
    public void A_converted_amount_fits_the_base_currency_and_its_exchange_rate_is_positive_and_of_the_transactions_date_or_earlier()
    {
        var dollars = Transaction.Record(TransactionType.Expense, Everyday, Currency.USD, 100m, Today.AddDays(-1), Groceries, null, null, Today, Monday);

        Assert.Throws<ArgumentException>(() => dollars.ConvertInto(Currency.VND, 2_500_000.5m, 25_000m, Today.AddDays(-1)));
        Assert.Throws<ArgumentException>(() => dollars.ConvertInto(Currency.VND, -1m, 25_000m, Today.AddDays(-1)));
        Assert.Throws<ArgumentException>(() => dollars.ConvertInto(Currency.VND, 2_500_000m, 0m, Today.AddDays(-1)));
        Assert.Throws<ArgumentException>(() => dollars.ConvertInto(Currency.VND, 2_500_000m, 25_000m, Today));
        Assert.Throws<ArgumentOutOfRangeException>(() => dollars.ConvertInto((Currency)9, 2_500_000m, 25_000m, Today.AddDays(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => dollars.FlagNotConverted((Currency)9));
        Assert.False(dollars.IsConverted);
        Assert.True(BaseCurrencyConversion.CanHold(0m, Currency.USD));
        Assert.False(BaseCurrencyConversion.CanHold(1_000_000_000_000_000m, Currency.VND));
    }

    /// <summary>An expense in VND on the everyday account, recorded on Monday.</summary>
    [Fact]
    public void A_transaction_that_is_not_split_has_one_line_with_its_category_and_whole_amount()
    {
        var transaction = Spend(125_000m);

        var line = Assert.Single(transaction.Lines);
        Assert.Equal((Groceries.Id, 125_000m, null), (line.CategoryId, line.Amount, line.Note));
        Assert.False(transaction.IsSplit);
    }

    [Fact]
    public void A_transaction_can_be_split_across_several_categories_each_line_with_an_amount_and_an_optional_note()
    {
        var transaction = Split(500_000m, new SplitLine(Groceries, 320_000m, "  food "), new SplitLine(Household, 180_000m, "  "));

        Assert.True(transaction.IsSplit);
        Assert.Equal(500_000m, transaction.Amount);
        Assert.Equal(
            [(Groceries.Id, 320_000m, "food"), (Household.Id, 180_000m, null)],
            transaction.Lines.Select(line => (line.CategoryId, line.Amount, line.Note)));
        Assert.Equal(Groceries.Id, transaction.CategoryId);
    }

    [Theory]
    [InlineData("320000", "179999")]
    [InlineData("320000", "180001")]
    [InlineData("500000", "500000")]
    public void The_lines_of_a_split_must_sum_exactly_to_the_transaction_amount(string first, string second)
    {
        var violation = Assert.Throws<TransactionRuleViolation>(() =>
            Split(500_000m, new SplitLine(Groceries, Amount(first)), new SplitLine(Household, Amount(second))));

        Assert.Equal(nameof(Transaction.Lines), violation.Property);
    }

    [Fact]
    public void Lines_that_sum_to_the_amount_to_the_last_decimal_are_a_split()
    {
        var transaction = Transaction.RecordSplit(
            TransactionType.Expense, Everyday, Currency.USD, 100m,
            Today, [new SplitLine(Groceries, 33.33m), new SplitLine(Household, 33.33m), new SplitLine(Groceries, 33.34m)], null, null, Today, Monday);

        Assert.Equal(100m, transaction.Lines.Sum(line => line.Amount));
        Assert.Equal(3, transaction.Lines.Count);
    }

    [Theory]
    [InlineData("0", "500000")]
    [InlineData("-1", "500001")]
    [InlineData("0.5", "499999.5")]
    public void Every_line_of_a_split_has_an_amount_above_zero_that_fits_the_currency(string first, string second)
    {
        var violation = Assert.Throws<TransactionRuleViolation>(() =>
            Split(500_000m, new SplitLine(Groceries, Amount(first)), new SplitLine(Household, Amount(second))));

        Assert.Equal("Lines[0].Amount", violation.Property);
    }

    [Fact]
    public void A_split_has_at_least_two_lines_and_at_most_fifty()
    {
        var one = Assert.Throws<TransactionRuleViolation>(() => Split(500_000m, new SplitLine(Groceries, 500_000m)));
        var many = Assert.Throws<TransactionRuleViolation>(() =>
            Split(51m, [.. Enumerable.Range(0, 51).Select(_ => new SplitLine(Groceries, 1m))]));

        Assert.Equal(nameof(Transaction.Lines), one.Property);
        Assert.Equal(nameof(Transaction.Lines), many.Property);
        Assert.Equal(50, Split(50m, [.. Enumerable.Range(0, 50).Select(_ => new SplitLine(Groceries, 1m))]).Lines.Count);
    }

    [Fact]
    public void Every_line_of_a_split_takes_a_category_of_the_kind_the_transactions_type_takes()
    {
        var violation = Assert.Throws<TransactionRuleViolation>(() =>
            Split(500_000m, new SplitLine(Groceries, 320_000m), new SplitLine(Salary, 180_000m)));

        Assert.Equal("Lines[1].CategoryId", violation.Property);
    }

    [Fact]
    public void A_split_takes_no_archived_category_but_an_edit_keeps_the_ones_its_lines_already_have()
    {
        var gym = Category.Create("Gym", CategoryKind.Expense, "dumbbell", Color.Parse("#ea580c"), Monday);
        var old = Category.Create("Old", CategoryKind.Expense, "archive", Color.Parse("#ea580c"), Monday);
        var transaction = Split(500_000m, new SplitLine(Groceries, 320_000m), new SplitLine(gym, 180_000m));
        gym.Archive([], Monday);
        old.Archive([], Monday);

        transaction.EditSplit(
            TransactionType.Expense, Everyday, Currency.VND, 500_000m, Today, [new SplitLine(gym, 200_000m), new SplitLine(Groceries, 300_000m)], null, null, Today, Tuesday);

        Assert.Equal([(gym.Id, 200_000m), (Groceries.Id, 300_000m)], transaction.Lines.Select(line => (line.CategoryId, line.Amount)));
        Assert.Equal("Lines[1].CategoryId", Assert.Throws<TransactionRuleViolation>(() => Split(10m, new SplitLine(Groceries, 5m), new SplitLine(gym, 5m))).Property);
        Assert.Equal(
            "Lines[0].CategoryId",
            Assert.Throws<TransactionRuleViolation>(() => transaction.EditSplit(
                TransactionType.Expense, Everyday, Currency.VND, 500_000m, Today, [new SplitLine(old, 200_000m), new SplitLine(Groceries, 300_000m)], null, null, Today, Tuesday)).Property);
    }

    [Fact]
    public void An_edit_can_split_a_transaction_and_make_a_split_one_category_again()
    {
        var transaction = Spend(500_000m);

        transaction.EditSplit(
            TransactionType.Expense, Everyday, Currency.VND, 500_000m, Today, [new SplitLine(Groceries, 320_000m), new SplitLine(Household, 180_000m)], null, null, Today, Tuesday);
        Assert.True(transaction.IsSplit);

        transaction.Edit(TransactionType.Expense, Everyday, Currency.VND, 450_000m, Today, Household, null, null, Today, Tuesday);

        var line = Assert.Single(transaction.Lines);
        Assert.Equal((Household.Id, 450_000m, Household.Id), (line.CategoryId, line.Amount, transaction.CategoryId));
        Assert.False(transaction.IsSplit);
    }

    [Fact]
    public void A_refused_edit_of_a_split_leaves_its_lines_as_they_were()
    {
        var transaction = Split(500_000m, new SplitLine(Groceries, 320_000m), new SplitLine(Household, 180_000m));

        Assert.Throws<TransactionRuleViolation>(() => transaction.EditSplit(
            TransactionType.Expense, Everyday, Currency.VND, 600_000m, Today, [new SplitLine(Groceries, 320_000m), new SplitLine(Household, 180_000m)], null, null, Today, Tuesday));

        Assert.Equal((500_000m, 2, Monday), (transaction.Amount, transaction.Lines.Count, transaction.UpdatedAt));
    }

    [Theory]
    [InlineData("500000", "320000,180000", "0")]
    [InlineData("500000", "320000", "180000")]
    [InlineData("500000", "320000,200000", "-20000")]
    [InlineData("100", "33.33,33.33", "33.34")]
    public void What_remains_to_split_is_the_amount_minus_its_lines(string amount, string lines, string remaining)
    {
        Assert.Equal(Amount(remaining), Transaction.RemainingToSplit(Amount(amount), lines.Split(',').Select(Amount)));
    }

    private static decimal Amount(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    private static Transaction Split(decimal amount, params SplitLine[] lines) =>
        Transaction.RecordSplit(TransactionType.Expense, Everyday, Currency.VND, amount, Today, lines, null, null, Today, Monday);

    private static Transaction Spend(decimal amount, DateOnly? date = null, Category? category = null) =>
        Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, amount, date ?? Today, category ?? Groceries, null, null, Today, Monday);

    private static Transaction Refund(decimal amount, DateOnly? date = null, Category? category = null) =>
        Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, amount, date ?? Today, category ?? Groceries, null, null, Today, Monday, isRefund: true);
}
