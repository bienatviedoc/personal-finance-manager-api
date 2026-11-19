using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categories;

/// <summary>Gives a newly registered user the default category set, in their language.</summary>
public sealed record CreateDefaultCategoriesCommand(Guid UserId, Language Language);

public sealed class CreateDefaultCategoriesValidator : AbstractValidator<CreateDefaultCategoriesCommand>
{
    public CreateDefaultCategoriesValidator()
    {
        RuleFor(command => command.UserId).Required();
        RuleFor(command => command.Language).IsInEnum();
    }
}

/// <summary>Runs during registration, before anyone is signed in, so it acts for the new user.</summary>
public sealed class CreateDefaultCategoriesHandler(
    IValidator<CreateDefaultCategoriesCommand> validator, ICategoryRepository categories, IActForUser actFor, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<CreateDefaultCategoriesCommand, Done>(validator), IDefaultCategories
{
    public async Task CreateForNewUserAsync(Guid userId, Language language, CancellationToken cancellationToken)
    {
        var result = await HandleAsync(new CreateDefaultCategoriesCommand(userId, language), cancellationToken);
        if (!result.IsValid)
        {
            throw new ArgumentException($"Cannot create default categories: {string.Join(" ", result.Errors.SelectMany(error => error.Value))}");
        }
    }

    protected override async Task<Result<Done>> ExecuteAsync(CreateDefaultCategoriesCommand command, CancellationToken cancellationToken)
    {
        using var _ = actFor.ActFor(command.UserId);
        foreach (var category in DefaultCategories.For(command.Language, clock.GetUtcNow()))
        {
            categories.Add(category);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Done();
    }
}
