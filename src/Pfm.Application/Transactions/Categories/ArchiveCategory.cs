using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Categories;

/// <summary>What deleting a category does: it and its subcategories are archived, never removed.</summary>
public sealed record ArchiveCategoryCommand(Guid Id);

public sealed class ArchiveCategoryValidator : AbstractValidator<ArchiveCategoryCommand>
{
    public ArchiveCategoryValidator() => RuleFor(command => command.Id).Required();
}

public enum ArchiveCategoryOutcome
{
    /// <summary>Archived now, or already archived.</summary>
    Archived,

    /// <summary>The user has no category with that id.</summary>
    NotFound,
}

public sealed class ArchiveCategoryHandler(
    IValidator<ArchiveCategoryCommand> validator, ICategoryRepository categories, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<ArchiveCategoryCommand, ArchiveCategoryOutcome>(validator)
{
    protected override async Task<Result<ArchiveCategoryOutcome>> ExecuteAsync(ArchiveCategoryCommand command, CancellationToken cancellationToken)
    {
        var all = await categories.ListAsync(cancellationToken);
        var category = all.SingleOrDefault(candidate => candidate.Id == command.Id);
        if (category is null)
        {
            return ArchiveCategoryOutcome.NotFound;
        }

        category.Archive([.. all.Where(candidate => candidate.ParentId == category.Id)], clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ArchiveCategoryOutcome.Archived;
    }
}
