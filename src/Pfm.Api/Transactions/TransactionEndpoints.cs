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
            .WithDescription("The user's transactions that pass every filter sent, a page at a time, with the total of all that pass, whichever page is asked for. Newest date first and, within a date, last recorded first, unless sort and order say otherwise. A deleted transaction is never listed or counted. The two legs of a transfer are listed like any transaction, each on its own account, with the transferId they share; the total counts them and adds them to neither income nor expenses.");
        transactions.MapGet("/quick-add", QuickAdd).WithName("GetQuickAdd")
            .WithDescription("What quick add starts a new transaction from: the account, not archived, that the user recorded a transaction on last, and the category, not archived, they most often gave the payee. Send the payee as typed; it suggests a category once it is, in any case, a payee the user has used before.");
        transactions.MapGet("/deleted", ListDeleted).WithName("ListDeletedTransactions")
            .WithDescription("The user's recently deleted transactions: those deleted in the last 30 days, which can still be restored, the last deleted first, a page at a time. Each leg of a deleted transfer is listed. After 30 days a deleted transaction is no longer listed or restorable, and the worker removes it for good.");
        transactions.MapPost("/{id:guid}/restore", Restore).WithName("RestoreTransaction")
            .WithDescription("Restores a transaction deleted in the last 30 days: it is back in lists and its account's balance at once, as it was when it was deleted. Restoring a leg of a transfer restores the whole transfer, the other leg with it. One that is not deleted, or was deleted longer ago, is not found. The ETag header carries its row version.");
        transactions.MapPost("/bulk", BulkEdit).WithName("BulkEditTransactions")
            .WithDescription("One action on many transactions at once: setCategory, addTag, removeTag, moveToAccount or delete. Select them by transactionIds, or every one that passes filter, which takes the list's filters; never both, and at most 1000. All of them take the action in one save, or none does: when any cannot, it answers 409 with the problem type urn:pfm:problem:bulk-action-refused, listing each transaction refused and why, and nothing changes. Income takes only an income category and an expense only an expense one; a split keeps its lines and a leg of a transfer has no category; a transaction moves only to an account in its currency, and not while it is reconciled; a reconciled transaction, or a transfer with a reconciled leg, is not deleted. Deleting a leg of a transfer deletes the whole transfer. The balances of the accounts involved follow at once.")
            .Produces<BulkActionRefusedProblem>(StatusCodes.Status409Conflict, "application/problem+json");
        transactions.MapGet("/{id:guid}", Get).WithName("GetTransaction")
            .WithDescription("One transaction. The ETag header carries its row version. A deleted transaction is not found.");
        transactions.MapPost("/", Create).WithName("CreateTransaction")
            .WithDescription("Records income or an expense on an account, in the account's currency; the account's balance follows at once. An expense flagged isRefund is a refund and raises the balance. Send lines in place of categoryId to split it across categories: lines that do not sum exactly to the amount are refused. Send an Idempotency-Key the client made up for this transaction: a retry with the same key answers the transaction the first request recorded and records nothing, and a different transaction sent with a key already used answers 422.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        transactions.MapPut("/{id:guid}", Edit).WithName("EditTransaction")
            .WithDescription("Changes income or an expense; the balance of its account, and of the account it was moved from, follows at once. Send the row version last read as If-Match: a stale one answers 412, none answers 428. A leg of a transfer is refused on its type: edit the transfer under /transfers. A reconciled transaction refuses a change to its amount, date, account, type or refund flag, each named as the field at fault; its payee, note and category can still change.")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        transactions.MapDelete("/{id:guid}", Delete).WithName("DeleteTransaction")
            .WithDescription("Deletes the transaction: it leaves lists and its account's balance at once. Deleting a leg of a transfer deletes the whole transfer, the other leg with it. A reconciled transaction, or a leg of a transfer either of whose legs is reconciled, answers 409 with the problem type urn:pfm:problem:transaction-reconciled and is not deleted.")
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet("/export/transactions.csv", Export).WithName("ExportTransactions").WithTags("Export")
            .WithDescription("Every transaction that passes the filters sent, as a CSV file to download: the whole list, however many pages GET /transactions would take, in its order. One line for each transaction under a line of column names (date, type, isRefund, account, category, payee, note, tags, amount, currency): dates are YYYY-MM-DD, amounts plain decimals above zero, as the list answers them, beside their currency's code. The account and category are named; a split names each of its categories, separated by \"; \", and a leg of a transfer none. Tags are separated the same way. UTF-8 with a byte order mark, comma-separated (RFC 4180).")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return api;
    }

    /// <summary>
    /// Problem type returned when an <c>Idempotency-Key</c> that already recorded a transaction is sent with a
    /// different one: nothing is recorded. Send a new transaction under a new key.
    /// </summary>
    public const string IdempotencyKeyReused = "urn:pfm:problem:idempotency-key-reused";

    /// <summary>
    /// Problem type returned when a reconciled transaction, or a transfer with a reconciled leg, is deleted:
    /// it is locked until its reconciliation is reopened, and nothing changes.
    /// </summary>
    public const string TransactionReconciled = "urn:pfm:problem:transaction-reconciled";

    /// <summary>
    /// Problem type returned when some of the transactions a bulk action selects cannot take it: nothing changed.
    /// Its &lt;c&gt;refused&lt;/c&gt; lists each of them and why.
    /// </summary>
    public const string BulkActionRefused = "urn:pfm:problem:bulk-action-refused";

    /// <param name="from">The earliest date listed, <c>YYYY-MM-DD</c>.</param>
    /// <param name="to">The latest date listed, <c>YYYY-MM-DD</c>.</param>
    /// <param name="accountId">One of the user's accounts: only its transactions are listed.</param>
    /// <param name="categoryId">One of the user's categories: only transactions with a line on it or on one of its subcategories are listed, so a split is found by each of its categories.</param>
    /// <param name="type"><c>income</c>, <c>expense</c>, <c>transferOut</c> or <c>transferIn</c>.</param>
    /// <param name="min">The smallest amount listed, a decimal string, in whatever currency a transaction is in.</param>
    /// <param name="max">The largest amount listed, a decimal string, in whatever currency a transaction is in.</param>
    /// <param name="q">Text the payee, the note or the note of any line contains, in upper or lower case; at most 200 characters.</param>
    /// <param name="tag">The name of one of the user's tags, in any case: only transactions with it are listed.</param>
    /// <param name="sort"><c>date</c> or <c>amount</c>; <c>date</c> when left out.</param>
    /// <param name="order"><c>asc</c> or <c>desc</c>; <c>desc</c> when left out.</param>
    /// <param name="limit">How many transactions a page holds: 1 to 100, 50 when left out.</param>
    /// <param name="cursor">The <c>nextCursor</c> of the page before, asked for with the same filters and sort order; left out for the first page.</param>
    private static async Task<Results<Ok<TransactionListResponse>, ValidationProblem>> List(
        ListTransactionsHandler handler, CancellationToken cancellationToken,
        string? from = null, string? to = null, string? accountId = null, string? categoryId = null, string? type = null,
        string? min = null, string? max = null, string? q = null, string? tag = null, string? sort = null, string? order = null,
        int limit = Paging.DefaultLimit, string? cursor = null)
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

        var asked = new TransactionListParameters();
        var (filter, sorted) = asked.Read(from, to, accountId, categoryId, type, min, max, q, tag, sort, order);
        if (asked.Refused.Count > 0)
        {
            return TypedResults.ValidationProblem(asked.Refused);
        }

        var list = await handler.HandleAsync(new ListTransactions(filter, sorted, limit, after), cancellationToken);
        return TypedResults.Ok(new TransactionListResponse(
            [.. list.Page.Items.Select(TransactionResponse.From)], list.Page.NextCursor, FilteredTotalResponse.From(list.Total)));
    }

    /// <param name="from">The earliest date exported, <c>YYYY-MM-DD</c>.</param>
    /// <param name="to">The latest date exported, <c>YYYY-MM-DD</c>.</param>
    /// <param name="accountId">One of the user's accounts: only its transactions are exported.</param>
    /// <param name="categoryId">One of the user's categories: only transactions with a line on it or on one of its subcategories are exported.</param>
    /// <param name="type"><c>income</c>, <c>expense</c>, <c>transferOut</c> or <c>transferIn</c>.</param>
    /// <param name="min">The smallest amount exported, a decimal string, in whatever currency a transaction is in.</param>
    /// <param name="max">The largest amount exported, a decimal string, in whatever currency a transaction is in.</param>
    /// <param name="q">Text the payee, the note or the note of any line contains, in upper or lower case; at most 200 characters.</param>
    /// <param name="tag">The name of one of the user's tags, in any case: only transactions with it are exported.</param>
    /// <param name="sort"><c>date</c> or <c>amount</c>; <c>date</c> when left out.</param>
    /// <param name="order"><c>asc</c> or <c>desc</c>; <c>desc</c> when left out.</param>
    private static async Task<Results<PushStreamHttpResult, ValidationProblem>> Export(
        ExportTransactionsHandler handler, CancellationToken cancellationToken,
        string? from = null, string? to = null, string? accountId = null, string? categoryId = null, string? type = null,
        string? min = null, string? max = null, string? q = null, string? tag = null, string? sort = null, string? order = null)
    {
        var asked = new TransactionListParameters();
        var (filter, sorted) = asked.Read(from, to, accountId, categoryId, type, min, max, q, tag, sort, order);
        if (asked.Refused.Count > 0)
        {
            return TypedResults.ValidationProblem(asked.Refused);
        }

        var export = await handler.HandleAsync(new ExportTransactions(filter, sorted), cancellationToken);
        // Written to the response a part at a time, so a long list is never held whole.
        return TypedResults.Stream(
            async body =>
            {
                await using var asynchronous = new AsynchronousBody(body);
                await export.WriteCsvAsync(asynchronous, cancellationToken);
                await asynchronous.FlushAsync(cancellationToken);
            },
            "text/csv; charset=utf-8",
            export.FileName);
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

    /// <param name="limit">How many transactions a page holds: 1 to 100, 50 when left out.</param>
    /// <param name="cursor">The <c>nextCursor</c> of the page before; left out for the first page.</param>
    private static async Task<Results<Ok<DeletedTransactionListResponse>, ValidationProblem>> ListDeleted(
        ListDeletedTransactionsHandler handler, CancellationToken cancellationToken, int limit = Paging.DefaultLimit, string? cursor = null)
    {
        if (!Paging.IsValidLimit(limit))
        {
            return PagingProblems.For("limit", $"Ask for 1 to {Paging.MaxLimit} at a time.");
        }

        DeletedTransactionCursor? after = null;
        if (cursor is not null && !DeletedTransactionCursor.TryParse(cursor, out after))
        {
            return PagingProblems.For("cursor", "Use the nextCursor of the page before.");
        }

        var page = await handler.HandleAsync(new ListDeletedTransactions(limit, after), cancellationToken);
        return TypedResults.Ok(new DeletedTransactionListResponse([.. page.Items.Select(DeletedTransactionResponse.From)], page.NextCursor));
    }

    private static async Task<Results<Ok<TransactionResponse>, NotFound, ValidationProblem>> Restore(
        Guid id, RestoreTransactionHandler handler, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new RestoreTransactionCommand(id), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is { } restored ? TypedResults.Ok(Versioned(http, TransactionResponse.From(restored))) : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<BulkEditResponse>, ValidationProblem, JsonHttpResult<BulkActionRefusedProblem>>> BulkEdit(
        BulkEditRequest request, BulkEditTransactionsHandler handler, CancellationToken cancellationToken)
    {
        TransactionFilter? filter = null;
        if (request.Filter is { } sent)
        {
            var asked = new TransactionListParameters();
            const string In = "filter.";
            filter = new TransactionFilter(
                asked.Date(In + "from", sent.From), asked.Date(In + "to", sent.To), asked.Id(In + "accountId", sent.AccountId),
                asked.Id(In + "categoryId", sent.CategoryId), asked.OneOf<TransactionType>(In + "type", sent.Type), asked.Amount(In + "min", sent.Min),
                asked.Amount(In + "max", sent.Max), asked.Search(In + "q", sent.Q), asked.Tag(In + "tag", sent.Tag));
            if (asked.Refused.Count > 0)
            {
                return TypedResults.ValidationProblem(asked.Refused);
            }
        }

        var result = await handler.HandleAsync(
            new BulkEditTransactionsCommand(request.Action, request.TransactionIds, filter, request.CategoryId, request.Tag, request.AccountId), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            BulkEditOutcome.Done done => TypedResults.Ok(new BulkEditResponse(done.Count)),
            BulkEditOutcome.Refused refused => TypedResults.Json(
                new BulkActionRefusedProblem(
                    BulkActionRefused, "Some of the transactions cannot take this action, so none did.", StatusCodes.Status409Conflict,
                    [.. refused.Rows.Select(row => new RefusedTransactionResponse(row.TransactionId, row.Reason, row.Message))]),
                statusCode: StatusCodes.Status409Conflict, contentType: "application/problem+json"),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Not an outcome of a bulk action."),
        };
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
                request.IsRefund, request.LineInputs(), Tags: request.Tags),
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
                request.IsRefund, request.LineInputs(), request.Tags),
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

    private static async Task<Results<NoContent, NotFound, ValidationProblem, ProblemHttpResult>> Delete(
        Guid id, DeleteTransactionHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DeleteTransactionCommand(id), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            null => TypedResults.NotFound(),
            DeleteOutcome.Reconciled => Reconciled(),
            _ => TypedResults.NoContent(),
        };
    }

    /// <summary>A 409 for a delete of a reconciled transaction, or of a transfer with a reconciled leg.</summary>
    internal static ProblemHttpResult Reconciled() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict, type: TransactionReconciled,
            title: "This transaction is reconciled.",
            detail: "A reconciled transaction is locked: reopen its reconciliation to delete it.");

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
/// <param name="Tags">The names of its tags, at most 20, each 1 to 50 characters: <c>tet-2027</c>, <c>japan-trip</c>. Kept trimmed and in lower case; a name the user has no tag with yet creates one. An edit replaces the tags the transaction had, and one without them takes every tag off.</param>
public sealed record TransactionRequest(
    string Type, Guid AccountId, string Amount, string Date, Guid? CategoryId = null, string? Payee = null, string? Note = null, bool IsRefund = false,
    IReadOnlyList<TransactionLineRequest>? Lines = null, IReadOnlyList<string?>? Tags = null)
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

