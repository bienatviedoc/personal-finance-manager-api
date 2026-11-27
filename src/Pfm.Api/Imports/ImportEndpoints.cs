using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pfm.Application.Abstractions;
using Pfm.Application.Imports;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Imports;

namespace Pfm.Api.Imports;

/// <summary>Statement import over HTTP (spec IMP-1, IMP-3). The use cases decide everything; these only map requests and answers.</summary>
public static class ImportEndpoints
{
    /// <summary>A batch has been committed: it cannot be committed again, and its file is no longer kept to preview.</summary>
    public const string ImportCommitted = "urn:pfm:problem:import-committed";

    /// <summary>Another statement is being imported into the account: one is committed into an account at a time.</summary>
    public const string ImportInProgress = "urn:pfm:problem:import-in-progress";

    /// <summary>Room for a file somewhat over the 5 MB a statement may be, so that one gets the answer that says so.</summary>
    private const long MaxRequestSize = 2 * ImportBatch.MaxFileSize;

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder api)
    {
        var imports = api.MapGroup("/imports").WithTags("Imports").ProducesProblem(StatusCodes.Status401Unauthorized);

        imports.MapPost("/", Upload).WithName("UploadStatement")
            .WithDescription("Uploads a bank statement to import into one of the user's accounts: a CSV file of at most 5 MB, saved as text (UTF-8, or UTF-16 with a byte order mark), sent as the file field of a multipart/form-data body beside accountId. Nothing is imported yet: the answer is the import batch, with a first guess at how to read the file (suggestedMapping) for the user to confirm or change in the preview.")
            .WithMetadata(new UploadSizeLimit(MaxRequestSize, ImportBatch.FileSizeMessage))
            // The X-XSRF-TOKEN header is checked for every request under /api (UseApiAntiforgery); a form needs no second check of its own.
            .DisableAntiforgery();
        imports.MapGet("/{id:guid}", Get).WithName("GetImportBatch")
            .WithDescription("One import batch: where it has got to (uploaded, committing, committed or failed) and its counts. While it is committing, importedCount grows towards selectedCount as the worker imports the rows: ask again every second or two to show progress.");
        imports.MapGet("/{id:guid}/preview", Preview).WithName("PreviewImport")
            .WithDescription("The uploaded statement read with a mapping, before anything is saved: every row with its cells, what it is (header, skipped, blank, parsed, failed or unmapped), for a parsed row the transaction it says, and for a failed row what is wrong with it. A row with the date, amount, direction and description (compared without case, accents or extra spaces) of a transaction already on the account is a likely duplicate, unticked (selected false). Without a date column and an amount (or a debit and a credit) column the rows are only cut into cells, for the user to map. A committed batch keeps no file: 409 with the problem type urn:pfm:problem:import-committed.")
            .ProducesProblem(StatusCodes.Status409Conflict);
        imports.MapPost("/{id:guid}/commit", Commit).WithName("CommitImport")
            .WithDescription("Commits the statement: the rows chosen (rows the preview reads as transactions, with the same mapping) are imported in the background as income (on incomeCategoryId) or expenses (on expenseCategoryId) on the batch's account, each belonging to the batch, and the answer is 202 with the batch, committing. A likely duplicate is imported only when it is chosen; those left out are counted in duplicateCount. A batch committed already answers 409 with the problem type urn:pfm:problem:import-committed. While another statement is being imported into the same account it answers 409 with the problem type urn:pfm:problem:import-in-progress and changes nothing: once that one is imported, preview again, and the rows it brought in are likely duplicates.")
            .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }

    /// <param name="accountId">The account to import into: one of the user's that is not archived.</param>
    /// <param name="file">The statement: a CSV file of at most 5 MB.</param>
    private static async Task<Results<Created<ImportBatchResponse>, ValidationProblem>> Upload(
        [FromForm] string? accountId, IFormFile? file, UploadStatementHandler handler, CancellationToken cancellationToken)
    {
        await using var content = file?.OpenReadStream();
        var result = await handler.HandleAsync(
            new UploadStatementCommand(
                Guid.TryParse(accountId, out var account) ? account : Guid.Empty, file is null ? null : new UploadedFile(file.FileName, file.Length, content!)),
            cancellationToken);
        return result.IsValid
            ? TypedResults.Created($"/api/v1/imports/{result.Outcome.Id}", ImportBatchResponse.From(result.Outcome))
            : ValidationProblems.For(result);
    }

    private static async Task<Results<Ok<ImportBatchResponse>, NotFound>> Get(Guid id, GetImportBatchHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new GetImportBatch(id), cancellationToken) is { } batch ? TypedResults.Ok(ImportBatchResponse.From(batch)) : TypedResults.NotFound();

    private static async Task<Results<Ok<ImportPreviewResponse>, NotFound, ValidationProblem, ProblemHttpResult>> Preview(
        Guid id, [AsParameters] MappingParameters parameters, PreviewImportHandler handler, CancellationToken cancellationToken)
    {
        var input = parameters.Input();
        if (input.Read() is not { } mapping)
        {
            return ValidationProblems.For(Result<ImportPreviewResponse>.Invalid(input.Errors()));
        }

        return await handler.HandleAsync(new PreviewImport(id, mapping), cancellationToken) switch
        {
            null => TypedResults.NotFound(),
            PreviewImportOutcome.Ready ready => TypedResults.Ok(ImportPreviewResponse.From(ready.Preview)),
            _ => Committed(),
        };
    }

    private static async Task<Results<Accepted<ImportBatchResponse>, NotFound, ValidationProblem, ProblemHttpResult>> Commit(
        Guid id, CommitImportRequest request, CommitImportHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CommitImportCommand(id, request.Mapping?.Input(), request.Rows, request.ExpenseCategoryId, request.IncomeCategoryId), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            null => TypedResults.NotFound(),
            CommitImportOutcome.Committing committing => TypedResults.Accepted($"/api/v1/imports/{id}", ImportBatchResponse.From(committing.Batch)),
            CommitImportOutcome.ImportInProgress => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict, type: ImportInProgress, title: "Another statement is being imported into this account.",
                detail: "Wait for it to finish, then check the preview again: the rows it imported show as likely duplicates."),
            _ => Committed(),
        };
    }

    private static ProblemHttpResult Committed() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict, type: ImportCommitted, title: "This statement has been committed already.",
            detail: "Its rows are imported, or being imported. Upload the file again to import it once more.");
}

