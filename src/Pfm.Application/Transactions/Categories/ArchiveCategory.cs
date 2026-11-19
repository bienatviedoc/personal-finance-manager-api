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
        var category = await categories.FindAsync(command.Id, cancellationToken);
        if (category is null)
        {
            return ArchiveCategoryOutcome.NotFound;
        }

        category.Archive(await categories.ListSubcategoriesAsync(category.Id, cancellationToken), clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ArchiveCategoryOutcome.Archived;
    }
}
