using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Identity;
using Pfm.Domain;
using Pfm.Domain.Identity;

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

    private static async Task<Results<Ok<ProfileResponse>, UnauthorizedHttpResult>> GetMe(GetProfileHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new GetProfile(), cancellationToken) is { } profile ? TypedResults.Ok(ProfileResponse.From(profile)) : TypedResults.Unauthorized();

    /// <summary>Changes the settings sent; any left out stay as they were.</summary>
    private static async Task<Results<Ok<ProfileResponse>, ValidationProblem, UnauthorizedHttpResult>> UpdateMe(
        UpdateProfileCommand command, UpdateProfileHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is { } profile ? TypedResults.Ok(ProfileResponse.From(profile)) : TypedResults.Unauthorized();
    }
}

/// <summary>The signed-in user and their settings.</summary>
/// <param name="TimeZone">An IANA time zone id.</param>
/// <param name="Locale">Null until the user picks one; until then the interface follows their browser.</param>
public sealed record ProfileResponse(Guid Id, string Email, Currency BaseCurrency, string TimeZone, Locale? Locale)
{
    public static ProfileResponse From(ProfileDetails profile) =>
        new(profile.Id, profile.Email, profile.BaseCurrency, profile.TimeZone, profile.Locale);
}
