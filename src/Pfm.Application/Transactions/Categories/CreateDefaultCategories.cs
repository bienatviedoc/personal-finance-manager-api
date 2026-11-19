using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Transactions;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categories;

/// <summary>Gives a newly registered user the default category set, in their language.</summary>
public sealed record CreateDefaultCategories(Guid UserId, Language Language);

public sealed class CreateDefaultCategoriesValidator : AbstractValidator<CreateDefaultCategories>
{
    public CreateDefaultCategoriesValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.Language).IsInEnum();
    }
}

/// <summary>Runs during registration, before anyone is signed in, so it acts for the new user.</summary>
public sealed class CreateDefaultCategoriesHandler(ICategoryRepository categories, IActForUser actFor) : IDefaultCategories
{
    public Task CreateForNewUserAsync(Guid userId, Language language, CancellationToken cancellationToken) =>
        HandleAsync(new CreateDefaultCategories(userId, language), cancellationToken);

    public async Task HandleAsync(CreateDefaultCategories command, CancellationToken cancellationToken)
    {
        using var _ = actFor.ActFor(command.UserId);
        foreach (var category in DefaultCategories.For(command.Language))
        {
            categories.Add(category);
        }

        await categories.SaveChangesAsync(cancellationToken);
    }
}
