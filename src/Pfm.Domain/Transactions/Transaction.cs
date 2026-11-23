namespace Pfm.Domain.Transactions;

/// <summary>
/// One movement of money on one account on one calendar date: income or an expense. Its amount is
/// positive and its type gives the sign; its currency is its account's. Deleting it is soft: it leaves
/// lists and balances at once and stays recoverable. It has one line, with its category and whole amount, or
/// is split across several, which sum exactly to its amount (spec section 3, rule 5).
/// </summary>
/// <remarks>
/// Its amount is converted into the user's base currency once, at the exchange rate of its date, and never
/// revalued when rates move (spec section 3, rule 7; decision record 0002). Until a rate exists on or before
/// its date it is not converted.
/// </remarks>
public sealed class Transaction : IUserOwned
{
    public const int MaxPayeeLength = 200;

    public const int MaxNoteLength = 1000;

    /// <summary>A split has at least this many lines: with one, the transaction is not split.</summary>
    public const int MinSplitLines = 2;

    public const int MaxLines = 50;

    private readonly List<TransactionLine> _lines = [];

    /// <summary>For persistence, which sets every property itself.</summary>
    private Transaction()
    {
    }

    private Transaction(DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid AccountId { get; private set; }

    public TransactionType Type { get; private set; }

    /// <summary>Always greater than zero; <see cref="Type"/> gives the sign.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Copied from the account the transaction is on.</summary>
    public Currency Currency { get; private set; }

    /// <summary>A calendar date in the user's time zone, never in the future.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>
    /// The amount in the user's base currency, or that it is not converted yet. Null only until the
    /// transaction is first saved, which converts it or flags it not converted.
    /// </summary>
    public BaseCurrencyConversion? Conversion { get; private set; }

    /// <summary>The category of the first line: the transaction's only category unless it is split.</summary>
    public Guid CategoryId { get; private set; }

    /// <summary>One line, or several when the transaction is split, in the order they were entered.</summary>
    public IReadOnlyList<TransactionLine> Lines => [.. _lines.OrderBy(line => line.Position)];

    /// <summary>Whether the transaction is split across several lines, each with its own category.</summary>
    public bool IsSplit => _lines.Count > 1;

    /// <summary>Who was paid, or who paid; null when not entered.</summary>
    public string? Payee { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When the transaction was deleted; null while it counts.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Changes every time the transaction is saved; an edit names the version it was made from.</summary>
    public uint RowVersion { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>Whether the amount has been converted into the base currency. Once it has, it stays as converted.</summary>
    public bool IsConverted => Conversion is { IsConverted: true };

    /// <summary>
    /// What the transaction does to its account's balance: nothing once deleted. An account's balance is its
    /// opening balance plus this of each of its transactions: income − expenses (spec section 3, rule 2).
    /// </summary>
    public decimal SignedAmount => IsDeleted ? 0 : Type.Signed(Amount);

    public static bool IsValidPayee(string? payee) => payee is null || payee.Trim().Length <= MaxPayeeLength;

    public static bool IsValidNote(string? note) => note is null || note.Trim().Length <= MaxNoteLength;

    /// <summary>A payee or note as the transaction keeps it: trimmed, and null when nothing is left.</summary>
    public static string? Normalised(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// What is left of <paramref name="amount"/> after <paramref name="lineAmounts"/>: zero when the lines sum
    /// exactly to it, which a split must (spec section 3, rule 5), and below zero when they come to more.
    /// </summary>
    public static decimal RemainingToSplit(decimal amount, IEnumerable<decimal> lineAmounts) => amount - lineAmounts.Sum();

    /// <param name="currency">The currency of the account, which the transaction takes.</param>
    /// <param name="category">One of the user's categories that is not archived, of the kind the type takes.</param>
    /// <param name="today">Today's date in the user's time zone.</param>
    public static Transaction Record(
        TransactionType type, Guid accountId, Currency currency, decimal amount, DateOnly date, Category category, string? payee, string? note,
        DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(category);
        var transaction = new Transaction(now);
        transaction.Set(type, accountId, currency, amount, date, [new SplitLine(category, amount)], split: false, payee, note, today, now);
        return transaction;
    }

    /// <summary>Records a transaction split across several categories.</summary>
    /// <param name="currency">The currency of the account, which the transaction takes.</param>
    /// <param name="lines">
    /// At least <see cref="MinSplitLines"/>, each on one of the user's categories that is not archived, of the
    /// kind the type takes, and together exactly <paramref name="amount"/>.
    /// </param>
    /// <param name="today">Today's date in the user's time zone.</param>
    public static Transaction RecordSplit(
        TransactionType type, Guid accountId, Currency currency, decimal amount, DateOnly date, IReadOnlyList<SplitLine> lines, string? payee, string? note,
        DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var transaction = new Transaction(now);
        transaction.Set(type, accountId, currency, amount, date, lines, split: true, payee, note, today, now);
        return transaction;
    }

    /// <summary>
    /// Changes the transaction, to another account (whose currency it then takes) if need be. It may keep
    /// a category that has since been archived, but not move to one. A split transaction becomes one line
    /// again, on <paramref name="category"/>.
    /// </summary>
    /// <param name="currency">The currency of the account the transaction is now on.</param>
    /// <param name="today">Today's date in the user's time zone.</param>
    public void Edit(
        TransactionType type, Guid accountId, Currency currency, decimal amount, DateOnly date, Category category, string? payee, string? note,
        DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(category);
        GuardNotDeleted();
        Set(type, accountId, currency, amount, date, [new SplitLine(category, amount)], split: false, payee, note, today, now);
    }

    /// <summary>
    /// Changes the transaction as <see cref="Edit"/> does, and splits it across <paramref name="lines"/>, which
    /// replace the lines it had. A line may keep a category the transaction already has that has since been
    /// archived.
    /// </summary>
    /// <param name="currency">The currency of the account the transaction is now on.</param>
    /// <param name="today">Today's date in the user's time zone.</param>
    public void EditSplit(
        TransactionType type, Guid accountId, Currency currency, decimal amount, DateOnly date, IReadOnlyList<SplitLine> lines, string? payee, string? note,
        DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lines);
        GuardNotDeleted();
        Set(type, accountId, currency, amount, date, lines, split: true, payee, note, today, now);
    }

    private void GuardNotDeleted()
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted transaction cannot be edited.");
        }
    }

    /// <summary>Whether the amount is already converted into this base currency, and so is not converted again.</summary>
    public bool IsConvertedInto(Currency baseCurrency) => Conversion is { IsConverted: true } converted && converted.BaseCurrency == baseCurrency;

    /// <summary>
    /// Keeps the amount as converted into the user's base currency at the exchange rate of the transaction's
    /// date, or of the nearest earlier day that has one. It is converted once: only an edit of the amount, the
    /// date or the account, or another base currency, has it converted again.
    /// </summary>
    /// <param name="amount">The amount in <paramref name="baseCurrency"/>, rounded to its decimals.</param>
    /// <param name="exchangeRate">What one unit of the transaction's currency is worth in <paramref name="baseCurrency"/>.</param>
    /// <param name="exchangeRateDate">The day the rate is of, on or before the transaction's date.</param>
    /// <exception cref="InvalidOperationException">The amount is already converted into <paramref name="baseCurrency"/>.</exception>
    public void ConvertInto(Currency baseCurrency, decimal amount, decimal exchangeRate, DateOnly exchangeRateDate)
    {
        GuardNotConvertedInto(baseCurrency);
        if (exchangeRateDate > Date)
        {
            throw new ArgumentException("An amount is converted at the rate of its date or of an earlier day.", nameof(exchangeRateDate));
        }

        Conversion = BaseCurrencyConversion.Converted(baseCurrency, amount, exchangeRate, exchangeRateDate);
    }

    /// <summary>
    /// Flags the transaction as not converted: no exchange rate exists on or before its date, so it waits
    /// for one, in the base currency it will be converted into.
    /// </summary>
    /// <exception cref="InvalidOperationException">The amount is already converted into <paramref name="baseCurrency"/>.</exception>
    public void FlagNotConverted(Currency baseCurrency)
    {
        GuardNotConvertedInto(baseCurrency);
        Conversion = BaseCurrencyConversion.NotConverted(baseCurrency);
    }

    private void GuardNotConvertedInto(Currency baseCurrency)
    {
        if (IsConvertedInto(baseCurrency))
        {
            throw new InvalidOperationException("An amount is converted once and is not revalued.");
        }
    }

    /// <summary>What was converted is no longer what the transaction says, so it waits to be converted again.</summary>
    private void DropConversion() => Conversion = Conversion is { } conversion ? BaseCurrencyConversion.NotConverted(conversion.BaseCurrency) : null;

    /// <summary>Takes the transaction out of lists and balances. Deleting it again changes nothing.</summary>
    public void Delete(DateTimeOffset now)
    {
        if (IsDeleted)
        {
            return;
        }

        DeletedAt = now;
        UpdatedAt = now;
    }

    /// <param name="split">
    /// Whether <paramref name="lines"/> were entered as a split. When not, there is one, with the whole amount,
    /// and what is wrong with its category is wrong with the transaction's.
    /// </param>
    private void Set(
        TransactionType type, Guid accountId, Currency currency, decimal amount, DateOnly date, IReadOnlyList<SplitLine> lines, bool split, string? payee,
        string? note, DateOnly today, DateTimeOffset now)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Not a transaction type.");
        }

