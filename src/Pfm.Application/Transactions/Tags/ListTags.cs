using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Tags;

/// <summary>
/// A page of the user's tags by name, starting after <paramref name="After"/>: what the transaction form
/// suggests as a tag is typed, and the list filters by.
/// </summary>
/// <param name="Search">Text the name contains, in upper or lower case; null lists every tag.</param>
public sealed record ListTags(string? Search = null, int Limit = Paging.DefaultLimit, NameCursor? After = null);

/// <summary>A tag as the use cases return it.</summary>
public sealed record TagDetails(Guid Id, string Name)
{
    public static TagDetails From(Tag tag) => new(tag.Id, tag.Name);
}

public sealed class ListTagsHandler(ITagRepository tags) : IQueryHandler
{
    /// <summary>A user has dozens or hundreds of tags, not thousands, so the page is cut from all of them.</summary>
    public async Task<Page<TagDetails>> HandleAsync(ListTags query, CancellationToken cancellationToken = default)
    {
        var search = Tag.Normalised(query.Search);
        var found = await tags.ListAsync(cancellationToken);
        return found
            .Where(tag => search is null || tag.Name.Contains(search, StringComparison.Ordinal))
            .OrderByName(tag => tag.Name, tag => tag.Id)
            .PageByName(tag => tag.Name, tag => tag.Id, query.Limit, query.After, TagDetails.From);
    }
}
