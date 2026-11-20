namespace Pfm.Domain.Accounts;

/// <summary>
/// What a credit card account records beyond any account (spec ACC-9). Each is optional: a user fills in
/// what they know.
/// </summary>
/// <param name="CreditLimit">The most the card lets its holder owe, in the account's currency; never negative.</param>
/// <param name="StatementDay">The day of the month the statement closes, 1 to 31.</param>
/// <param name="PaymentDueDay">The day of the month the payment is due, 1 to 31.</param>
public readonly record struct CreditCardTerms(decimal? CreditLimit, int? StatementDay, int? PaymentDueDay)
{
    /// <summary>The last day a month can have. In a shorter month, a later day means its last day.</summary>
    public const int LastDay = 31;

    public static bool IsValidCreditLimit(decimal creditLimit, Currency currency) => creditLimit >= 0 && Amounts.IsValid(creditLimit, currency);

    /// <summary>A day of the month, or none.</summary>
    public static bool IsValidDay(int? day) => day is null or (>= 1 and <= LastDay);
}