/// <summary>
/// How to read a statement, as the preview's query string or a commit's body sends it. Columns are counted from 0.
/// </summary>
/// <param name="Separator"><c>comma</c>, <c>semicolon</c>, <c>tab</c> or <c>pipe</c>.</param>
/// <param name="SkipFirstRows">Rows before the header row (or the first transaction): a preamble. 0 when left out.</param>
/// <param name="HasHeaderRow">Whether the row after the preamble names the columns.</param>
/// <param name="SkipLastRows">Rows after the last transaction, such as a total; blank rows at the very end are not counted. 0 when left out.</param>
/// <param name="DateColumn">The column with the date.</param>
/// <param name="DescriptionColumn">The column with the description, which becomes the payee.</param>
/// <param name="AmountColumn">The column with one signed amount, below zero for money going out; or else a debit and a credit column.</param>
/// <param name="DebitColumn">The column with money going out.</param>
/// <param name="CreditColumn">The column with money coming in.</param>
/// <param name="DateFormat"><c>dayMonthYear</c> (when left out), <c>monthDayYear</c> or <c>yearMonthDay</c>, whatever separates the parts; a time after the date is passed over.</param>
/// <param name="DecimalSeparator"><c>dot</c> (when left out) or <c>comma</c>; the other one groups thousands.</param>
public sealed record MappingRequest(
    string? Separator, int? SkipFirstRows, bool? HasHeaderRow, int? SkipLastRows, int? DateColumn, int? DescriptionColumn, int? AmountColumn,
    int? DebitColumn, int? CreditColumn, string? DateFormat, string? DecimalSeparator)
{
    public MappingInput Input() =>
        new(Separator, SkipFirstRows, HasHeaderRow, SkipLastRows, DateColumn, DescriptionColumn, AmountColumn, DebitColumn, CreditColumn, DateFormat,
            DecimalSeparator);
}

