namespace Pfm.Domain.Accounts;

/// <summary>What kind of place an account is (spec ACC-3).</summary>
public enum AccountType
{
    Current,
    Savings,
    CreditCard,
    Cash,
    EWallet,
    Loan,
}

public static class AccountTypes
{
    /// <summary>
    /// Whether an account of this type holds an amount owed: a credit card or a loan, whose balance is
    /// negative while the user owes anything (spec section 3, rule 9).
    /// </summary>
    public static bool HoldsAmountOwed(this AccountType type) => type is AccountType.CreditCard or AccountType.Loan;
}
