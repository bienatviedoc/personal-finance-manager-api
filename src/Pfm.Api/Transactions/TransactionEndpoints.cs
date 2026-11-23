using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pfm.Application.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Api.Transactions;

/// <summary>Transactions over HTTP. The use cases decide everything; these only map requests and answers.</summary>
public static class TransactionEndpoints
{
    /// <summary>What these endpoints edit, as a problem about its row version names it.</summary>
    private const string What = "transaction";

    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder api)
    {
        var transactions = api.MapGroup("/transactions").WithTags("Transactions").ProducesProblem(StatusCodes.Status401Unauthorized);

        transactions.MapGet("/", List).WithName("ListTransactions")
            .WithDescription("The user's transactions, newest date first and, within a date, last recorded first. A deleted transaction is never listed.");
        transactions.MapGet("/quick-add", QuickAdd).WithName("GetQuickAdd")
            .WithDescription("What quick add starts a new transaction from: the account, not archived, that the user recorded a transaction on last, and the category, not archived, they most often gave the payee. Send the payee as typed; it suggests a category once it is, in any case, a payee the user has used before.");
        transactions.MapGet("/{id:guid}", Get).WithName("GetTransaction")
            .WithDescription("One transaction. The ETag header carries its row version. A deleted transaction is not found.");
        transactions.MapPost("/", Create).WithName("CreateTransaction")
            .WithDescription("Records income or an expense on an account, in the account's currency; the account's balance follows at once. An expense flagged isRefund is a refund and raises the balance. Send lines in place of categoryId to split it across categories: lines that do not sum exactly to the amount are refused. Send an Idempotency-Key the client made up for this transaction: a retry with the same key answers the transaction the first request recorded and records nothing, and a different transaction sent with a key already used answers 422.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        transactions.MapPut("/{id:guid}", Edit).WithName("EditTransaction")
            .WithDescription("Changes a transaction; the balance of its account, and of the account it was moved from, follows at once. Send the row version last read as If-Match: a stale one answers 412, none answers 428.")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        transactions.MapDelete("/{id:guid}", Delete).WithName("DeleteTransaction")
            .WithDescription("Deletes the transaction: it leaves lists and its account's balance at once.");

        return api;
    }

    /// <summary>
    /// Problem type returned when an <c>Idempotency-Key</c> that already recorded a transaction is sent with a
    /// different one: nothing is recorded. Send a new transaction under a new key.
    /// </summary>
    public const string IdempotencyKeyReused = "urn:pfm:problem:idempotency-key-reused";

    /// <param name="limit">How many transactions a page holds: 1 to 100, 50 when left out.</param>
    /// <param name="cursor">The <c>nextCursor</c> of the page before; left out for the first page.</param>
    private static async Task<Results<Ok<TransactionListResponse>, ValidationProblem>> List(
        ListTransactionsHandler handler, CancellationToken cancellationToken, int limit = Paging.DefaultLimit, string? cursor = null)
    {
        if (!Paging.IsValidLimit(limit))
        {
            return PagingProblems.For("limit", $"Ask for 1 to {Paging.MaxLimit} at a time.");
        }

        TransactionCursor? after = null;
        if (cursor is not null && !TransactionCursor.TryParse(cursor, out after))
        {
            return PagingProblems.For("cursor", "Use the nextCursor of the page before.");
        }

        var page = await handler.HandleAsync(new ListTransactions(limit, after), cancellationToken);
        return TypedResults.Ok(new TransactionListResponse([.. page.Items.Select(TransactionResponse.From)], page.NextCursor));
    }

    /// <param name="type"><c>income</c> or <c>expense</c>: only a category of the kind it takes is suggested. Left out, a category of either kind is.</param>
    /// <param name="payee">Who is being paid, or is paying; at most 200 characters. Left out, no category is suggested.</param>
    private static async Task<Results<Ok<QuickAddResponse>, ValidationProblem>> QuickAdd(
        GetQuickAddHandler handler, CancellationToken cancellationToken, string? type = null, string? payee = null)
    {
        var read = EnumText<TransactionType>.Read(type);
        if (type is not null && read is null)
        {
            return PagingProblems.For("type", $"Choose one of: {string.Join(", ", EnumText<TransactionType>.Names)}.");
        }

        if (!Transaction.IsValidPayee(payee))
        {
            return PagingProblems.For("payee", $"Enter a payee of at most {Transaction.MaxPayeeLength} characters.");
        }

        var quickAdd = await handler.HandleAsync(new GetQuickAdd(read, payee), cancellationToken);
        return TypedResults.Ok(new QuickAddResponse(quickAdd.LastAccountId, quickAdd.SuggestedCategoryId));
    }

    private static async Task<Results<Ok<TransactionResponse>, NotFound>> Get(
        Guid id, GetTransactionHandler handler, HttpContext http, CancellationToken cancellationToken)
    {
        if (await handler.HandleAsync(new GetTransaction(id), cancellationToken) is not { } transaction)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(Versioned(http, TransactionResponse.From(transaction)));
    }

    /// <param name="idempotencyKey">A key the client made up for this transaction, at most 200 characters. A retry sends the same one.</param>
    private static async Task<Results<Created<TransactionResponse>, ValidationProblem, ProblemHttpResult>> Create(
        TransactionRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CreateTransactionHandler handler, HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CreateTransactionCommand(
                request.Type, request.AccountId, request.Amount, request.Date, request.CategoryId ?? Guid.Empty, request.Payee, request.Note, idempotencyKey,
                request.IsRefund, request.LineInputs()),
            cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        if (result.Outcome.Recorded is not { } recorded)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity, type: IdempotencyKeyReused,
                title: "This Idempotency-Key already recorded a different transaction. Send a new transaction with a new key.");
        }

        return TypedResults.Created($"/api/v1/transactions/{recorded.Id}", Versioned(http, TransactionResponse.From(recorded)));
    }

    /// <param name="ifMatch">The transaction's row version as last read, in double quotes: its ETag.</param>
    private static async Task<Results<Ok<TransactionResponse>, NotFound, ValidationProblem, ProblemHttpResult>> Edit(
        Guid id, TransactionRequest request, [FromHeader(Name = "If-Match")] string? ifMatch, EditTransactionHandler handler, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!RowVersions.TryRead(ifMatch, What, out var rowVersion, out var refused))
        {
            return refused;
        }

        var result = await handler.HandleAsync(
            new EditTransactionCommand(
                id, rowVersion, request.Type, request.AccountId, request.Amount, request.Date, request.CategoryId ?? Guid.Empty, request.Payee, request.Note,
                request.IsRefund, request.LineInputs()),
            cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            null => TypedResults.NotFound(),
            { Edited: { } edited } => TypedResults.Ok(Versioned(http, TransactionResponse.From(edited))),
            _ => RowVersions.Stale(What),
        };
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> Delete(
        Guid id, DeleteTransactionHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DeleteTransactionCommand(id), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is null ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static TransactionResponse Versioned(HttpContext http, TransactionResponse transaction)
    {
        http.SendAsETag(transaction.RowVersion);
        return transaction;
    }
}

