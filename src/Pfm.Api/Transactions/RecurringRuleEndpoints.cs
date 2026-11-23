using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Transactions.Recurring;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Api.Transactions;

/// <summary>Recurring rules over HTTP. The use cases decide everything; these only map requests and answers.</summary>
public static class RecurringRuleEndpoints
{
    public static IEndpointRouteBuilder MapRecurringRuleEndpoints(this IEndpointRouteBuilder api)
    {
        var rules = api.MapGroup("/recurring-rules").WithTags("Recurring rules").ProducesProblem(StatusCodes.Status401Unauthorized);

        rules.MapGet("/", List).WithName("ListRecurringRules")
            .WithDescription("The user's recurring rules, the last set up first.");
        rules.MapGet("/{id:guid}", Get).WithName("GetRecurringRule");
        rules.MapPost("/", Create).WithName("CreateRecurringRule")
            .WithDescription("Sets up income or an expense that repeats. The worker posts each occurrence as a transaction on its due date, in the user's time zone. A start date in the past is allowed: the occurrences since then are posted, each on its own due date.");
        rules.MapPut("/{id:guid}", Edit).WithName("EditRecurringRule")
            .WithDescription("Replaces what the rule's next transactions say and when they are due. Transactions it already posted stay as they are, and a new schedule carries on after the last occurrence posted or skipped.");
        rules.MapPost("/{id:guid}/pause", Pause).WithName("PauseRecurringRule")
            .WithDescription("Pauses the rule: it posts nothing until it is resumed.");
        rules.MapPost("/{id:guid}/resume", Resume).WithName("ResumeRecurringRule")
            .WithDescription("Resumes a paused rule: it posts again from today. The occurrences that were due while it was paused are skipped.");
        rules.MapDelete("/{id:guid}", Delete).WithName("DeleteRecurringRule")
            .WithDescription("Deletes the rule: it posts nothing more. The transactions it posted stay, no longer linked to a rule.");

        return api;
    }

    /// <param name="limit">How many rules a page holds: 1 to 100, 50 when left out.</param>
    /// <param name="cursor">The <c>nextCursor</c> of the page before; left out for the first page.</param>
    private static async Task<Results<Ok<RecurringRuleListResponse>, ValidationProblem>> List(
        ListRecurringRulesHandler handler, CancellationToken cancellationToken, int limit = Paging.DefaultLimit, string? cursor = null)
    {
        if (!Paging.IsValidLimit(limit))
        {
            return PagingProblems.For("limit", $"Ask for 1 to {Paging.MaxLimit} at a time.");
        }

        RecurringRuleCursor? after = null;
        if (cursor is not null && !RecurringRuleCursor.TryParse(cursor, out after))
        {
            return PagingProblems.For("cursor", "Use the nextCursor of the page before.");
        }

        var page = await handler.HandleAsync(new ListRecurringRules(limit, after), cancellationToken);
        return TypedResults.Ok(new RecurringRuleListResponse([.. page.Items.Select(RecurringRuleResponse.From)], page.NextCursor));
    }

    private static async Task<Results<Ok<RecurringRuleResponse>, NotFound>> Get(Guid id, GetRecurringRuleHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new GetRecurringRule(id), cancellationToken) is { } rule
            ? TypedResults.Ok(RecurringRuleResponse.From(rule))
            : TypedResults.NotFound();

    private static async Task<Results<Created<RecurringRuleResponse>, ValidationProblem>> Create(
        RecurringRuleRequest request, CreateRecurringRuleHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CreateRecurringRuleCommand(
                request.Type, request.AccountId, request.Amount, request.CategoryId, request.Payee, request.Note, request.Frequency, request.Interval,
                request.StartDate, request.EndDate),
            cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return TypedResults.Created($"/api/v1/recurring-rules/{result.Outcome.Id}", RecurringRuleResponse.From(result.Outcome));
    }

    private static async Task<Results<Ok<RecurringRuleResponse>, NotFound, ValidationProblem>> Edit(
        Guid id, RecurringRuleRequest request, EditRecurringRuleHandler handler, CancellationToken cancellationToken) =>
        Changed(await handler.HandleAsync(
            new EditRecurringRuleCommand(
                id, request.Type, request.AccountId, request.Amount, request.CategoryId, request.Payee, request.Note, request.Frequency,
                request.Interval, request.StartDate, request.EndDate),
            cancellationToken));

