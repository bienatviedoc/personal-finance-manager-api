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
        return api;
    }

    private static async Task<Results<Ok<UserProfile>, UnauthorizedHttpResult>> GetMe(GetProfileHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(cancellationToken) is { } profile ? TypedResults.Ok(profile) : TypedResults.Unauthorized();
}
