using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Identity;
using Pfm.Application.UseCases;
using Pfm.Domain.Identity;

namespace Pfm.Api.Identity;

/// <summary>
/// The signed-in user's audit log over HTTP (spec section 8, Auditability). Read only: entries are append only, so
/// nothing here changes or removes one.
/// </summary>
public static class AuditLogEndpoints
{
    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/me/audit-log", List).WithName("ListAuditEntries").WithTags("Identity")
            .WithDescription("The signed-in user's audit log, the newest entry first, a page at a time: each sign-in, statement committed or import undone, bulk edit, bulk delete, delete of a transaction, a transfer, an attachment, a budget, a recurring rule or a categorisation rule, and the user asking to be deleted, with what it affected, when, and a short summary in the user's locale when it was written. Entries cannot be changed or deleted.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        return api;
    }

    /// <param name="limit">How many entries a page holds: 1 to 100, 50 when left out.</param>
    /// <param name="cursor">The <c>nextCursor</c> of the page before; left out for the first page.</param>
    private static async Task<Results<Ok<AuditLogResponse>, ValidationProblem>> List(
        ListAuditEntriesHandler handler, CancellationToken cancellationToken, int limit = Paging.DefaultLimit, string? cursor = null)
    {
        if (!Paging.IsValidLimit(limit))
        {
            return PagingProblems.For("limit", $"Ask for 1 to {Paging.MaxLimit} at a time.");
        }

        AuditEntryCursor? after = null;
        if (cursor is not null && !AuditEntryCursor.TryParse(cursor, out after))
        {
            return PagingProblems.For("cursor", "Use the nextCursor of the page before.");
        }

        var page = await handler.HandleAsync(new ListAuditEntries(limit, after), cancellationToken);
        return TypedResults.Ok(new AuditLogResponse([.. page.Items.Select(AuditEntryResponse.From)], page.NextCursor));
    }
}

/// <summary>A page of the user's audit log, the newest entry first.</summary>
/// <param name="NextCursor">Pass it as <c>cursor</c> for the next page; null on the last.</param>
public sealed record AuditLogResponse(IReadOnlyList<AuditEntryResponse> Items, string? NextCursor);

/// <summary>Something sensitive the user did.</summary>
/// <param name="Action">What they did.</param>
/// <param name="EntityType">The kind of thing it affected.</param>
/// <param name="EntityId">The thing it affected; null when it affected several, as a bulk edit does.</param>
/// <param name="At">When.</param>
/// <param name="Summary">What happened, in a sentence, in the user's locale when it was written.</param>
public sealed record AuditEntryResponse(Guid Id, AuditAction Action, AuditEntityType EntityType, Guid? EntityId, DateTimeOffset At, string Summary)
{
    public static AuditEntryResponse From(AuditEntryDetails entry) => new(entry.Id, entry.Action, entry.EntityType, entry.EntityId, entry.At, entry.Summary);
}
