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

/// <summary>Answers null when the user has no category with that id.</summary>
public sealed class ArchiveCategoryHandler(
    IValidator<ArchiveCategoryCommand> validator, ICategoryRepository categories, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<ArchiveCategoryCommand, Done?>(validator)
{
    protected override async Task<Result<Done?>> ExecuteAsync(ArchiveCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await categories.FindAsync(command.Id, cancellationToken);
        if (category is null)
        {
            return (Done?)null;
        }

        category.Archive(await categories.ListSubcategoriesAsync(category.Id, cancellationToken), clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Done();
    }
}
