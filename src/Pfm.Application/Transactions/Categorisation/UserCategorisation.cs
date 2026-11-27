using Pfm.Application.Contracts.Transactions;
using Pfm.Application.Transactions.Categories;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categorisation;

/// <summary>The user's categorisation rules at work: what quick add and the import preview ask which category a payee or description takes.</summary>
public sealed class UserCategorisation(ICategorisationRuleRepository rules, ICategoryRepository categories) : ICategorisation
{
    /// <summary>The user's rules, with their categories, ready to categorise.</summary>
    public async Task<Categoriser> CategoriserAsync(CancellationToken cancellationToken)
    {
        var all = await rules.ListAsync(cancellationToken);
        return new Categoriser(all, all.Count == 0 ? [] : await categories.ListAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<Guid?>> CategoriseAsync(IReadOnlyList<TextToCategorise> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var categoriser = await CategoriserAsync(cancellationToken);
        return [.. texts.Select(text => categoriser.CategoryFor(text.Text, text.IsIncome ? CategoryKind.Income : CategoryKind.Expense))];
    }
}
