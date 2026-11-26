using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Tags;

/// <summary>
/// The tags a transaction is given by name: the user's own, each created the first time it is used (spec
/// TXN-5). Shared by recording and editing.
/// </summary>
public sealed class TransactionTags(ITagRepository tags, TimeProvider clock)
{
    /// <summary>The user's tags with these names, the ones they have no tag with yet added to be saved with the transaction.</summary>
    /// <param name="names">Names as <see cref="Tag.Normalised"/> keeps them, each once.</param>
    public async Task<IReadOnlyList<Tag>> FindOrCreateAsync(IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        if (names.Count == 0)
        {
            return [];
        }

        var found = (await tags.FindByNamesAsync(names, cancellationToken)).ToList();
        foreach (var name in names.Where(name => !found.Any(tag => tag.Name == name)))
        {
            var tag = Tag.Create(name, clock.GetUtcNow());
            tags.Add(tag);
            found.Add(tag);
        }

        return found;
    }
}