/// <summary>A transaction: income, an expense, or one of the two legs of a transfer.</summary>
/// <param name="Type"><c>transferOut</c> and <c>transferIn</c> are the legs of a transfer, which is neither income nor spending.</param>
/// <param name="CategoryId">Null for a leg of a transfer, which has no category.</param>
/// <param name="TransferId">For a leg of a transfer, the transfer it belongs to: read, edit and delete it under <c>/transfers/{transferId}</c>. Null for income and expenses.</param>
/// <param name="Amount">A decimal string in <paramref name="Currency"/>, greater than zero; the type gives the sign, except that a refund raises the balance.</param>
/// <param name="IsRefund">Whether the expense is a refund: money coming back, which raises the account's balance and lowers spent in its category.</param>
/// <param name="Currency">The currency of the transaction's account.</param>
/// <param name="CategoryId">The category of the first line: the transaction's only category unless it is split.</param>
/// <param name="RowVersion">Send it as <c>If-Match</c>, in double quotes, when editing the transaction.</param>
/// <param name="Lines">One line with the whole amount, or, for a split transaction, its lines in the order they were entered; their amounts sum exactly to <paramref name="Amount"/>.</param>
/// <param name="RecurringRuleId">The recurring rule that posted the transaction; null for one the user recorded, and once that rule is deleted.</param>
/// <param name="Tags">The names of its tags, in alphabetical order; none for a transaction without tags.</param>
/// <param name="IsReconciled">Whether it is in a completed reconciliation: its amount, date, account and type cannot change, and it cannot be deleted, until that reconciliation is reopened.</param>
public sealed record TransactionResponse(
    Guid Id, TransactionType Type, Guid AccountId, string Amount, Currency Currency, DateOnly Date, Guid? CategoryId, string? Payee, string? Note,
    Guid? TransferId, string RowVersion, bool IsRefund, IReadOnlyList<TransactionLineResponse> Lines, Guid? RecurringRuleId, IReadOnlyList<string> Tags,
    bool IsReconciled)
{
    public static TransactionResponse From(TransactionDetails transaction) =>
        new(transaction.Id, transaction.Type, transaction.AccountId, AmountJson.Text(transaction.Amount), transaction.Currency, transaction.Date,
            transaction.CategoryId, transaction.Payee, transaction.Note, transaction.TransferId, transaction.RowVersion.ToString(CultureInfo.InvariantCulture),
            transaction.IsRefund, [.. transaction.Lines.Select(line => new TransactionLineResponse(line.CategoryId, AmountJson.Text(line.Amount), line.Note))],
            transaction.RecurringRuleId, transaction.Tags, transaction.IsReconciled);
}