/// <summary>The mapping a preview is read with, from its query string; see <see cref="MappingRequest"/> for each.</summary>
public sealed class MappingParameters
{
    [FromQuery(Name = "separator")] public string? Separator { get; init; }

    [FromQuery(Name = "skipFirstRows")] public int? SkipFirstRows { get; init; }

    [FromQuery(Name = "hasHeaderRow")] public bool? HasHeaderRow { get; init; }

    [FromQuery(Name = "skipLastRows")] public int? SkipLastRows { get; init; }

    [FromQuery(Name = "dateColumn")] public int? DateColumn { get; init; }

    [FromQuery(Name = "descriptionColumn")] public int? DescriptionColumn { get; init; }

    [FromQuery(Name = "amountColumn")] public int? AmountColumn { get; init; }

    [FromQuery(Name = "debitColumn")] public int? DebitColumn { get; init; }

    [FromQuery(Name = "creditColumn")] public int? CreditColumn { get; init; }

    [FromQuery(Name = "dateFormat")] public string? DateFormat { get; init; }

    [FromQuery(Name = "decimalSeparator")] public string? DecimalSeparator { get; init; }

    public MappingInput Input() =>
        new(Separator, SkipFirstRows, HasHeaderRow, SkipLastRows, DateColumn, DescriptionColumn, AmountColumn, DebitColumn, CreditColumn, DateFormat,
            DecimalSeparator);
}

/// <summary>What to import of an uploaded statement.</summary>
/// <param name="Mapping">The mapping the statement was previewed with.</param>
/// <param name="Rows">The numbers of the rows to import, as the preview numbers them.</param>
/// <param name="ExpenseCategoryId">The expense category money going out is recorded on; needed when a chosen row takes money out.</param>
/// <param name="IncomeCategoryId">The income category money coming in is recorded on; needed when a chosen row brings money in.</param>
public sealed record CommitImportRequest(MappingRequest? Mapping, IReadOnlyList<int>? Rows, Guid? ExpenseCategoryId, Guid? IncomeCategoryId);

/// <summary>How a statement is read, as it was committed or as it is first guessed. Columns are counted from 0.</summary>
public sealed record MappingResponse(
    Separator Separator, int SkipFirstRows, bool HasHeaderRow, int SkipLastRows, int? DateColumn, int? DescriptionColumn, int? AmountColumn,
    int? DebitColumn, int? CreditColumn, DateFormat DateFormat, DecimalSeparator DecimalSeparator)
{
    public static MappingResponse? From(StatementMapping? mapping) =>
        mapping is null
            ? null
            : new(mapping.Separator, mapping.SkipFirstRows, mapping.HasHeaderRow, mapping.SkipLastRows, mapping.DateColumn, mapping.DescriptionColumn,
                mapping.AmountColumn, mapping.DebitColumn, mapping.CreditColumn, mapping.DateFormat, mapping.DecimalSeparator);
}

/// <summary>An import batch: one uploaded statement and the transactions created from it.</summary>
/// <param name="FileName">What the user's file was called.</param>
/// <param name="Status"><c>uploaded</c> (to preview and commit), <c>committing</c> (the worker is importing it), <c>committed</c>, or <c>failed</c> (stopped, for the reason in failure; what it imported stays).</param>
/// <param name="RowCount">Once committed, the rows of the file that are transactions or failed to be read as one.</param>
/// <param name="SelectedCount">How many rows the user chose to import.</param>
/// <param name="ImportedCount">How many of them are transactions now; it reaches selectedCount when the batch is committed.</param>
/// <param name="DuplicateCount">How many likely duplicates were left out, unticked.</param>
/// <param name="FailedCount">How many rows could not be read as a transaction, and were not imported.</param>
/// <param name="Failure">Why the worker stopped, when the batch failed: <c>accountArchived</c>, <c>categoryArchived</c> (a category chosen for it was archived since it was committed), <c>accountCurrencyChanged</c> or <c>transactionRefused</c> (a row broke another rule of transactions). A code, for the app to say in the user's language.</param>
/// <param name="CommittedAt">When its last row was imported.</param>
/// <param name="Mapping">How the file was read when it was committed; null before.</param>
/// <param name="SuggestedMapping">A first guess at how to read the file (its separator, and the header row after any preamble), while it is uploaded; null after.</param>
public sealed record ImportBatchResponse(
    Guid Id, Guid AccountId, string FileName, ImportBatchStatus Status, int RowCount, int SelectedCount, int ImportedCount, int DuplicateCount,
    int FailedCount, ImportFailure? Failure, DateTimeOffset CreatedAt, DateTimeOffset? CommittedAt, MappingResponse? Mapping, MappingResponse? SuggestedMapping)
{
    public static ImportBatchResponse From(ImportBatchDetails batch) =>
        new(batch.Id, batch.AccountId, batch.FileName, batch.Status, batch.RowCount, batch.SelectedCount, batch.ImportedCount, batch.DuplicateCount,
            batch.FailedCount, batch.Failure, batch.CreatedAt, batch.CommittedAt, MappingResponse.From(batch.Mapping), MappingResponse.From(batch.SuggestedMapping));
}

