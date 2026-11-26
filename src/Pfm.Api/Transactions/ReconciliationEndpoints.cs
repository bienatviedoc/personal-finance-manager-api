using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Transactions.Reconciliations;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Api.Transactions;

/// <summary>Reconciliations over HTTP (spec ACC-7). The use cases decide everything; these only map requests and answers.</summary>
public static class ReconciliationEndpoints
{
    /// <summary>The account has a reconciliation in progress already: complete or cancel it before starting another.</summary>
    public const string ReconciliationInProgress = "urn:pfm:problem:reconciliation-in-progress";

    /// <summary>The reconciliation is completed: reopen it to change what is ticked, or to cancel it.</summary>
    public const string ReconciliationCompleted = "urn:pfm:problem:reconciliation-completed";

    /// <summary>The difference is not exactly zero, so the reconciliation does not complete; nothing is added to make it so.</summary>
    public const string ReconciliationDifferenceNotZero = "urn:pfm:problem:reconciliation-difference-not-zero";

    /// <summary>Only an account's latest reconciliation can be reopened: each later one is measured from what the ones before it reconciled.</summary>
    public const string ReconciliationNotLatest = "urn:pfm:problem:reconciliation-not-latest";

    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder api)
    {
        var ofAccount = api.MapGroup("/accounts/{accountId:guid}/reconciliations").WithTags("Reconciliations").ProducesProblem(StatusCodes.Status401Unauthorized);
        ofAccount.MapPost("/", Start).WithName("StartReconciliation")
            .WithDescription("Starts reconciling the account against a statement: its closing date, not in the future, and closing balance, in the account's currency and below zero for what is owed. The difference starts as the statement balance less the account's reconciled balance: its opening balance and everything reconciled before. An account has one reconciliation in progress at a time: another answers 409 with the problem type urn:pfm:problem:reconciliation-in-progress. A statement dated before the account's last reconciliation is refused on statementDate.")
            .ProducesProblem(StatusCodes.Status409Conflict);
        ofAccount.MapGet("/", List).WithName("ListReconciliations")
            .WithDescription("The account's reconciliations, the last started first, a page at a time, each with its difference.");

        var reconciliations = api.MapGroup("/reconciliations").WithTags("Reconciliations").ProducesProblem(StatusCodes.Status401Unauthorized);
        reconciliations.MapGet("/{id:guid}", Get).WithName("GetReconciliation")
            .WithDescription("One reconciliation, with the account's reconciled balance, what is ticked in it and the difference still to reach zero.");
        reconciliations.MapGet("/{id:guid}/transactions", ListTransactions).WithName("ListReconciliationTransactions")
            .WithDescription("The transactions to tick against the statement, oldest first: those ticked in this reconciliation and, while it is in progress, every other transaction on its account dated on or before the statement date that no reconciliation holds yet. Not paged.");
        reconciliations.MapPut("/{id:guid}/transactions/{transactionId:guid}", Tick).WithName("TickTransaction")
            .WithDescription("Ticks a transaction the statement shows, and answers the reconciliation with its new difference. Only a transaction on the account, dated on or before the statement date and in no other reconciliation can be ticked; any other is refused on transactionId. A completed reconciliation answers 409 with the problem type urn:pfm:problem:reconciliation-completed.")
            .ProducesProblem(StatusCodes.Status409Conflict);
        reconciliations.MapDelete("/{id:guid}/transactions/{transactionId:guid}", Untick).WithName("UntickTransaction")
            .WithDescription("Unticks a transaction, and answers the reconciliation with its new difference. A completed reconciliation answers 409 with the problem type urn:pfm:problem:reconciliation-completed.")
            .ProducesProblem(StatusCodes.Status409Conflict);
        reconciliations.MapPost("/{id:guid}/complete", Complete).WithName("CompleteReconciliation")
            .WithDescription("Completes the reconciliation, which locks every transaction ticked in it: their amount, date and account cannot change, and they cannot be deleted, until it is reopened. It completes only when the difference is exactly zero; otherwise it answers 409 with the problem type urn:pfm:problem:reconciliation-difference-not-zero, and no adjustment transaction is ever added. Completing one already completed changes nothing.")
            .ProducesProblem(StatusCodes.Status409Conflict);
        reconciliations.MapPost("/{id:guid}/reopen", Reopen).WithName("ReopenReconciliation")
            .WithDescription("Reopens a completed reconciliation: its transactions are unlocked and stay ticked, to be changed and completed again. Only the account's latest reconciliation can be reopened; an earlier one answers 409 with the problem type urn:pfm:problem:reconciliation-not-latest. Reopening one in progress changes nothing.")
            .ProducesProblem(StatusCodes.Status409Conflict);
        reconciliations.MapDelete("/{id:guid}", Cancel).WithName("CancelReconciliation")
            .WithDescription("Cancels a reconciliation in progress, entered against the wrong statement for instance: what is ticked in it is unticked and it is removed. A completed one answers 409 with the problem type urn:pfm:problem:reconciliation-completed: reopen it first.")
            .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }

    private static async Task<Results<Created<ReconciliationResponse>, NotFound, ValidationProblem, ProblemHttpResult>> Start(
        Guid accountId, StartReconciliationRequest request, StartReconciliationHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new StartReconciliationCommand(accountId, request.StatementDate, request.StatementBalance), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            null => TypedResults.NotFound(),
            { Started: { } started } => TypedResults.Created($"/api/v1/reconciliations/{started.Id}", ReconciliationResponse.From(started)),
            _ => Problem(ReconciliationInProgress, "This account has a reconciliation in progress.", "Complete or cancel it before starting another."),
        };
    }

    /// <param name="limit">How many reconciliations a page holds: 1 to 100, 50 when left out.</param>
    /// <param name="cursor">The <c>nextCursor</c> of the page before; left out for the first page.</param>
    private static async Task<Results<Ok<ReconciliationListResponse>, NotFound, ValidationProblem>> List(
        Guid accountId, ListReconciliationsHandler handler, CancellationToken cancellationToken, int limit = Paging.DefaultLimit, string? cursor = null)
    {
        if (!Paging.IsValidLimit(limit))
        {
            return PagingProblems.For("limit", $"Ask for 1 to {Paging.MaxLimit} at a time.");
        }

        ReconciliationCursor? after = null;
        if (cursor is not null && !ReconciliationCursor.TryParse(cursor, out after))
        {
            return PagingProblems.For("cursor", "Use the nextCursor of the page before.");
        }

        return await handler.HandleAsync(new ListReconciliations(accountId, limit, after), cancellationToken) is { } page
            ? TypedResults.Ok(new ReconciliationListResponse([.. page.Items.Select(ReconciliationResponse.From)], page.NextCursor))
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<ReconciliationResponse>, NotFound>> Get(Guid id, GetReconciliationHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new GetReconciliation(id), cancellationToken) is { } reconciliation
            ? TypedResults.Ok(ReconciliationResponse.From(reconciliation))
            : TypedResults.NotFound();

    private static async Task<Results<Ok<ReconciliationTransactionListResponse>, NotFound>> ListTransactions(
        Guid id, ListReconciliationTransactionsHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new ListReconciliationTransactions(id), cancellationToken) is { } transactions
            ? TypedResults.Ok(new ReconciliationTransactionListResponse([.. transactions.Select(ReconciliationTransactionResponse.From)]))
            : TypedResults.NotFound();

    private static async Task<Results<Ok<ReconciliationResponse>, NotFound, ValidationProblem, ProblemHttpResult>> Tick(
        Guid id, Guid transactionId, TickTransactionHandler handler, CancellationToken cancellationToken) =>
        Ticked(await handler.HandleAsync(new TickTransactionCommand(id, transactionId), cancellationToken));

    private static async Task<Results<Ok<ReconciliationResponse>, NotFound, ValidationProblem, ProblemHttpResult>> Untick(
        Guid id, Guid transactionId, UntickTransactionHandler handler, CancellationToken cancellationToken) =>
        Ticked(await handler.HandleAsync(new UntickTransactionCommand(id, transactionId), cancellationToken));

    private static Results<Ok<ReconciliationResponse>, NotFound, ValidationProblem, ProblemHttpResult> Ticked(Result<TickOutcome?> result)
    {
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            null => TypedResults.NotFound(),
            { Changed: { } changed } => TypedResults.Ok(ReconciliationResponse.From(changed)),
            _ => Completed(),
        };
    }

    private static async Task<Results<Ok<ReconciliationResponse>, NotFound, ValidationProblem, ProblemHttpResult>> Complete(
        Guid id, CompleteReconciliationHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CompleteReconciliationCommand(id), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            null => TypedResults.NotFound(),
            CompleteReconciliationOutcome.Completed { Reconciliation: var completed } => TypedResults.Ok(ReconciliationResponse.From(completed)),
            CompleteReconciliationOutcome.DifferenceNotZero { Difference: var difference } => Problem(
                ReconciliationDifferenceNotZero, "The difference is not zero.",
                $"The difference is {AmountJson.Text(difference)}. Tick or untick transactions until it is exactly zero; nothing is added to make it so."),
            _ => throw new ArgumentOutOfRangeException(nameof(id), result.Outcome, "Not an outcome of completing a reconciliation."),
        };
    }

    private static async Task<Results<Ok<ReconciliationResponse>, NotFound, ValidationProblem, ProblemHttpResult>> Reopen(
        Guid id, ReopenReconciliationHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ReopenReconciliationCommand(id), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            null => TypedResults.NotFound(),
            { Reopened: { } reopened } => TypedResults.Ok(ReconciliationResponse.From(reopened)),
            _ => Problem(
                ReconciliationNotLatest, "Only the account's latest reconciliation can be reopened.",
                "Each later reconciliation is measured from what this one reconciled. Reopen the latest one first."),
        };
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem, ProblemHttpResult>> Cancel(
        Guid id, CancelReconciliationHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CancelReconciliationCommand(id), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            null => TypedResults.NotFound(),
            CancelReconciliationOutcome.Cancelled => TypedResults.NoContent(),
            _ => Completed(),
        };
    }

    private static ProblemHttpResult Completed() =>
        Problem(ReconciliationCompleted, "This reconciliation is completed.", "Reopen it to change what is ticked in it, or to cancel it.");

    private static ProblemHttpResult Problem(string type, string title, string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, type: type, title: title, detail: detail);
}