/// <summary>A recently deleted transaction, as it was when it was deleted.</summary>
/// <param name="Type"><c>transferOut</c> and <c>transferIn</c> are the legs of a deleted transfer; restoring either restores both.</param>
/// <param name="Amount">A decimal string in <paramref name="Currency"/>, greater than zero; the type gives the sign, except that a refund raises the balance.</param>
/// <param name="DeletedAt">When it was deleted.</param>
/// <param name="RestorableUntil">When it can no longer be restored: 30 days after it was deleted. The worker then removes it for good.</param>
public sealed record DeletedTransactionResponse(
    Guid Id, TransactionType Type, Guid AccountId, string Amount, Currency Currency, DateOnly Date, Guid? CategoryId, string? Payee, string? Note,
    Guid? TransferId, bool IsRefund, IReadOnlyList<TransactionLineResponse> Lines, DateTimeOffset DeletedAt, DateTimeOffset RestorableUntil)
{
    /// <summary>Written the way a transaction is, less what only a live one has.</summary>
    public static DeletedTransactionResponse From(DeletedTransactionDetails deleted)
    {
        var transaction = TransactionResponse.From(deleted.Transaction);
        return new(transaction.Id, transaction.Type, transaction.AccountId, transaction.Amount, transaction.Currency, transaction.Date,
            transaction.CategoryId, transaction.Payee, transaction.Note, transaction.TransferId, transaction.IsRefund, transaction.Lines,
            deleted.DeletedAt, deleted.RestorableUntil);
    }
}

