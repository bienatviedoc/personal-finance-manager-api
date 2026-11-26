using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Transactions.Tags;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Api.Transactions;

/// <summary>Tags over HTTP. A tag is created the first time a transaction is given it, so there is only the list.</summary>
public static class TagEndpoints
{
    public static IEndpointRouteBuilder MapTagEndpoints(this IEndpointRouteBuilder api)
    {
        var tags = api.MapGroup("/tags").WithTags("Tags").ProducesProblem(StatusCodes.Status401Unauthorized);

        tags.MapGet("/", List).WithName("ListTags")
            .WithDescription("The user's tags by name, a page at a time: what the transaction form suggests as a tag is typed. A tag is created the first time a transaction is given it.");

        return api;
    }

    /// <param name="q">Text the tag's name contains, in upper or lower case; left out, every tag is listed.</param>
    /// <param name="limit">How many tags a page holds: 1 to 100, 50 when left out.</param>
    /// <param name="cursor">The <c>nextCursor</c> of the page before, asked for with the same q; left out for the first page.</param>
    private static async Task<Results<Ok<TagListResponse>, ValidationProblem>> List(
        ListTagsHandler handler, CancellationToken cancellationToken, string? q = null, int limit = Paging.DefaultLimit, string? cursor = null)
    {
        if (!Paging.IsValidLimit(limit))
        {
            return PagingProblems.For("limit", $"Ask for 1 to {Paging.MaxLimit} at a time.");
        }

        NameCursor? after = null;
        if (cursor is not null && !NameCursor.TryParse(cursor, out after))
        {
            return PagingProblems.For("cursor", "Use the nextCursor of the page before.");
        }

        if (q is not null && q.Trim().Length > Tag.MaxNameLength)
        {
            return PagingProblems.For("q", $"Search for at most {Tag.MaxNameLength} characters.");
        }

        var page = await handler.HandleAsync(new ListTags(q, limit, after), cancellationToken);
        return TypedResults.Ok(new TagListResponse([.. page.Items.Select(TagResponse.From)], page.NextCursor));
    }
}

/// <summary>A tag: a free-form label on transactions, across categories.</summary>
/// <param name="Name">Trimmed and in lower case, such as <c>japan-trip</c>; what a transaction is given it by, and the list filtered by.</param>
public sealed record TagResponse(Guid Id, string Name)
{
    public static TagResponse From(TagDetails tag) => new(tag.Id, tag.Name);
}

/// <summary>A page of tags. <c>nextCursor</c> asks for the next page; it is null on the last.</summary>
public sealed record TagListResponse(IReadOnlyList<TagResponse> Items, string? NextCursor);
