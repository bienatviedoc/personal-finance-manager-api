using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Identity;

namespace Pfm.Api.Identity;

/// <summary>The signed-in user's profile: who they are, their base currency, time zone and locale.</summary>
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Identity").ProducesProblem(StatusCodes.Status401Unauthorized);
        me.MapGet("", GetMe).WithName("GetMe");
        me.MapPatch("", UpdateMe).WithName("UpdateMe").ProducesValidationProblem();
        return api;
    }

    private static async Task<Results<Ok<UserProfile>, UnauthorizedHttpResult>> GetMe(GetProfileHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(cancellationToken) is { } profile ? TypedResults.Ok(profile) : TypedResults.Unauthorized();

    /// <summary>Changes the settings sent; any left out stay as they were.</summary>
    private static async Task<Results<Ok<UserProfile>, ValidationProblem, UnauthorizedHttpResult>> UpdateMe(
        UpdateProfileCommand command, UpdateProfileHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is { } profile ? TypedResults.Ok(profile) : TypedResults.Unauthorized();
    }
}
