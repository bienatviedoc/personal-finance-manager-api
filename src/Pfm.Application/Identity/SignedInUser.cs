using Pfm.Application.Abstractions;

namespace Pfm.Application.Identity;

/// <summary>Who a use case about the signed-in user's own sign-in acts for.</summary>
internal static class SignedInUser
{
    /// <summary>The signed-in user; null when nobody is signed in, or their user is gone.</summary>
    public static async Task<UserDetails?> FindSignedInAsync(this IUsers users, ICurrentUser currentUser) =>
        currentUser.UserId is { } userId ? await users.FindByIdAsync(userId) : null;
}