/// <summary>A page of recently deleted transactions. <c>nextCursor</c> asks for the next page; it is null on the last.</summary>
public sealed record DeletedTransactionListResponse(IReadOnlyList<DeletedTransactionResponse> Items, string? NextCursor);

/// <summary>What quick add starts a new transaction from.</summary>
/// <param name="LastAccountId">The account, not archived, that the user recorded a transaction on last; null when they have recorded none.</param>
/// <param name="SuggestedCategoryId">The category, not archived, the user most often gave the payee, and of those given equally often the one given last; null when they never used the payee.</param>
public sealed record QuickAddResponse(Guid? LastAccountId, Guid? SuggestedCategoryId);

/// <summary>One action on many transactions (spec TXN-7).</summary>
/// <param name="Action"><c>setCategory</c>, <c>addTag</c>, <c>removeTag</c>, <c>moveToAccount</c> or <c>delete</c>.</param>
/// <param name="TransactionIds">The transactions selected one by one: 1 to 1000. Leave out when sending &lt;paramref name="Filter"/&gt;.</param>
/// <param name="Filter">Selects every transaction that passes it, as the list would show them: at most 1000. Leave out when sending &lt;paramref name="TransactionIds"/&gt;.</param>
/// <param name="CategoryId">For setCategory: one of the user's categories that is not archived.</param>
/// <param name="Tag">For addTag and removeTag: the tag's name, 1 to 50 characters, kept trimmed and in lower case. Adding one the user has no tag with yet creates it.</param>
/// <param name="AccountId">For moveToAccount: one of the user's accounts that is not archived.</param>
public sealed record BulkEditRequest(
    string Action, IReadOnlyList<Guid>? TransactionIds = null, BulkFilterRequest? Filter = null, Guid? CategoryId = null, string? Tag = null,
    Guid? AccountId = null);

