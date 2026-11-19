namespace Pfm.Application.Abstractions;

/// <summary>Who the current request or job acts for. Null when no user is signed in.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
}

/// <summary>For hosts and jobs that act for no user: they see no user-owned data.</summary>
public sealed class NoCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
}
