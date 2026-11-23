using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Identity;

namespace Pfm.Api.Identity;

/// <summary>What a user may do with everything the app holds about them: delete themselves (spec section 8, Privacy).</summary>
public static class PrivacyEndpoints
{
    public static IEndpointRouteBuilder MapPrivacyEndpoints(this IEndpointRouteBuilder api)
    {
        // Takes a password, so it is limited like signing in is.
        api.MapPost("/me/deletion", DeleteUser).WithName("DeleteUser").WithTags("Identity")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .LimitAuthAttempts();
        return api;
    }

    /// <summary>
    /// Deletes the signed-in user: they are signed out everywhere and deactivated at once. Signing in again
    /// within 30 days cancels the deletion; after 30 days everything of theirs is erased.
    /// </summary>
    private static async Task<Results<NoContent, ValidationProblem, UnauthorizedHttpResult>> DeleteUser(
        DeleteUserRequest request, DeleteUserHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DeleteUserCommand(request.Password), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is null ? TypedResults.Unauthorized() : TypedResults.NoContent();
    }
}

/// <param name="Password">The user's password: a session left open is not enough to delete them.</param>
public sealed record DeleteUserRequest(string Password);
