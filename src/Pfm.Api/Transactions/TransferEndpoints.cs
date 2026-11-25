using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pfm.Application.Transactions.Transfers;
using Pfm.Domain;

namespace Pfm.Api.Transactions;

/// <summary>Transfers over HTTP. The use cases decide everything; these only map requests and answers.</summary>
public static class TransferEndpoints
{
    /// <summary>What these endpoints edit, as a problem about its row version names it.</summary>
    private const string What = "transfer";

    /// <summary>A retry of a transfer that was recorded and unmarked since: its key stays used, and nothing is recorded.</summary>
    public const string TransferUnmarked = "urn:pfm:problem:transfer-unmarked";

    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder api)
    {
        var transfers = api.MapGroup("/transfers").WithTags("Transfers").ProducesProblem(StatusCodes.Status401Unauthorized);

        transfers.MapPost("/", Create).WithName("CreateTransfer")
            .WithDescription("Moves money between two of the user's accounts. It records two linked transactions, a transferOut leg on the source account and a transferIn leg on the destination, and both balances follow at once; a transfer is neither income nor spending. Between accounts in different currencies send both amounts: no exchange rate is looked up. Send an Idempotency-Key the client made up for this transfer: a retry with the same key answers the transfer the first request recorded and records nothing, and anything else sent with a key already used answers 422. A retry after the transfer was unmarked answers 409 with the problem type urn:pfm:problem:transfer-unmarked and records nothing: a new transfer needs a new key.")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        transfers.MapGet("/{id:guid}", Get).WithName("GetTransfer")
            .WithDescription("One transfer, by the transferId its two legs carry in the transaction list. The ETag header carries its row version. A deleted transfer is not found.");
        transfers.MapPut("/{id:guid}", Edit).WithName("EditTransfer")
            .WithDescription("Changes a transfer, both legs together; the balances of the accounts it was and is now between follow at once. Send the row version last read as If-Match: a stale one answers 412, none answers 428.")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        transfers.MapDelete("/{id:guid}", Delete).WithName("DeleteTransfer")
            .WithDescription("Deletes the transfer, both legs together: they leave the transaction list and both accounts' balances at once.");
        transfers.MapPost("/mark", Mark).WithName("MarkAsTransfer")
            .WithDescription("Marks an expense on one account and income on another as one transfer, such as the two rows imported statements show for paying a card: the expense becomes its transferOut leg and the income its transferIn leg, each keeping its id, account, amount, date and note and losing its category and payee, so neither counts as spending or income any more. The two must be on different accounts, on the same date and, between accounts in one currency, of the same amount; an expense that is a refund, or a transaction that is a leg of a transfer already, is refused. Nothing is matched automatically.");
        transfers.MapPost("/{id:guid}/unmark", Unmark).WithName("UnmarkTransfer")
            .WithDescription("Turns a transfer back into two ordinary transactions: its transferOut leg becomes an expense on expenseCategoryId and its transferIn leg income on incomeCategoryId, each keeping its id, account, amount, date and note. Any transfer can be unmarked, marked or recorded. Answers the two transactions; the transfer is no more.");

        return api;
    }

    /// <param name="idempotencyKey">A key the client made up for this transfer, at most 200 characters. A retry sends the same one.</param>
    private static async Task<Results<Created<TransferResponse>, ValidationProblem, ProblemHttpResult>> Create(
        TransferRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CreateTransferHandler handler, HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CreateTransferCommand(request.FromAccountId, request.ToAccountId, request.Amount, request.ToAmount, request.Date, request.Note, idempotencyKey),
            cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            CreateTransferOutcome.Recorded { Transfer: var recorded } =>
                TypedResults.Created($"/api/v1/transfers/{recorded.Id}", Versioned(http, TransferResponse.From(recorded))),
            CreateTransferOutcome.Unmarked => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict, type: TransferUnmarked,
                title: "This Idempotency-Key recorded a transfer that is no longer one.",
                detail: "The transfer this request recorded was unmarked since into an expense and income, so it is not recorded again. Send a new transfer with a new key."),
            _ => TypedResults.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity, type: TransactionEndpoints.IdempotencyKeyReused,
                title: "This Idempotency-Key already recorded something else. Send a new transfer with a new key."),
        };
    }

    private static async Task<Results<Ok<TransferResponse>, NotFound>> Get(Guid id, GetTransferHandler handler, HttpContext http, CancellationToken cancellationToken)
    {
        if (await handler.HandleAsync(new GetTransfer(id), cancellationToken) is not { } transfer)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(Versioned(http, TransferResponse.From(transfer)));
    }

    /// <param name="ifMatch">The transfer's row version as last read, in double quotes: its ETag.</param>
    private static async Task<Results<Ok<TransferResponse>, NotFound, ValidationProblem, ProblemHttpResult>> Edit(
        Guid id, TransferRequest request, [FromHeader(Name = "If-Match")] string? ifMatch, EditTransferHandler handler, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!RowVersions.TryRead(ifMatch, What, out var rowVersion, out var refused))
        {
            return refused;
        }

        var result = await handler.HandleAsync(
            new EditTransferCommand(id, rowVersion, request.FromAccountId, request.ToAccountId, request.Amount, request.ToAmount, request.Date, request.Note),
            cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            null => TypedResults.NotFound(),
            { Edited: { } edited } => TypedResults.Ok(Versioned(http, TransferResponse.From(edited))),
            _ => RowVersions.Stale(What),
        };
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> Delete(Guid id, DeleteTransferHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DeleteTransferCommand(id), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is null ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<Results<Created<TransferResponse>, ValidationProblem>> Mark(
        MarkAsTransferRequest request, MarkAsTransferHandler handler, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new MarkAsTransferCommand(request.ExpenseId, request.IncomeId), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return TypedResults.Created($"/api/v1/transfers/{result.Outcome.Id}", Versioned(http, TransferResponse.From(result.Outcome)));
    }

    private static async Task<Results<Ok<UnmarkedTransferResponse>, NotFound, ValidationProblem>> Unmark(
        Guid id, UnmarkTransferRequest request, UnmarkTransferHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new UnmarkTransferCommand(id, request.ExpenseCategoryId, request.IncomeCategoryId), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is { } unmarked
            ? TypedResults.Ok(new UnmarkedTransferResponse(TransactionResponse.From(unmarked.Expense), TransactionResponse.From(unmarked.Income)))
            : TypedResults.NotFound();
    }

    private static TransferResponse Versioned(HttpContext http, TransferResponse transfer)
    {
        http.SendAsETag(transfer.RowVersion);
        return transfer;
    }
}

