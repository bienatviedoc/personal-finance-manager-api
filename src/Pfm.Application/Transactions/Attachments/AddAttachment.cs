using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Attachments;

/// <summary>Attaches a receipt to a transaction: a JPEG, PNG or PDF file of at most 5 MB.</summary>
/// <param name="File">The uploaded file; null when the request held none.</param>
public sealed record AddAttachmentCommand(Guid TransactionId, UploadedFile? File);

/// <summary>A file as it was uploaded. Nothing here is believed until it is checked.</summary>
/// <param name="Name">What the browser called it.</param>
/// <param name="Size">In bytes.</param>
/// <param name="Content">The bytes, readable from the start more than once.</param>
public sealed record UploadedFile(string? Name, long Size, Stream Content);

public sealed class AddAttachmentValidator : AbstractValidator<AddAttachmentCommand>
{
    public AddAttachmentValidator()
    {
        RuleFor(command => command.TransactionId).Required();
        RuleFor(command => command.File).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Choose a file to attach.")
            .Must(file => Attachment.IsValidSize(file!.Size)).WithMessage(command => command.File!.Size > 0 ? Attachment.SizeMessage : "This file is empty.")
            .Must(file => Attachment.FileNameFrom(file!.Name) is not null)
            .WithMessage($"Give the file a name of at most {Attachment.MaxFileNameLength} characters.");
    }
}

/// <summary>Answers null when the user has no transaction with that id, or deleted it.</summary>
public sealed class AddAttachmentHandler(
    IValidator<AddAttachmentCommand> validator, ITransactionRepository transactions, IAttachmentRepository attachments, IObjectStorage storage,
    IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<AddAttachmentCommand, AttachmentDetails?>(validator)
{
    protected override async Task<Result<AttachmentDetails?>> ExecuteAsync(AddAttachmentCommand command, CancellationToken cancellationToken)
    {
        var file = command.File!;
        var transaction = await transactions.FindAsync(command.TransactionId, cancellationToken);
        if (transaction is null)
        {
            return (AttachmentDetails?)null;
        }

        var start = new byte[AttachmentFormat.SignatureLength];
        file.Content.Position = 0;
        var read = await file.Content.ReadAtLeastAsync(start, start.Length, throwOnEndOfStream: false, cancellationToken);

        Attachment attachment;
        try
        {
            attachment = Attachment.To(transaction.Id, transaction.UserId, file.Name!, start.AsSpan(0, read), file.Size, clock.GetUtcNow());
        }
        catch (AttachmentRuleViolation violation)
        {
            return Result<AttachmentDetails?>.Invalid(FieldError.On(nameof(command.File), violation.Message));
        }

        // The file first: an attachment that is listed can always be opened.
        file.Content.Position = 0;
        await storage.PutAsync(attachment.StorageKey, file.Content, attachment.ContentType, cancellationToken);
        try
        {
            attachments.Add(attachment);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Not saved, so nothing points at the file: do not leave it behind.
            await storage.DeleteAsync(attachment.StorageKey, CancellationToken.None);
            throw;
        }

        return AttachmentDetails.Of(attachment);
    }
}