        if (!Enum.IsDefined(currency))
        {
            throw new ArgumentOutOfRangeException(nameof(currency), currency, "Not a supported currency.");
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("A transaction is on an account.", nameof(accountId));
        }

        if (!IsValidPayee(payee))
        {
            throw new ArgumentException($"A payee is at most {MaxPayeeLength} characters.", nameof(payee));
        }

        if (!IsValidNote(note))
        {
            throw new ArgumentException($"A note is at most {MaxNoteLength} characters.", nameof(note));
        }

        if (amount <= 0)
        {
            throw new TransactionRuleViolation(nameof(Amount), "An amount is greater than zero.");
        }

        if (!Amounts.IsValid(amount, currency))
        {
            throw new TransactionRuleViolation(nameof(Amount), $"An amount in {currency} has at most {currency.DecimalPlaces()} decimals and {Amounts.MaxIntegerDigits} digits before them.");
        }

        if (date > today)
        {
            throw new TransactionRuleViolation(nameof(Date), "A transaction's date cannot be in the future.");
        }

        GuardLines(type, currency, amount, lines, split);

        // What was converted is no longer what the transaction says: it is converted again, at its date's rate.
        if (amount != Amount || date != Date || accountId != AccountId)
        {
            DropConversion();
        }

        Type = type;
        AccountId = accountId;
        Currency = currency;
        Amount = amount;
        Date = date;
        CategoryId = lines[0].Category.Id;
        Payee = Normalised(payee);
        Note = Normalised(note);
        UpdatedAt = now;
        Replace(lines, now);
    }

    /// <summary>The rules of a transaction's lines, each broken one named by where it is: <c>Lines[1].Amount</c>.</summary>
    private void GuardLines(TransactionType type, Currency currency, decimal amount, IReadOnlyList<SplitLine> lines, bool split)
    {
        if (split && lines.Count is < MinSplitLines or > MaxLines)
        {
            throw new TransactionRuleViolation(nameof(Lines), $"A split has {MinSplitLines} to {MaxLines} lines.");
        }

        var categoriesItHas = _lines.Select(line => line.CategoryId).ToHashSet();
        for (var position = 0; position < lines.Count; position++)
        {
            var line = lines[position] ?? throw new ArgumentException("A line is never null.", nameof(lines));
            var category = line.Category ?? throw new ArgumentException("A line has a category.", nameof(lines));
            var at = split ? $"{nameof(Lines)}[{position}]." : string.Empty;
            if (!IsValidNote(line.Note))
            {
                throw new ArgumentException($"A note is at most {MaxNoteLength} characters.", nameof(lines));
            }

            if (line.Amount <= 0)
            {
                throw new TransactionRuleViolation(at + nameof(Amount), "An amount is greater than zero.");
            }

            if (!Amounts.IsValid(line.Amount, currency))
            {
                throw new TransactionRuleViolation(at + nameof(Amount), $"An amount in {currency} has at most {currency.DecimalPlaces()} decimals and {Amounts.MaxIntegerDigits} digits before them.");
            }

            if (category.IsArchived && !categoriesItHas.Contains(category.Id))
            {
                throw new TransactionRuleViolation(at + nameof(CategoryId), "An archived category takes no new transactions.");
            }

            if (category.Kind != type.CategoryKind())
            {
                throw new TransactionRuleViolation(
                    at + nameof(CategoryId),
                    type == TransactionType.Income ? "An income transaction takes an income category." : "An expense takes an expense category.");
            }
        }

        var remaining = RemainingToSplit(amount, lines.Select(line => line.Amount));
        if (remaining != 0)
        {
            throw new TransactionRuleViolation(
                nameof(Lines),
                remaining > 0
                    ? $"The lines of a split sum to the transaction's amount: {remaining} is left to split."
                    : $"The lines of a split sum to the transaction's amount: they come to {-remaining} more.");
        }
    }

    /// <summary>
    /// The line at each position says what was entered there: the ones the transaction has are changed where
    /// they stand, more are added after them and the ones past the last are removed.
    /// </summary>
    private void Replace(IReadOnlyList<SplitLine> lines, DateTimeOffset now)
    {
        var has = Lines;
        for (var position = 0; position < lines.Count; position++)
        {
            if (position < has.Count)
            {
                has[position].Become(lines[position]);
            }
            else
            {
                _lines.Add(new TransactionLine(position, lines[position], now));
            }
        }

        _lines.RemoveAll(line => line.Position >= lines.Count);
    }
}
