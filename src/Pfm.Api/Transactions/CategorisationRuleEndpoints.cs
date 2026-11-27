using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pfm.Application.Transactions.Categorisation;
using Pfm.Domain.Transactions;

namespace Pfm.Api.Transactions;

/// <summary>Categorisation rules over HTTP (spec IMP-4). The use cases decide everything; these only map requests and answers.</summary>
public static class CategorisationRuleEndpoints
{
    /// <summary>What these endpoints edit, as a problem about its row version names it.</summary>
    private const string What = "categorisation rule";

    public static IEndpointRouteBuilder MapCategorisationRuleEndpoints(this IEndpointRouteBuilder api)
    {
        var rules = api.MapGroup("/category-rules").WithTags("Categorisation rules").ProducesProblem(StatusCodes.Status401Unauthorized);

        rules.MapGet("/", List).WithName("ListCategorisationRules")
            .WithDescription("Every one of the user's categorisation rules, unpaged (a user has a few dozen), in the order they are tried: highest priority first, and of equal ones the one set up first. A rule whose category has been archived stays listed, and categorises nothing until it is moved to another.");
        rules.MapPost("/", Create).WithName("CreateCategorisationRule")
            .WithDescription("Sets up a rule: a payee, or a statement row's description, that contains, starts with or is exactly the match text (compared without case, accents or extra spaces) takes the category. It categorises only transactions of its category's kind. Of several rules that match, the one of highest priority wins. Left out, the priority is one above the user's highest.");
        rules.MapPut("/order", Reorder).WithName("ReorderCategorisationRules")
            .WithDescription("Puts the rules in a new order: rules lists every one of the user's rules once, the one to try first first, each with the row version it was read at, and each takes its priority from its place (the last 0, the one before it 1, and so on). A list that names a rule twice is refused on rules; one that is not the user's rules as they are now, because a rule was set up, edited or deleted since they were read, answers 412. Either changes nothing. Answers the rules in their new order.");
        rules.MapPut("/{id:guid}", Edit).WithName("EditCategorisationRule")
            .WithDescription("Changes a rule's match text, match type, category and priority (left out, it keeps its own). It may keep a category that has been archived since, but not move to one. Send the row version last read as If-Match: a stale one answers 412, none answers 428. Reordering the rules and merging a rule's category change the row version too.");
        rules.MapDelete("/{id:guid}", Delete).WithName("DeleteCategorisationRule")
            .WithDescription("Deletes the rule: it categorises nothing more. What it categorised keeps its category.");

        return api;
    }