/// <summary>What recording or editing a transaction sends. Its currency is not sent: it is the account's.</summary>
/// <param name="Type"><c>income</c> or <c>expense</c>.</param>
/// <param name="AccountId">One of the user's accounts that is not archived.</param>
/// <param name="Amount">A decimal string greater than zero, such as <c>"125000.00"</c>, with no more decimals than the account's currency has.</param>
/// <param name="Date">A calendar date, <c>YYYY-MM-DD</c>, not after today in the user's time zone.</param>
/// <param name="CategoryId">One of the user's categories that is not archived: an income category for income, an expense category for an expense. Left out of a split, whose lines name its categories.</param>
/// <param name="Payee">Who was paid, or who paid; at most 200 characters.</param>
/// <param name="Note">At most 1000 characters.</param>
/// <param name="IsRefund">Whether the expense is a refund: money coming back from a purchase, which raises the account's balance and lowers spent in its category. Only an expense can be one; false when left out.</param>
/// <param name="Lines">Sent to split the transaction across categories: 2 to 50 lines whose amounts sum exactly to <paramref name="Amount"/>. Left out, the transaction is on <paramref name="CategoryId"/> alone; an edit without them makes a split one category again.</param>
public sealed record TransactionRequest(
    string Type, Guid AccountId, string Amount, string Date, Guid? CategoryId = null, string? Payee = null, string? Note = null, bool IsRefund = false,
    IReadOnlyList<TransactionLineRequest>? Lines = null)
{
    /// <summary>The lines as a command carries them; a line sent as null stays one, for the validator to refuse.</summary>
    internal IReadOnlyList<TransactionLineInput>? LineInputs() =>
        Lines is null ? null : [.. Lines.Select(line => line is null ? null! : new TransactionLineInput(line.CategoryId, line.Amount, line.Note))];
}

