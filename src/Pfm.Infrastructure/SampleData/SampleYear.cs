using Pfm.Domain;
using Pfm.Domain.Accounts;
using Pfm.Domain.Budgets;
using Pfm.Domain.Transactions;

namespace Pfm.Infrastructure.SampleData;

/// <summary>
/// A year of a sample user's money, up to today: what <see cref="SampleDataSeeder"/> saves. Every account is in
/// the user's base currency, so every transaction is converted at once, at a rate of 1.
/// </summary>
internal sealed class SampleYear
{
    private readonly DateOnly _today;
    private readonly Currency _currency;
    private readonly DateTimeOffset _now;
    private readonly Dictionary<string, Category> _categories;
    private readonly Random _random = new(49);
    private readonly Account _everyday;
    private readonly Account _savings;
    private readonly Account _creditCard;
    private readonly Account _cash;

    /// <param name="categories">The user's categories: the default set, in English.</param>
    public SampleYear(DateOnly today, Currency currency, DateTimeOffset now, IReadOnlyList<Category> categories)
    {
        _today = today;
        _currency = currency;
        _now = now;
        _categories = categories.ToDictionary(category => category.Name);
        From = today.AddYears(-1).AddDays(1);

        var institution = Institution.Create("Vietcombank", Color.Parse("#16a34a"), logoKey: null, now);
        var cardIssuer = Institution.Create("Techcombank", Color.Parse("#dc2626"), logoKey: null, now);
        Institutions = [institution, cardIssuer];
        _everyday = Account.Create("Everyday", AccountType.Current, institution.Id, currency, 20_000_000, From, now);
        _savings = Account.Create("Savings", AccountType.Savings, institution.Id, currency, 100_000_000, From, now);
        _creditCard = Account.Create(
            "Credit card", AccountType.CreditCard, cardIssuer.Id, currency, 0, From, now, Last4Digits.Parse("4242"), new CreditCardTerms(50_000_000, 25, 10));
        _cash = Account.Create("Cash", AccountType.Cash, institutionId: null, currency, 2_000_000, From, now);
        Accounts = [_everyday, _savings, _creditCard, _cash];
        Budgets = [.. MakeBudgets()];
    }

    /// <summary>The first day of the year: a year ago tomorrow.</summary>
    public DateOnly From { get; }

    public IReadOnlyList<Institution> Institutions { get; }

    public IReadOnlyList<Account> Accounts { get; }

    public IReadOnlyList<Budget> Budgets { get; }

    private static readonly (string Category, decimal Amount)[] MonthlyBudgets =
    [
        ("Food & drink", 6_000_000), ("Transport", 1_500_000), ("Housing", 10_000_000), ("Shopping", 2_000_000),
        ("Health", 800_000), ("Entertainment", 1_000_000),
    ];

    /// <summary>The year's transactions, in no order, each leg of a transfer on its own.</summary>
    /// <param name="count">How many; left out, a few a day on top of the monthly ones.</param>
    public IEnumerable<Transaction> Transactions(int? count)
    {
        var monthly = Monthly().ToList();
        foreach (var transaction in monthly)
        {
            yield return transaction;
        }

        var everyday = count is { } wanted ? wanted - monthly.Count : -1;
        if (everyday >= 0)
        {
            var days = _today.DayNumber - From.DayNumber + 1;
            for (var i = 0; i < everyday; i++)
            {
                yield return Everyday(From.AddDays(_random.Next(days)));
            }

            yield break;
        }

        for (var day = From; day <= _today; day = day.AddDays(1))
        {
            for (var i = _random.Next(1, 5); i > 0; i--)
            {
                yield return Everyday(day);
            }
        }
    }

    private IEnumerable<Budget> MakeBudgets()
    {
        foreach (var month in BudgetMonth.Spanning(From, _today))
        {
            var budgeted = new List<Guid>();
            foreach (var (name, amount) in MonthlyBudgets)
            {
                var category = _categories[name];
                var subcategories = _categories.Values.Where(sub => sub.ParentId == category.Id).Select(sub => sub.Id).ToList();
                yield return Budget.Set(
                    new BudgetCategory(category.Id, category.ParentId, subcategories, category.Kind == CategoryKind.Expense, category.IsArchived),
                    month, amount, _currency, budgeted, _now);
                budgeted.Add(category.Id);
            }
        }
    }

