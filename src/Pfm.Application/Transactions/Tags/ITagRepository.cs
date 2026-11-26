using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Tags;

/// <summary>The signed-in user's tags.</summary>
public interface ITagRepository
{
    /// <summary>Every tag of the user's; a user has dozens or hundreds, not more.</summary>
    Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The user's tags with these names, as <see cref="Tag.Normalised"/> keeps them; a name they have no tag with finds none.</summary>
    Task<IReadOnlyList<Tag>> FindByNamesAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a new tag, saved with the use case's changes. When another request adds one with the same name
    /// first, the save throws <see cref="Abstractions.ConcurrentChangeException"/> and the use case is made again,
    /// finding it.
    /// </summary>
    void Add(Tag tag);
}