/// <summary>A line of a split: the part of the transaction's amount that was for one category.</summary>
/// <param name="CategoryId">One of the user's categories that is not archived, of the kind the transaction's type takes.</param>
/// <param name="Amount">A decimal string greater than zero, with no more decimals than the account's currency has.</param>
/// <param name="Note">What this part was for; at most 1000 characters.</param>
public sealed record TransactionLineRequest(Guid CategoryId, string Amount, string? Note = null);

/// <summary>A transaction.</summary>
/// <param name="Amount">A decimal string in <paramref name="Currency"/>, greater than zero; the type gives the sign, except that a refund raises the balance.</param>
/// <param name="IsRefund">Whether the expense is a refund: money coming back, which raises the account's balance and lowers spent in its category.</param>
/// <param name="Currency">The currency of the transaction's account.</param>
/// <param name="CategoryId">The category of the first line: the transaction's only category unless it is split.</param>
/// <param name="RowVersion">Send it as <c>If-Match</c>, in double quotes, when editing the transaction.</param>
/// <param name="Lines">One line with the whole amount, or, for a split transaction, its lines in the order they were entered; their amounts sum exactly to <paramref name="Amount"/>.</param>
/// <param name="RecurringRuleId">The recurring rule that posted the transaction; null for one the user recorded, and once that rule is deleted.</param>
public sealed record TransactionResponse(
    Guid Id, TransactionType Type, Guid AccountId, string Amount, Currency Currency, DateOnly Date, Guid CategoryId, string? Payee, string? Note,
    string RowVersion, bool IsRefund, IReadOnlyList<TransactionLineResponse> Lines, Guid? RecurringRuleId)
{
    public static TransactionResponse From(TransactionDetails transaction) =>
        new(transaction.Id, transaction.Type, transaction.AccountId, AmountJson.Text(transaction.Amount), transaction.Currency, transaction.Date,
            transaction.CategoryId, transaction.Payee, transaction.Note, transaction.RowVersion.ToString(CultureInfo.InvariantCulture), transaction.IsRefund,
            [.. transaction.Lines.Select(line => new TransactionLineResponse(line.CategoryId, AmountJson.Text(line.Amount), line.Note))], transaction.RecurringRuleId);
}

/// <summary>What quick add starts a new transaction from.</summary>
/// <param name="LastAccountId">The account, not archived, that the user recorded a transaction on last; null when they have recorded none.</param>
/// <param name="SuggestedCategoryId">The category, not archived, the user most often gave the payee, and of those given equally often the one given last; null when they never used the payee.</param>
public sealed record QuickAddResponse(Guid? LastAccountId, Guid? SuggestedCategoryId);

/// <summary>A line of a transaction.</summary>
/// <param name="Amount">A decimal string in the transaction's currency, greater than zero.</param>
public sealed record TransactionLineResponse(Guid CategoryId, string Amount, string? Note);

/// <summary>A page of transactions. <c>nextCursor</c> asks for the next page; it is null on the last.</summary>
public sealed record TransactionListResponse(IReadOnlyList<TransactionResponse> Items, string? NextCursor);