    /// <summary>What comes round every month: pay, bills, saving and paying off the credit card.</summary>
    private IEnumerable<Transaction> Monthly()
    {
        for (var month = new DateOnly(From.Year, From.Month, 1); month <= _today; month = month.AddMonths(1))
        {
            var bills = new (int Day, Func<DateOnly, IEnumerable<Transaction>> Make)[]
            {
                (1, date => [Record(TransactionType.Expense, _everyday, "Rent", 8_000_000, date, "Landlord", "Monthly rent")]),
                (5, date => [Record(TransactionType.Income, _everyday, "Salary", 30_000_000, date, "Acme Vietnam", null)]),
                (6, date => Transfer(_everyday, _savings, 3_000_000, date, "Saving")),
                (10, date => [Record(TransactionType.Expense, _everyday, "Electricity", Between(500_000, 1_500_000), date, "EVN", null)]),
                (11, date => Transfer(_everyday, _creditCard, 6_000_000, date, "Credit card payment")),
                (12, date => [Record(TransactionType.Expense, _everyday, "Water", Between(120_000, 250_000), date, "Sawaco", null)]),
                (15, date => [Record(TransactionType.Expense, _creditCard, "Internet & phone", 350_000, date, "Viettel", null)]),
                (18, date => [Record(TransactionType.Expense, _creditCard, "Subscriptions", 260_000, date, "Netflix", null)]),
                (28, date => [Record(TransactionType.Income, _savings, "Interest", Between(250_000, 350_000), date, "Vietcombank", null)]),
            };
            foreach (var (day, make) in bills)
            {
                var date = month.AddDays(day - 1);
                if (date >= From && date <= _today)
                {
                    foreach (var transaction in make(date))
                    {
                        yield return transaction;
                    }
                }
            }
        }
    }

    private sealed record Spending(string Category, decimal Least, decimal Most, params string[] Payees);

    private static readonly Spending[] Spendings =
    [
        new("Groceries", 80_000, 900_000, "WinMart", "Co.opmart", "Bach Hoa Xanh"),
        new("Groceries", 80_000, 900_000, "WinMart", "Co.opmart", "Bach Hoa Xanh"),
        new("Eating out", 50_000, 500_000, "Pho 24", "Com Tam Ba Ghien", "Pizza 4P's"),
        new("Eating out", 50_000, 500_000, "Pho 24", "Com Tam Ba Ghien", "Pizza 4P's"),
        new("Coffee", 29_000, 95_000, "Highlands Coffee", "The Coffee House", "Phuc Long"),
        new("Coffee", 29_000, 95_000, "Highlands Coffee", "The Coffee House", "Phuc Long"),
        new("Fuel", 50_000, 200_000, "Petrolimex"),
        new("Taxi & ride-hailing", 20_000, 250_000, "Grab", "Be", "Xanh SM"),
        new("Parking", 5_000, 30_000, "Parking"),
        new("Clothes", 200_000, 2_000_000, "Uniqlo", "Zara"),
        new("Household", 100_000, 3_000_000, "Dien May Xanh", "Shopee"),
        new("Medicine", 30_000, 500_000, "Pharmacity", "Long Chau"),
        new("Movies & events", 90_000, 400_000, "CGV"),
        new("Books & courses", 80_000, 600_000, "Fahasa", "Tiki"),
        new("Gifts & donations", 100_000, 1_000_000, "Gift"),
        new("Personal care", 50_000, 600_000, "Guardian"),
    ];

    /// <summary>One everyday expense on a day: most often bought outright, now and then split or a refund.</summary>
    private Transaction Everyday(DateOnly date)
    {
        var spending = Spendings[_random.Next(Spendings.Length)];
        var account = _random.Next(20) switch
        {
            < 10 => _everyday,
            < 17 => _creditCard,
            _ => _cash,
        };
        var amount = Between(spending.Least, spending.Most);
        var payee = spending.Payees[_random.Next(spending.Payees.Length)];
        var roll = _random.Next(100);
        if (roll < 5 && spending.Category == "Groceries")
        {
            var household = Math.Round(amount / 3, 0);
            return Convert(Transaction.RecordSplit(
                TransactionType.Expense, new AccountAmount(account.Id, _currency, amount), date,
                [new SplitLine(_categories["Groceries"], amount - household), new SplitLine(_categories["Household"], household, "Cleaning")],
                payee, note: null, Now));
        }

        if (roll < 2)
        {
            return Convert(Transaction.Record(
                TransactionType.Expense, new AccountAmount(account.Id, _currency, amount), date, _categories[spending.Category], payee, "Returned", Now, isRefund: true));
        }

        return Record(TransactionType.Expense, account, spending.Category, amount, date, payee, roll < 10 ? "Shared with friends" : null);
    }

    /// <summary>The moment every sample transaction is recorded at.</summary>
    private Moment Now => new(_today, _now);

    private Transaction Record(TransactionType type, Account account, string category, decimal amount, DateOnly date, string? payee, string? note) =>
        Convert(Transaction.Record(type, new AccountAmount(account.Id, _currency, amount), date, _categories[category], payee, note, Now));

    private IEnumerable<Transaction> Transfer(Account from, Account to, decimal amount, DateOnly date, string note)
    {
        var transfer = Domain.Transactions.Transfer.Record(new AccountAmount(from.Id, _currency, amount), new AccountAmount(to.Id, _currency, amount), date, note, Now);
        return [Convert(transfer.OutLeg), Convert(transfer.InLeg)];
    }

    /// <summary>Converts into the base currency, which every account is in: at a rate of 1, as the app converts it.</summary>
    private Transaction Convert(Transaction transaction)
    {
        transaction.ConvertInto(_currency, transaction.Amount, 1m, transaction.Date);
        return transaction;
    }

    /// <summary>A whole thousand between the two.</summary>
    private decimal Between(decimal least, decimal most) => Math.Round((decimal)_random.Next((int)least, (int)most + 1) / 1_000, 0) * 1_000;
}
