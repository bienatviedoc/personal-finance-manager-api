using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Accounts;
using Pfm.Application.UseCases;
using Pfm.Domain.Imports;

namespace Pfm.Application.Imports;

/// <summary>Uploads a bank statement, as a CSV file of at most 5 MB, to import into one of the user's accounts (spec IMP-1).</summary>
/// <param name="File">The statement; null when the request held none.</param>
public sealed record UploadStatementCommand(Guid AccountId, UploadedFile? File);

public sealed class UploadStatementValidator : AbstractValidator<UploadStatementCommand>
{
    public UploadStatementValidator()
    {
        RuleFor(command => command.AccountId).Required();
        RuleFor(command => command.File).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Choose a statement file to upload.")
            .Must(file => file!.Size > 0).WithMessage("This file is empty.")
            .Must(file => file!.Size <= ImportBatch.MaxFileSize).WithMessage(ImportBatch.FileSizeMessage)
            .Must(file => ImportBatch.FileNameFrom(file!.Name) is not null).WithMessage("Give the file a name.");
    }
}

/// <summary>
/// Keeps the file as an import batch on the account, to preview and commit. Nothing is imported yet. The file
/// must be text: UTF-8, with or without a byte order mark, or UTF-16 with one; a spreadsheet saved as such is refused.
/// </summary>
public sealed class UploadStatementHandler(
    IValidator<UploadStatementCommand> validator, IAccountLookup accounts, IImportBatchRepository batches, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<UploadStatementCommand, ImportBatchDetails>(validator)
{
    public const string NotAnAccount = "Choose one of your accounts that is not archived.";

    protected override async Task<Result<ImportBatchDetails>> ExecuteAsync(UploadStatementCommand command, CancellationToken cancellationToken)
    {
        if (await accounts.FindAsync(command.AccountId, cancellationToken) is not { IsArchived: false } account)
        {
            return Result<ImportBatchDetails>.Invalid(FieldError.On(nameof(command.AccountId), NotAnAccount));
        }

        var file = command.File!;
        using var bytes = new MemoryStream(checked((int)file.Size));
        file.Content.Position = 0;
        await file.Content.CopyToAsync(bytes, cancellationToken);
        if (StatementFileText.Decode(bytes.GetBuffer().AsSpan(0, (int)bytes.Length)) is not { } text)
        {
            return Result<ImportBatchDetails>.Invalid(FieldError.On(nameof(command.File), "Upload a CSV file saved as text (UTF-8)."));
        }

        var batch = ImportBatch.Upload(account.Id, file.Name!, text, clock.GetUtcNow());
        batches.Add(batch);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ImportBatchDetails.Of(batch);
    }
}