    private static async Task<Results<Ok<RecurringRuleResponse>, NotFound, ValidationProblem>> Pause(
        Guid id, PauseRecurringRuleHandler handler, CancellationToken cancellationToken) =>
        Changed(await handler.HandleAsync(new PauseRecurringRuleCommand(id), cancellationToken));

    private static async Task<Results<Ok<RecurringRuleResponse>, NotFound, ValidationProblem>> Resume(
        Guid id, ResumeRecurringRuleHandler handler, CancellationToken cancellationToken) =>
        Changed(await handler.HandleAsync(new ResumeRecurringRuleCommand(id), cancellationToken));

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> Delete(
        Guid id, DeleteRecurringRuleHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DeleteRecurringRuleCommand(id), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is null ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    /// <summary>The rule as a change left it, 404 when the user has no such rule, or the fields to fix.</summary>
    private static Results<Ok<RecurringRuleResponse>, NotFound, ValidationProblem> Changed(Result<RecurringRuleDetails?> result)
    {
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is { } rule ? TypedResults.Ok(RecurringRuleResponse.From(rule)) : TypedResults.NotFound();
    }
}

/// <summary>What setting up or editing a recurring rule sends. Its currency is not sent: it is the account's.</summary>
/// <param name="Type"><c>income</c> or <c>expense</c>. A recurring rule is never a transfer.</param>
/// <param name="AccountId">One of the user's accounts that is not archived.</param>
/// <param name="Amount">A decimal string greater than zero, such as <c>"125000.00"</c>, with no more decimals than the account's currency has.</param>
/// <param name="CategoryId">One of the user's categories that is not archived: an income category for income, an expense category for an expense.</param>
/// <param name="Frequency"><c>weekly</c>, <c>monthly</c>, <c>yearly</c> or <c>everyNDays</c>. A monthly rule started on the 31st is due on the last day of a shorter month.</param>
/// <param name="StartDate">The date the first occurrence is due, <c>YYYY-MM-DD</c>.</param>
/// <param name="Payee">Who is paid, or who pays; at most 200 characters.</param>
/// <param name="Note">At most 1000 characters.</param>
/// <param name="Interval">How many days apart, 1 to 365, for <c>everyNDays</c>; left out for any other frequency.</param>
/// <param name="EndDate">The last date an occurrence can be due, <c>YYYY-MM-DD</c>; left out for a rule that goes on.</param>
public sealed record RecurringRuleRequest(
    string Type, Guid AccountId, string Amount, Guid CategoryId, string Frequency, string StartDate, string? Payee = null, string? Note = null,
    int? Interval = null, string? EndDate = null);

/// <summary>A recurring rule.</summary>
/// <param name="Amount">A decimal string in <paramref name="Currency"/>, greater than zero; the type gives the sign.</param>
/// <param name="Currency">The currency of the rule's account.</param>
/// <param name="Interval">How many days apart an <c>everyNDays</c> rule is due; null for any other frequency.</param>
/// <param name="NextRunDate">The due date of the next occurrence to post; null once the rule has ended. A paused rule keeps the date it stopped at.</param>
public sealed record RecurringRuleResponse(
    Guid Id, TransactionType Type, Guid AccountId, string Amount, Currency Currency, Guid CategoryId, string? Payee, string? Note,
    RecurringFrequency Frequency, int? Interval, DateOnly StartDate, DateOnly? EndDate, DateOnly? NextRunDate, bool IsPaused)
{
    public static RecurringRuleResponse From(RecurringRuleDetails rule) =>
        new(rule.Id, rule.Type, rule.AccountId, AmountJson.Text(rule.Amount), rule.Currency, rule.CategoryId, rule.Payee, rule.Note, rule.Frequency,
            rule.Interval, rule.StartDate, rule.EndDate, rule.NextRunDate, rule.IsPaused);
}

/// <summary>A page of recurring rules. <c>nextCursor</c> asks for the next page; it is null on the last.</summary>
public sealed record RecurringRuleListResponse(IReadOnlyList<RecurringRuleResponse> Items, string? NextCursor);
