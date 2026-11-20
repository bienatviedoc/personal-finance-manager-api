namespace Pfm.Domain.Accounts;

/// <summary>
/// A place a user's money sits. It has exactly one currency and optionally belongs to an institution.
/// Its balance is never stored: it is the opening balance plus what its transactions come to. Never
/// deleted: archiving hides it from pickers while it stays in history.
/// </summary>
public sealed class Account : IUserOwned
{
    /// <summary>For persistence, which sets every property itself.</summary>
    private Account()
    {
    }

    private Account(DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>The institution the account is held with, or none (cash, say).</summary>
    public Guid? InstitutionId { get; private set; }

    public string Name { get; private set; } = "";

    public AccountType Type { get; private set; }

    public Currency Currency { get; private set; }

    /// <summary>
    /// What the account held on <see cref="OpeningDate"/>, before any of its transactions. Negative for an
    /// amount owed.
    /// </summary>
    public decimal OpeningBalance { get; private set; }

    public DateOnly OpeningDate { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Changes every time the account is saved; an edit names the version it was made from.</summary>
    public uint RowVersion { get; private set; }

    /// <summary>
    /// The amount owed the account opened with, as the user entered it: positive. Null for a type that
    /// holds no amount owed.
    /// </summary>
    public decimal? AmountOwed => Type.HoldsAmountOwed() ? -OpeningBalance : null;

    /// <param name="openingAmount">
    /// The opening balance; for a credit card or loan, the amount owed, as a positive number.
    /// </param>
    public static Account Create(
        string name, AccountType type, Guid? institutionId, Currency currency, decimal openingAmount, DateOnly openingDate, DateTimeOffset now)
    {
        var account = new Account(now);
        account.Set(name, type, institutionId, currency, openingAmount, openingDate, now);
        return account;
    }

    /// <param name="openingAmount">
    /// The opening balance; for a credit card or loan, the amount owed, as a positive number.
    /// </param>
    /// <param name="hasTransactions">Whether the account has transactions, which fix its currency.</param>
    public void Edit(
        string name, AccountType type, Guid? institutionId, Currency currency, decimal openingAmount, DateOnly openingDate,
        bool hasTransactions, DateTimeOffset now)
    {
        if (hasTransactions && currency != Currency)
        {
            throw new AccountRuleViolation(nameof(Currency), "An account's currency cannot change once it has transactions.");
        }

        Set(name, type, institutionId, currency, openingAmount, openingDate, now);
    }

    /// <summary>
    /// The account's balance, given what its transactions come to: income − expenses + refunds +
    /// transfers in − transfers out (spec section 3, rule 2).
    /// </summary>
    public decimal BalanceWith(decimal netTransactions) => OpeningBalance + netTransactions;

    /// <summary>Hides the account from pickers, at any balance. Archiving it again changes nothing.</summary>
    public void Archive(DateTimeOffset now)
    {
        if (IsArchived)
        {
            return;
        }

        IsArchived = true;
        UpdatedAt = now;
    }

    private void Set(string name, AccountType type, Guid? institutionId, Currency currency, decimal openingAmount, DateOnly openingDate, DateTimeOffset now)
    {
        if (!Names.IsValid(name))
        {
            throw new ArgumentException($"An account's name is required and at most {Names.MaxLength} characters.", nameof(name));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Not an account type.");
        }

        if (!Enum.IsDefined(currency))
        {
            throw new ArgumentOutOfRangeException(nameof(currency), currency, "Not a supported currency.");
        }

        if (!Amounts.IsValid(openingAmount, currency))
        {
            throw new ArgumentException("The amount has more digits than its currency or the app can hold.", nameof(openingAmount));
        }

        if (type.HoldsAmountOwed() && openingAmount < 0)
        {
            throw new ArgumentException("An amount owed is entered as a positive number.", nameof(openingAmount));
        }

        Name = name.Trim();
        Type = type;
        InstitutionId = institutionId;
        Currency = currency;
        OpeningBalance = type.HoldsAmountOwed() ? -openingAmount : openingAmount;
        OpeningDate = openingDate;
        UpdatedAt = now;
    }
}
