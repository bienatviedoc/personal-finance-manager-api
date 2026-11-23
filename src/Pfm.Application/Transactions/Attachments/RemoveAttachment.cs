using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Attachments;

/// <summary>Removes an attachment from its transaction, and its file from storage. Unlike a transaction, it is not kept.</summary>
public sealed record RemoveAttachmentCommand(Guid Id);

public sealed class RemoveAttachmentValidator : AbstractValidator<RemoveAttachmentCommand>
{
    public RemoveAttachmentValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>Answers null when the user has no attachment with that id.</summary>
public sealed class RemoveAttachmentHandler(
    IValidator<RemoveAttachmentCommand> validator, IAttachmentRepository attachments, IObjectStorage storage, IUnitOfWork unitOfWork)
    : CommandHandler<RemoveAttachmentCommand, Done?>(validator)
{
    protected override async Task<Result<Done?>> ExecuteAsync(RemoveAttachmentCommand command, CancellationToken cancellationToken)
    {
        var attachment = await attachments.FindAsync(command.Id, cancellationToken);
        if (attachment is null)
        {
            return (Done?)null;
        }

        // The row first: were the file to go first and the save fail, the attachment would be listed and not open.
        attachments.Remove(attachment);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrentChangeException)
        {
            // Another request removed it at the same moment, and takes the file with it.
            return (Done?)null;
        }

        await storage.DeleteAsync(attachment.StorageKey, cancellationToken);
        return new Done();
    }
}