/// <summary>What starting a reconciliation sends: the statement it is matched against.</summary>
/// <param name="StatementDate">The statement's closing date, <c>YYYY-MM-DD</c>, not after today in the user's time zone.</param>
/// <param name="StatementBalance">The statement's closing balance, a decimal string in the account's currency, such as <c>"4500000.00"</c>; below zero for what is owed on a credit card or loan.</param>
public sealed record StartReconciliationRequest(string? StatementDate, string? StatementBalance);

/// <summary>A reconciliation, with what is ticked in it worked out.</summary>
/// <param name="StatementBalance">A decimal string in <paramref name="Currency"/>, the account's.</param>
/// <param name="Status"><c>inProgress</c> while transactions are ticked, <c>completed</c> once it locks them.</param>
/// <param name="CompletedAt">When it was completed; null while it is in progress.</param>
/// <param name="ReconciledBalance">The account's opening balance plus every transaction reconciled in its other reconciliations, a decimal string.</param>
/// <param name="TickedTotal">What the transactions ticked in this one do to the balance, a decimal string.</param>
/// <param name="Difference">The statement balance less the reconciled balance and the ticked total, a decimal string: it completes only at exactly zero.</param>
/// <param name="TickedCount">How many transactions are ticked in it.</param>
public sealed record ReconciliationResponse(
    Guid Id, Guid AccountId, DateOnly StatementDate, string StatementBalance, Currency Currency, ReconciliationStatus Status, DateTimeOffset? CompletedAt,
    string ReconciledBalance, string TickedTotal, string Difference, int TickedCount)
{
    public static ReconciliationResponse From(ReconciliationDetails reconciliation) =>
        new(reconciliation.Id, reconciliation.AccountId, reconciliation.StatementDate, AmountJson.Text(reconciliation.StatementBalance), reconciliation.Currency,
            reconciliation.Status, reconciliation.CompletedAt, AmountJson.Text(reconciliation.ReconciledBalance), AmountJson.Text(reconciliation.TickedTotal),
            AmountJson.Text(reconciliation.Difference), reconciliation.TickedCount);
}