/// <summary>The filters of the transaction list, each as its query parameter of the same name takes it; one left out filters nothing.</summary>
public sealed record BulkFilterRequest(
    string? From = null, string? To = null, string? AccountId = null, string? CategoryId = null, string? Type = null, string? Min = null, string? Max = null,
    string? Q = null, string? Tag = null);

/// <summary>A bulk action done.</summary>
/// <param name="Count">How many transactions it was done to: every one selected.</param>
public sealed record BulkEditResponse(int Count);

/// <summary>A bulk action refused because some of the transactions it selects cannot take it. Nothing changed.</summary>
/// <param name="Type">&lt;c&gt;urn:pfm:problem:bulk-action-refused&lt;/c&gt;.</param>
/// <param name="Refused">Each transaction refused, in the order they were selected.</param>
public sealed record BulkActionRefusedProblem(string Type, string Title, int Status, IReadOnlyList<RefusedTransactionResponse> Refused);

/// <summary>A transaction that cannot take a bulk action.</summary>
/// <param name="Reason">Why: notFound, transferLeg, split, categoryKind, reconciled, otherCurrency or tooManyTags.</param>
/// <param name="Message">The reason in words, in English.</param>
public sealed record RefusedTransactionResponse(Guid TransactionId, BulkRefusal Reason, string Message);

