using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Transactions.Categorisation;
using Pfm.Domain.Transactions;

namespace Pfm.Api.Transactions;

/// <summary>Categorisation rules over HTTP (spec IMP-4). The use cases decide everything; these only map requests and answers.</summary>
public static class CategorisationRuleEndpoints
{
    public static IEndpointRouteBuilder MapCategorisationRuleEndpoints(this IEndpointRouteBuilder api)
    {
        var rules = api.MapGroup("/category-rules").WithTags("Categorisation rules").ProducesProblem(StatusCodes.Status401Unauthorized);

        rules.MapGet("/", List).WithName("ListCategorisationRules")
            .WithDescription("Every one of the user's categorisation rules, unpaged (a user has a few dozen), in the order they are tried: highest priority first, and of equal ones the one set up first. A rule whose category has been archived stays listed, and categorises nothing until it is moved to another.");
        rules.MapPost("/", Create).WithName("CreateCategorisationRule")
            .WithDescription("Sets up a rule: a payee, or a statement row's description, that contains, starts with or is exactly the match text (compared without case, accents or extra spaces) takes the category. It categorises only transactions of its category's kind. Of several rules that match, the one of highest priority wins. Left out, the priority is one above the user's highest.");
        rules.MapPut("/order", Reorder).WithName("ReorderCategorisationRules")
            .WithDescription("Puts the rules in a new order: ids lists every one of the user's rules once, the one to try first first, and each takes its priority from its place (the last 0, the one before it 1, and so on). Any other list is refused on ids and changes nothing. Answers the rules in their new order.");
        rules.MapPut("/{id:guid}", Edit).WithName("EditCategorisationRule")
            .WithDescription("Changes a rule's match text, match type, category and priority (left out, it keeps its own). It may keep a category that has been archived since, but not move to one.");
        rules.MapDelete("/{id:guid}", Delete).WithName("DeleteCategorisationRule")
            .WithDescription("Deletes the rule: it categorises nothing more. What it categorised keeps its category.");

        return api;
    }

    private static async Task<Ok<CategorisationRuleListResponse>> List(ListCategorisationRulesHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(CategorisationRuleListResponse.From(await handler.HandleAsync(new ListCategorisationRules(), cancellationToken)));

    private static async Task<Results<Created<CategorisationRuleResponse>, ValidationProblem>> Create(
        CategorisationRuleRequest request, CreateCategorisationRuleHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CreateCategorisationRuleCommand(request.MatchText, request.MatchType, request.CategoryId, request.Priority), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return TypedResults.Created($"/api/v1/category-rules/{result.Outcome.Id}", CategorisationRuleResponse.From(result.Outcome));
    }

    private static async Task<Results<Ok<CategorisationRuleResponse>, NotFound, ValidationProblem>> Edit(
        Guid id, CategorisationRuleRequest request, EditCategorisationRuleHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new EditCategorisationRuleCommand(id, request.MatchText, request.MatchType, request.CategoryId, request.Priority), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is { } edited ? TypedResults.Ok(CategorisationRuleResponse.From(edited)) : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<CategorisationRuleListResponse>, ValidationProblem>> Reorder(
        CategorisationRuleOrderRequest request, ReorderCategorisationRulesHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ReorderCategorisationRulesCommand(request.Ids), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return TypedResults.Ok(CategorisationRuleListResponse.From(result.Outcome.Rules));
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
/// <param name="Ids">Every one of the user's rules once, the one to try first first.</param>
public sealed record CategorisationRuleOrderRequest(IReadOnlyList<Guid>? Ids);

/// <summary>A categorisation rule.</summary>
/// <param name="MatchType"><c>contains</c>, <c>startsWith</c> or <c>exactly</c>.</param>
/// <param name="Priority">Of several rules that match, the highest wins; of equal ones, the one set up first.</param>
public sealed record CategorisationRuleResponse(Guid Id, string MatchText, CategorisationMatch MatchType, Guid CategoryId, int Priority)
{
    public static CategorisationRuleResponse From(CategorisationRuleDetails rule) =>
        new(rule.Id, rule.MatchText, rule.MatchType, rule.CategoryId, rule.Priority);
}

/// <summary>Every one of the user's categorisation rules, in the order they are tried.</summary>
public sealed record CategorisationRuleListResponse(IReadOnlyList<CategorisationRuleResponse> Items)
{
    public static CategorisationRuleListResponse From(IEnumerable<CategorisationRuleDetails> rules) => new([.. rules.Select(CategorisationRuleResponse.From)]);
}