/// <summary>A page of an account's reconciliations. <c>nextCursor</c> asks for the next page; it is null on the last.</summary>
public sealed record ReconciliationListResponse(IReadOnlyList<ReconciliationResponse> Items, string? NextCursor);

/// <summary>A transaction as a reconciliation lists it to tick.</summary>
/// <param name="Amount">A decimal string greater than zero; the type gives the sign, except that a refund raises the balance.</param>
/// <param name="IsTicked">Whether it is ticked in this reconciliation.</param>
public sealed record ReconciliationTransactionResponse(
    Guid Id, TransactionType Type, DateOnly Date, string Amount, Currency Currency, bool IsRefund, string? Payee, string? Note, Guid? CategoryId,
    Guid? TransferId, bool IsTicked)
{
    public static ReconciliationTransactionResponse From(ReconciliationTransactionDetails transaction) =>
        new(transaction.Id, transaction.Type, transaction.Date, AmountJson.Text(transaction.Amount), transaction.Currency, transaction.IsRefund,
            transaction.Payee, transaction.Note, transaction.CategoryId, transaction.TransferId, transaction.IsTicked);
}

/// <summary>The transactions to tick against a reconciliation's statement, oldest first.</summary>
public sealed record ReconciliationTransactionListResponse(IReadOnlyList<ReconciliationTransactionResponse> Items);
