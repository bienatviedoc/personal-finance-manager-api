namespace Pfm.Application.Contracts.Transactions;

/// <summary>
/// The user's categorisation rules at work (spec IMP-4), for other modules: Imports asks it which category each
/// statement row's description takes, for the preview to show.
/// </summary>
public interface ICategorisation
{
    /// <summary>
    /// For each text, in the same order, the category the user's highest-priority rule that matches it gives it, of the
    /// kind the transaction takes and not archived; null when no rule matches.
    /// </summary>
    Task<IReadOnlyList<Guid?>> CategoriseAsync(IReadOnlyList<TextToCategorise> texts, CancellationToken cancellationToken);
}

/// <summary>A payee or description to categorise.</summary>
/// <param name="IsIncome">Money came in, so it takes an income category; else an expense one.</param>
public sealed record TextToCategorise(string? Text, bool IsIncome);