/// <summary>A line of a transaction.</summary>
/// <param name="Amount">A decimal string in the transaction's currency, greater than zero.</param>
public sealed record TransactionLineResponse(Guid CategoryId, string Amount, string? Note);

/// <summary>A page of transactions. <c>nextCursor</c> asks for the next page; it is null on the last.</summary>
/// <param name="Total">What every transaction that passed the filters comes to, whichever page this is.</param>
public sealed record TransactionListResponse(IReadOnlyList<TransactionResponse> Items, string? NextCursor, FilteredTotalResponse Total);

/// <summary>The total of the transactions that passed the filters of a list, on every page of it.</summary>
/// <param name="Count">How many transactions passed.</param>
/// <param name="Currencies">Their amounts added up in each currency they are in. Currencies are never added together.</param>
/// <param name="Spent">
/// Only when the list is filtered by a category: what they spent in it and its subcategories, in the base currency,
/// exactly as a budget counts spent. Filtered to a budget month too, it is that budget's spent. Null otherwise.
/// </param>
public sealed record FilteredTotalResponse(int Count, IReadOnlyList<CurrencyTotalResponse> Currencies, SpentTotalResponse? Spent)
{
    public static FilteredTotalResponse From(FilteredTotal total) =>
        new(total.Count, [.. total.Currencies.Select(CurrencyTotalResponse.From)], total.Spent is { } spent ? SpentTotalResponse.From(spent) : null);
}

