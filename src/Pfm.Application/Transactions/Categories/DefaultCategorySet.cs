using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Transactions;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categories;

/// <summary>
/// Gives a newly registered user the default category set, in their language. Runs during registration,
/// before anyone is signed in, so it acts for the new user.
/// </summary>
public sealed class DefaultCategorySet(ICategoryRepository categories, IActForUser actFor, IUnitOfWork unitOfWork, TimeProvider clock)
    : IDefaultCategories
{
    public async Task CreateForNewUserAsync(Guid userId, Language language, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);

        using var _ = actFor.ActFor(userId);
        foreach (var category in DefaultCategories.For(language, clock.GetUtcNow()))
        {
            categories.Add(category);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