/// <summary>What recording or editing a transfer sends. Currencies are not sent: each amount is in the currency of its account.</summary>
/// <param name="FromAccountId">The source account, which the money leaves: one of the user's accounts that is not archived.</param>
/// <param name="ToAccountId">The destination account: another of the user's accounts that is not archived.</param>
/// <param name="Amount">What leaves the source account: a decimal string greater than zero, such as <c>"125000.00"</c>, with no more decimals than that account's currency has.</param>
/// <param name="ToAmount">What arrives on the destination account, in its currency. Required when the two accounts are in different currencies; between accounts in one currency leave it out, or send the same as <c>amount</c>.</param>
/// <param name="Date">A calendar date, <c>YYYY-MM-DD</c>, not after today in the user's time zone.</param>
/// <param name="Note">At most 1000 characters; both legs carry it.</param>
public sealed record TransferRequest(Guid FromAccountId, Guid ToAccountId, string Amount, string Date, string? ToAmount = null, string? Note = null);

/// <summary>The two transactions marked as one transfer.</summary>
/// <param name="ExpenseId">An expense, not a refund, on the account the money left: it becomes the transferOut leg.</param>
/// <param name="IncomeId">Income on another account, on the same date (and of the same amount between accounts in one currency): it becomes the transferIn leg.</param>
public sealed record MarkAsTransferRequest(Guid ExpenseId, Guid IncomeId);

/// <summary>The categories a transfer's legs take once it is unmarked.</summary>
/// <param name="ExpenseCategoryId">An expense category, not archived, for the transferOut leg, which becomes an expense.</param>
/// <param name="IncomeCategoryId">An income category, not archived, for the transferIn leg, which becomes income.</param>
public sealed record UnmarkTransferRequest(Guid ExpenseCategoryId, Guid IncomeCategoryId);

/// <summary>What a transfer became once unmarked: an expense on the account the money left, and income on the one it arrived on.</summary>
public sealed record UnmarkedTransferResponse(TransactionResponse Expense, TransactionResponse Income);

/// <summary>A transfer: its two legs as one movement.</summary>
/// <param name="Id">The <c>transferId</c> its two legs carry in the transaction list.</param>
/// <param name="Amount">What left the source account, a decimal string in <paramref name="Currency"/>.</param>
/// <param name="Currency">The currency of the source account.</param>
/// <param name="ToAmount">What arrived on the destination account, a decimal string in <paramref name="ToCurrency"/>.</param>
/// <param name="ToCurrency">The currency of the destination account.</param>
/// <param name="OutTransactionId">The transferOut leg, a transaction on the source account.</param>
/// <param name="InTransactionId">The transferIn leg, a transaction on the destination account.</param>
/// <param name="RowVersion">Send it as <c>If-Match</c>, in double quotes, when editing the transfer.</param>
public sealed record TransferResponse(
    Guid Id, Guid FromAccountId, Guid ToAccountId, string Amount, Currency Currency, string ToAmount, Currency ToCurrency, DateOnly Date, string? Note,
    Guid OutTransactionId, Guid InTransactionId, string RowVersion)
{
    public static TransferResponse From(TransferDetails transfer) =>
        new(transfer.Id, transfer.FromAccountId, transfer.ToAccountId, AmountJson.Text(transfer.Amount), transfer.Currency, AmountJson.Text(transfer.ToAmount),
            transfer.ToCurrency, transfer.Date, transfer.Note, transfer.OutTransactionId, transfer.InTransactionId,
            transfer.RowVersion.ToString(CultureInfo.InvariantCulture));
}
