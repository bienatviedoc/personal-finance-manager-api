namespace Pfm.Domain.Transactions;

/// <summary>What suggested the category quick add starts a payee's transaction on.</summary>
public enum CategorySuggestion
{
    /// <summary>The user's earlier transactions with the payee.</summary>
    History = 1,

    /// <summary>A categorisation rule the payee matches, the payee having no history.</summary>
    Rule = 2,
}
