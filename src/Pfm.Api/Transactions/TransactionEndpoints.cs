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
    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder api)
    {
        var transactions = api.MapGroup("/transactions").WithTags("Transactions").ProducesProblem(StatusCodes.Status401Unauthorized);

        transactions.MapGet("/", List).WithName("ListTransactions")
            .WithDescription("The user's transactions, newest date first and, within a date, last recorded first. A deleted transaction is never listed.");
        transactions.MapGet("/{id:guid}", Get).WithName("GetTransaction")
            .WithDescription("One transaction. The ETag header carries its row version. A deleted transaction is not found.");
        transactions.MapPost("/", Create).WithName("CreateTransaction")
            .WithDescription("Records income or an expense on an account, in the account's currency; the account's balance follows at once. Send an Idempotency-Key the client made up for this transaction: a retry with the same key answers the transaction the first request recorded and records nothing.");
        transactions.MapPut("/{id:guid}", Edit).WithName("EditTransaction")
            .WithDescription("Changes a transaction; the balance of its account, and of the account it was moved from, follows at once. Send the row version last read as If-Match: a stale one answers 412, none answers 428.")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        transactions.MapDelete("/{id:guid}", Delete).WithName("DeleteTransaction")
            .WithDescription("Deletes the transaction: it leaves lists and its account's balance at once.");

        return api;
    }

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
    private static async Task<Results<Created<TransactionResponse>, ValidationProblem>> Create(
        TransactionRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CreateTransactionHandler handler, HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CreateTransactionCommand(
                request.Type, request.AccountId, request.Amount, request.Date, request.CategoryId, request.Payee, request.Note, idempotencyKey),
            cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return TypedResults.Created($"/api/v1/transactions/{result.Outcome.Id}", Versioned(http, TransactionResponse.From(result.Outcome)));
    }

    /// <param name="ifMatch">The transaction's row version as last read, in double quotes: its ETag.</param>
    private static async Task<Results<Ok<TransactionResponse>, NotFound, ValidationProblem, ProblemHttpResult>> Edit(
        Guid id, TransactionRequest request, [FromHeader(Name = "If-Match")] string? ifMatch, EditTransactionHandler handler, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (ifMatch is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status428PreconditionRequired, title: "Send the transaction's row version as If-Match.");
        }

        if (!RowVersions.TryRead(ifMatch, out var rowVersion))
        {
            return Stale();
        }

        var result = await handler.HandleAsync(
            new EditTransactionCommand(
                id, rowVersion, request.Type, request.AccountId, request.Amount, request.Date, request.CategoryId, request.Payee, request.Note),
            cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            null => TypedResults.NotFound(),
            { Edited: { } edited } => TypedResults.Ok(Versioned(http, TransactionResponse.From(edited))),
            _ => Stale(),
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

    private static ProblemHttpResult Stale() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status412PreconditionFailed, title: "The transaction has changed since it was read. Read it again, then edit it.");

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
/// <param name="CategoryId">One of the user's categories that is not archived: an income category for income, an expense category for an expense.</param>
/// <param name="Payee">Who was paid, or who paid; at most 200 characters.</param>
/// <param name="Note">At most 1000 characters.</param>
public sealed record TransactionRequest(
    string Type, Guid AccountId, string Amount, string Date, Guid CategoryId, string? Payee = null, string? Note = null);

/// <summary>A transaction.</summary>
/// <param name="Amount">A decimal string in <paramref name="Currency"/>, greater than zero; the type gives the sign.</param>
/// <param name="Currency">The currency of the transaction's account.</param>
/// <param name="RowVersion">Send it as <c>If-Match</c>, in double quotes, when editing the transaction.</param>
public sealed record TransactionResponse(
    Guid Id, TransactionType Type, Guid AccountId, string Amount, Currency Currency, DateOnly Date, Guid CategoryId, string? Payee, string? Note,
    string RowVersion)
{
    public static TransactionResponse From(TransactionDetails transaction) =>
        new(transaction.Id, transaction.Type, transaction.AccountId, AmountJson.Text(transaction.Amount), transaction.Currency, transaction.Date,
            transaction.CategoryId, transaction.Payee, transaction.Note, transaction.RowVersion.ToString(CultureInfo.InvariantCulture));
}

/// <summary>A page of transactions. <c>nextCursor</c> asks for the next page; it is null on the last.</summary>
public sealed record TransactionListResponse(IReadOnlyList<TransactionResponse> Items, string? NextCursor);