/// <summary>One row of a previewed statement.</summary>
/// <param name="Number">Its place in the file, counted from 1: what a commit names it by.</param>
/// <param name="Cells">What the file holds in each cell, as written.</param>
/// <param name="Kind"><c>skipped</c> (a preamble or footer row), <c>header</c>, <c>blank</c>, <c>parsed</c> (a transaction), <c>failed</c> (see problem) or <c>unmapped</c>.</param>
/// <param name="Date">For a parsed row, the transaction's date.</param>
/// <param name="Type">For a parsed row, <c>income</c> (money came in) or <c>expense</c>.</param>
/// <param name="Amount">For a parsed row, a decimal string in the account's currency, greater than zero.</param>
/// <param name="Description">For a parsed row, its description, which becomes the payee; null when it has none.</param>
/// <param name="Problem">For a failed row, why it cannot be imported.</param>
/// <param name="LikelyDuplicate">Whether it matches a transaction already on the account.</param>
/// <param name="Selected">Ticked to import at first: parsed and not a likely duplicate.</param>
public sealed record PreviewRowResponse(
    int Number, IReadOnlyList<string> Cells, StatementRowKind Kind, DateOnly? Date, StatementEntryType? Type, string? Amount, string? Description,
    RowProblem? Problem, bool LikelyDuplicate, bool Selected);

/// <summary>How many rows of the preview are of each kind that matters before committing.</summary>
/// <param name="Parsed">Read as transactions.</param>
/// <param name="Failed">Could not be read as transactions, and will not be imported.</param>
/// <param name="LikelyDuplicates">Parsed rows matching a transaction already on the account: skipped unless ticked.</param>
/// <param name="Selected">Ticked to import at first: parsed rows but the likely duplicates.</param>
public sealed record PreviewCountsResponse(int Parsed, int Failed, int LikelyDuplicates, int Selected);

/// <summary>An uploaded statement read with a mapping. Every row is in it, not a page: a preview is checked whole before committing.</summary>
/// <param name="Header">The cells of the header row; none without one.</param>
/// <param name="ColumnCount">The most cells any row has: the columns there are to map.</param>
/// <param name="Currency">The account's, which every amount is in.</param>
public sealed record ImportPreviewResponse(
    IReadOnlyList<string> Header, int ColumnCount, Currency Currency, IReadOnlyList<PreviewRowResponse> Rows, PreviewCountsResponse Counts)
{
    public static ImportPreviewResponse From(ImportPreview preview) =>
        new(preview.Header, preview.ColumnCount, preview.Currency,
            [.. preview.Rows.Select(row => new PreviewRowResponse(
                row.Row.Number, row.Row.Cells, row.Row.Kind, row.Row.Entry?.Date, row.Row.Entry?.Type,
                row.Row.Entry is { } entry ? AmountJson.Text(entry.Amount) : null, row.Row.Entry?.Description, row.Row.Problem, row.LikelyDuplicate,
                row.Selected))],
            new PreviewCountsResponse(preview.Parsed, preview.Failed, preview.LikelyDuplicates, preview.Selected));
}
