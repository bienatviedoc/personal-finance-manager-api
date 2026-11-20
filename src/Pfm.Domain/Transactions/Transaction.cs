namespace Pfm.Domain.Transactions;

/// <summary>
/// One movement of money on one account on one calendar date: income or an expense. Its amount is
/// positive and its type gives the sign; its currency is its account's. Deleting it is soft: it leaves
/// lists and balances at once and stays recoverable.
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
    /// The base currency the amount is converted into, or is waiting to be while it is not converted. Null
    /// only until the transaction is first saved.
    /// </summary>
    public Currency? BaseCurrency { get; private set; }

    /// <summary>The amount in <see cref="BaseCurrency"/>, like <see cref="Amount"/> without a sign; null while not converted.</summary>
    public decimal? BaseCurrencyAmount { get; private set; }

    /// <summary>What one unit of <see cref="Currency"/> was worth in <see cref="BaseCurrency"/> when the amount was converted; null while not converted.</summary>
    public decimal? ExchangeRate { get; private set; }

    /// <summary>The day <see cref="ExchangeRate"/> is of: the transaction's date, or the nearest earlier day that had a rate.</summary>
    public DateOnly? ExchangeRateDate { get; private set; }

    public Guid CategoryId { get; private set; }

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
    public bool IsConverted => BaseCurrencyAmount is not null;

    /// <summary>What the transaction does to its account's balance: nothing once deleted.</summary>
    public decimal SignedAmount => IsDeleted ? 0 : Type.Signed(Amount);

    /// <summary>
    /// What transactions come to on their account: income − expenses (spec section 3, rule 2), leaving out
    /// the deleted ones. An account's balance is its opening balance plus this.
    /// </summary>
    public static decimal NetOf(IEnumerable<Transaction> transactions) => transactions.Sum(transaction => transaction.SignedAmount);

    public static bool IsValidPayee(string? payee) => payee is null || payee.Trim().Length <= MaxPayeeLength;

    public static bool IsValidNote(string? note) => note is null || note.Trim().Length <= MaxNoteLength;

    /// <summary>A payee or note as the transaction keeps it: trimmed, and null when nothing is left.</summary>
    public static string? Normalised(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <param name="currency">The currency of the account, which the transaction takes.</param>
    /// <param name="category">One of the user's categories that is not archived, of the kind the type takes.</param>
    /// <param name="today">Today's date in the user's time zone.</param>
    public static Transaction Record(
        TransactionType type, Guid accountId, Currency currency, decimal amount, DateOnly date, Category category, string? payee, string? note,
        DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(category);
        if (category.IsArchived)
        {
            throw new TransactionRuleViolation(nameof(CategoryId), "An archived category takes no new transactions.");
        }

        var transaction = new Transaction(now);
        transaction.Set(type, accountId, currency, amount, date, category, payee, note, today, now);
        return transaction;
    }

    /// <summary>
    /// Changes the transaction, to another account (whose currency it then takes) if need be. It may keep
    /// a category that has since been archived, but not move to one.
    /// </summary>
    /// <param name="currency">The currency of the account the transaction is now on.</param>
    /// <param name="today">Today's date in the user's time zone.</param>
    public void Edit(
        TransactionType type, Guid accountId, Currency currency, decimal amount, DateOnly date, Category category, string? payee, string? note,
        DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(category);
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted transaction cannot be edited.");
        }

        if (category.IsArchived && category.Id != CategoryId)
        {
            throw new TransactionRuleViolation(nameof(CategoryId), "An archived category takes no new transactions.");
        }

        Set(type, accountId, currency, amount, date, category, payee, note, today, now);
    }

    /// <summary>Whether the amount is already converted into this base currency, and so is not converted again.</summary>
    public bool IsConvertedInto(Currency baseCurrency) => IsConverted && BaseCurrency == baseCurrency;

    /// <summary>Whether an amount fits the base currency: what a converted amount must do to be kept.</summary>
    public static bool CanHoldInBaseCurrency(decimal amount, Currency baseCurrency) => amount >= 0 && Amounts.IsValid(amount, baseCurrency);

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
        if (!CanHoldInBaseCurrency(amount, baseCurrency))
        {
            throw new ArgumentException($"A converted amount is not negative and fits {baseCurrency}.", nameof(amount));
        }

        if (exchangeRate <= 0)
        {
            throw new ArgumentException("An exchange rate is greater than zero.", nameof(exchangeRate));
        }

        if (exchangeRateDate > Date)
        {
            throw new ArgumentException("An amount is converted at the rate of its date or of an earlier day.", nameof(exchangeRateDate));
        }

        BaseCurrency = baseCurrency;
        BaseCurrencyAmount = amount;
        ExchangeRate = exchangeRate;
        ExchangeRateDate = exchangeRateDate;
    }

    /// <summary>
    /// Flags the transaction as not converted: no exchange rate exists on or before its date, so it waits
    /// for one, in the base currency it will be converted into.
    /// </summary>
    /// <exception cref="InvalidOperationException">The amount is already converted into <paramref name="baseCurrency"/>.</exception>
    public void FlagNotConverted(Currency baseCurrency)
    {
        GuardNotConvertedInto(baseCurrency);
        BaseCurrency = baseCurrency;
        DropConversion();
    }

    private void GuardNotConvertedInto(Currency baseCurrency)
    {
        if (!Enum.IsDefined(baseCurrency))
        {
            throw new ArgumentOutOfRangeException(nameof(baseCurrency), baseCurrency, "Not a supported currency.");
        }

        if (IsConvertedInto(baseCurrency))
        {
            throw new InvalidOperationException("An amount is converted once and is not revalued.");
        }
    }

    private void DropConversion()
    {
        BaseCurrencyAmount = null;
        ExchangeRate = null;
        ExchangeRateDate = null;
    }

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

    private void Set(
        TransactionType type, Guid accountId, Currency currency, decimal amount, DateOnly date, Category category, string? payee, string? note,
        DateOnly today, DateTimeOffset now)
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

        if (category.Kind != type.CategoryKind())
        {
            throw new TransactionRuleViolation(
                nameof(CategoryId),
                type == TransactionType.Income ? "An income transaction takes an income category." : "An expense takes an expense category.");
        }

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
        CategoryId = category.Id;
        Payee = Normalised(payee);
        Note = Normalised(note);
        UpdatedAt = now;
    }
}