    private static async Task<Ok<CategorisationRuleListResponse>> List(ListCategorisationRulesHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(CategorisationRuleListResponse.From(await handler.HandleAsync(new ListCategorisationRules(), cancellationToken)));

    private static async Task<Results<Created<CategorisationRuleResponse>, ValidationProblem>> Create(
        CategorisationRuleRequest request, CreateCategorisationRuleHandler handler, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CreateCategorisationRuleCommand(request.MatchText, request.MatchType, request.CategoryId, request.Priority), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return TypedResults.Created($"/api/v1/category-rules/{result.Outcome.Id}", Versioned(http, CategorisationRuleResponse.From(result.Outcome)));
    }

    /// <param name="ifMatch">The rule's row version as last read, in double quotes: its ETag.</param>
    private static async Task<Results<Ok<CategorisationRuleResponse>, NotFound, ValidationProblem, ProblemHttpResult>> Edit(
        Guid id, CategorisationRuleRequest request, [FromHeader(Name = "If-Match")] string? ifMatch, EditCategorisationRuleHandler handler,
        HttpContext http, CancellationToken cancellationToken)
    {
        if (!RowVersions.TryRead(ifMatch, What, out var rowVersion, out var refused))
        {
            return refused;
        }

        var result = await handler.HandleAsync(
            new EditCategorisationRuleCommand(id, rowVersion, request.MatchText, request.MatchType, request.CategoryId, request.Priority), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            null => TypedResults.NotFound(),
            { Edited: { } edited } => TypedResults.Ok(Versioned(http, CategorisationRuleResponse.From(edited))),
            _ => RowVersions.Stale(What),
        };
    }

    private static async Task<Results<Ok<CategorisationRuleListResponse>, ValidationProblem, ProblemHttpResult>> Reorder(
        CategorisationRuleOrderRequest request, ReorderCategorisationRulesHandler handler, CancellationToken cancellationToken)
    {
        IReadOnlyList<CategorisationRuleVersion>? rules = null;
        if (request.Rules is { } sent)
        {
            var read = new List<CategorisationRuleVersion>();
            foreach (var rule in sent)
            {
                // A row version the API never gave out matches no rule, as a stale one does.
                if (!RowVersions.TryParse(rule.RowVersion, out var rowVersion))
                {
                    return StaleOrder();
                }

                read.Add(new CategorisationRuleVersion(rule.Id, rowVersion));
            }

            rules = read;
        }

        var result = await handler.HandleAsync(new ReorderCategorisationRulesCommand(rules), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome.Rules is { } reordered ? TypedResults.Ok(CategorisationRuleListResponse.From(reordered)) : StaleOrder();
    }

    private static ProblemHttpResult StaleOrder() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status412PreconditionFailed,
            title: "The rules have changed since they were read. Read them again, then reorder them.");

    private static CategorisationRuleResponse Versioned(HttpContext http, CategorisationRuleResponse rule)
    {
        http.SendAsETag(rule.RowVersion);
        return rule;
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> Delete(
        Guid id, DeleteCategorisationRuleHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DeleteCategorisationRuleCommand(id), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is null ? TypedResults.NotFound() : TypedResults.NoContent();
    }
}

/// <summary>What setting up or editing a categorisation rule sends.</summary>
/// <param name="MatchText">What to look for in a payee or description: at most 100 characters, compared without case, accents or extra spaces.</param>
/// <param name="MatchType"><c>contains</c>, <c>startsWith</c> or <c>exactly</c>.</param>
/// <param name="CategoryId">One of the user's categories that is not archived; an edited rule may keep one archived since.</param>
/// <param name="Priority">From 0 to 1,000,000: of several rules that match, the highest wins. Left out, one above the user's highest for a new rule, and its own for an edited one.</param>
public sealed record CategorisationRuleRequest(string MatchText, string? MatchType, Guid CategoryId, int? Priority = null);

/// <summary>A new order for the rules.</summary>
/// <param name="Rules">Every one of the user's rules once, the one to try first first, each with the row version it was read at.</param>
public sealed record CategorisationRuleOrderRequest(IReadOnlyList<CategorisationRuleOrderEntry>? Rules);

/// <summary>One rule in a new order.</summary>
/// <param name="RowVersion">The rule's row version as it was read, without quotes.</param>
public sealed record CategorisationRuleOrderEntry(Guid Id, string RowVersion);

/// <summary>A categorisation rule.</summary>
/// <param name="MatchType"><c>contains</c>, <c>startsWith</c> or <c>exactly</c>.</param>
/// <param name="Priority">Of several rules that match, the highest wins; of equal ones, the one set up first.</param>
/// <param name="RowVersion">Send it as <c>If-Match</c>, in double quotes, when editing the rule, and with its id when reordering the rules.</param>
public sealed record CategorisationRuleResponse(Guid Id, string MatchText, CategorisationMatch MatchType, Guid CategoryId, int Priority, string RowVersion)
{
    public static CategorisationRuleResponse From(CategorisationRuleDetails rule) =>
        new(rule.Id, rule.MatchText, rule.MatchType, rule.CategoryId, rule.Priority, rule.RowVersion.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Every one of the user's categorisation rules, in the order they are tried.</summary>
public sealed record CategorisationRuleListResponse(IReadOnlyList<CategorisationRuleResponse> Items)
{
    public static CategorisationRuleListResponse From(IEnumerable<CategorisationRuleDetails> rules) => new([.. rules.Select(CategorisationRuleResponse.From)]);
}
