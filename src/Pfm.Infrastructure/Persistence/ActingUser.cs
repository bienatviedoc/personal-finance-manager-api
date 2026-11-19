using Pfm.Application.Abstractions;

namespace Pfm.Infrastructure.Persistence;

/// <summary>
/// The user a flow acts for while nobody is signed in on it (see <see cref="IActForUser"/>). One per scope;
/// <see cref="PfmDbContext"/> treats them as the signed-in user.
/// </summary>
public sealed class ActingUser : IActForUser
{
    public Guid? UserId { get; private set; }

    public IDisposable ActFor(Guid userId)
    {
        var previous = UserId;
        UserId = userId;
        return new Restore(this, previous);
    }

    private sealed class Restore(ActingUser acting, Guid? previous) : IDisposable
    {
        public void Dispose() => acting.UserId = previous;
    }
}
