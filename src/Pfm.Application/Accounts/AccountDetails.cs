using Pfm.Domain;
using Pfm.Domain.Accounts;

namespace Pfm.Application.Accounts;

/// <summary>An account as the use cases return it, with its balance as of now.</summary>
/// <param name="OpeningBalance">Negative for an amount owed.</param>
/// <param name="AmountOwed">The amount owed the account opened with, positive; null unless it is a credit card or loan.</param>
/// <param name="Balance">The opening balance plus what the account's transactions come to.</param>
/// <param name="RowVersion">The version an edit must name.</param>
/// <param name="Last4">The last 4 digits of the account or card number, or none.</param>
/// <param name="CreditLimit">A credit card's limit; null when it has none, and for every other type.</param>
/// <param name="AvailableCredit">The credit limit minus the amount owed now; null for an account with no credit limit.</param>
public sealed record AccountDetails(
    Guid Id, Guid? InstitutionId, string Name, AccountType Type, Currency Currency, decimal OpeningBalance, decimal? AmountOwed,
    DateOnly OpeningDate, decimal Balance, bool IsArchived, uint RowVersion, string? Last4, decimal? CreditLimit, int? StatementDay,
    int? PaymentDueDay, decimal? AvailableCredit)
{
    /// <param name="netTransactions">What the account's transactions come to.</param>
    public static AccountDetails Of(Account account, decimal netTransactions) =>
        new(account.Id, account.InstitutionId, account.Name, account.Type, account.Currency, account.OpeningBalance, account.AmountOwed,
            account.OpeningDate, account.BalanceWith(netTransactions), account.IsArchived, account.RowVersion, account.Last4?.Digits,
            account.CreditCard.CreditLimit, account.CreditCard.StatementDay, account.CreditCard.PaymentDueDay, account.AvailableCreditWith(netTransactions));
}
