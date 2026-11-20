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

    /// <summary>
    /// The last 4 digits of the account or card number, as a label to tell accounts apart; or none. Never
    /// more than these 4: a full number is not held anywhere (spec ACC-8).
    /// </summary>
    public string? Last4 { get; private set; }

    /// <summary>The most a credit card lets its holder owe, in the account's currency. Null when not given, and for every other type.</summary>
    public decimal? CreditLimit { get; private set; }

    /// <summary>The day of the month a credit card's statement closes. Null when not given, and for every other type.</summary>
    public int? StatementDay { get; private set; }

    /// <summary>The day of the month a credit card's payment is due. Null when not given, and for every other type.</summary>
    public int? PaymentDueDay { get; private set; }

    /// <param name="openingAmount">
    /// The opening balance; for a credit card or loan, the amount owed, as a positive number.
    /// </param>
    /// <param name="last4">The last 4 digits of the account or card number, or none.</param>
    /// <param name="creditCard">A credit card's terms; none for every other type.</param>
    public static Account Create(
        string name, AccountType type, Guid? institutionId, Currency currency, decimal openingAmount, DateOnly openingDate, DateTimeOffset now,
        string? last4 = null, CreditCardTerms? creditCard = null)
    {
        var account = new Account(now);
        account.Set(name, type, institutionId, currency, openingAmount, openingDate, last4, creditCard, now);
        return account;
    }

    /// <param name="openingAmount">
    /// The opening balance; for a credit card or loan, the amount owed, as a positive number.
    /// </param>
    /// <param name="hasTransactions">Whether the account has transactions, which fix its currency.</param>
    /// <param name="last4">The last 4 digits of the account or card number; none removes the label.</param>
    /// <param name="creditCard">A credit card's terms; none removes them, and every other type has none.</param>
    public void Edit(
        string name, AccountType type, Guid? institutionId, Currency currency, decimal openingAmount, DateOnly openingDate,
        bool hasTransactions, DateTimeOffset now, string? last4 = null, CreditCardTerms? creditCard = null)
    {
        if (hasTransactions && currency != Currency)
        {
            throw new AccountRuleViolation(nameof(Currency), "An account's currency cannot change once it has transactions.");
        }

        Set(name, type, institutionId, currency, openingAmount, openingDate, last4, creditCard, now);
    }

    /// <summary>
    /// The account's balance, given what its transactions come to: income − expenses + refunds +
    /// transfers in − transfers out (spec section 3, rule 2).
    /// </summary>
    public decimal BalanceWith(decimal netTransactions) => OpeningBalance + netTransactions;

    /// <summary>
    /// What is left of a credit card's limit, given what its transactions come to: the credit limit minus
    /// the amount owed now, which is the balance below zero (spec ACC-9). Negative when the card is over its
    /// limit. Null for an account with no credit limit.
    /// </summary>
    public decimal? AvailableCreditWith(decimal netTransactions) => CreditLimit is { } limit ? limit + BalanceWith(netTransactions) : null;

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

    private void Set(
        string name, AccountType type, Guid? institutionId, Currency currency, decimal openingAmount, DateOnly openingDate, string? last4,
        CreditCardTerms? creditCard, DateTimeOffset now)
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

        if (last4 is not null && !Last4Digits.IsValid(last4))
        {
            throw new ArgumentException("The label is the last 4 digits of the number, never more of it.", nameof(last4));
        }

        if (creditCard is { } terms)
        {
            if (type != AccountType.CreditCard && terms != default)
            {
                throw new ArgumentException("Only a credit card has a credit limit, a statement day and a payment due day.", nameof(creditCard));
            }

            if (terms.CreditLimit is { } limit && !CreditCardTerms.IsValidCreditLimit(limit, currency))
            {
                throw new ArgumentException("A credit limit is an amount in the account's currency that is not negative.", nameof(creditCard));
            }

            if (!CreditCardTerms.IsValidDay(terms.StatementDay) || !CreditCardTerms.IsValidDay(terms.PaymentDueDay))
            {
                throw new ArgumentException($"A statement day or payment due day is a day of the month, 1 to {CreditCardTerms.LastDay}.", nameof(creditCard));
            }
        }

        Name = name.Trim();
        Type = type;
        InstitutionId = institutionId;
        Currency = currency;
        OpeningBalance = type.HoldsAmountOwed() ? -openingAmount : openingAmount;
        OpeningDate = openingDate;
        Last4 = last4;
        CreditLimit = creditCard?.CreditLimit;
        StatementDay = creditCard?.StatementDay;
        PaymentDueDay = creditCard?.PaymentDueDay;
        UpdatedAt = now;
    }
}