/// <summary>What filtered transactions spent in a category and its subcategories (spec section 6).</summary>
/// <param name="Currency">The user's base currency.</param>
/// <param name="Amount">A decimal string: the expenses minus the refunds, each as it was converted at the rate of its own date, a split counting only its lines in the category. It can be negative.</param>
/// <param name="NotConverted">How many of the expenses have no exchange rate yet, and so are left out of <paramref name="Amount"/>.</param>
public sealed record SpentTotalResponse(Currency Currency, string Amount, int NotConverted)
{
    public static SpentTotalResponse From(SpentTotal spent) => new(spent.Currency, AmountJson.Text(spent.Amount), spent.NotConverted);
}

/// <summary>The filtered transactions in one currency, added up.</summary>
/// <param name="Income">The income among them, a decimal string.</param>
/// <param name="Expenses">The expenses among them minus the refunds, a decimal string: negative when more came back than was spent.</param>
/// <param name="Net">Each amount with the sign of its type, added up: income minus expenses.</param>
public sealed record CurrencyTotalResponse(Currency Currency, string Income, string Expenses, string Net)
{
    public static CurrencyTotalResponse From(CurrencyTotal total) =>
        new(total.Currency, AmountJson.Text(total.Income), AmountJson.Text(total.Expenses), AmountJson.Text(total.Net));
}

/// <summary>
/// Reads the filters and sort order of a list of transactions from its query string, and keeps a message for
/// each one it cannot read, under the parameter's name.
/// </summary>
internal sealed class TransactionListParameters
{
    public Dictionary<string, string[]> Refused { get; } = [];

    /// <summary>The filters and the sort order, the API's own order (newest date first) for what is left out.</summary>
    public (TransactionFilter Filter, TransactionSort Sort) Read(
        string? from, string? to, string? accountId, string? categoryId, string? type, string? min, string? max, string? q, string? tag, string? sort, string? order) =>
        (new TransactionFilter(
                Date(nameof(from), from), Date(nameof(to), to), Id(nameof(accountId), accountId), Id(nameof(categoryId), categoryId),
                OneOf<TransactionType>(nameof(type), type), Amount(nameof(min), min), Amount(nameof(max), max), Search(nameof(q), q),
                Tag(nameof(tag), tag)),
            new TransactionSort(
                OneOf<TransactionSortBy>(nameof(sort), sort) ?? TransactionSortBy.Date, OneOf<SortOrder>(nameof(order), order) ?? SortOrder.Desc));

    public DateOnly? Date(string name, string? text) => Read(name, text, DateText.Read(text), DateText.Message);

    public Guid? Id(string name, string? text) => Read(name, text, Guid.TryParse(text, out var id) ? id : (Guid?)null, "Send an id.");

    public TEnum? OneOf<TEnum>(string name, string? text)
        where TEnum : struct, Enum =>
        Read(name, text, EnumText<TEnum>.Read(text), $"Choose one of: {string.Join(", ", EnumText<TEnum>.Names)}.");

    /// <summary>A transaction's amount is above zero, so a negative one bounds nothing.</summary>
    public decimal? Amount(string name, string? text) =>
        Read(name, text, AmountText.TryParse(text, out var amount) && amount >= 0 ? amount : (decimal?)null, "Enter an amount that is not negative, such as 125000.00.");

    /// <summary>The text to search for, trimmed; nothing but spaces searches for nothing.</summary>
    public string? Search(string name, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var search = text.Trim();
        return Read(name, search, search.Length <= TransactionFilter.MaxSearchLength ? search : null, $"Search for at most {TransactionFilter.MaxSearchLength} characters.");
    }

    /// <summary>A tag's name as tags keep it; nothing but spaces filters by no tag.</summary>
    public string? Tag(string name, string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : Read(name, text, Domain.Transactions.Tag.IsValidName(text) ? Domain.Transactions.Tag.Normalised(text) : null, $"Send a tag of at most {Domain.Transactions.Tag.MaxNameLength} characters.");

    private T? Read<T>(string name, string? text, T? read, string message)
    {
        if (text is not null && read is null)
        {
            Refused[name] = [message];
        }

        return read;
    }
}
