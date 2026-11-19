using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Identity;
using Pfm.Domain;

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
        UpdateProfileRequest request, UpdateProfileHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new UpdateProfileCommand(request.BaseCurrency, request.TimeZone, request.Locale), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is { } profile ? TypedResults.Ok(ProfileResponse.From(profile)) : TypedResults.Unauthorized();
    }
}

/// <summary>The settings to change. Leave one out (or null) to keep it as it is.</summary>
/// <param name="BaseCurrency">An ISO 4217 code the app supports, such as <c>VND</c>.</param>
/// <param name="TimeZone">An IANA time zone id, such as <c>Asia/Ho_Chi_Minh</c>.</param>
/// <param name="Locale">A language tag: <c>en</c> or <c>vi</c>.</param>
public sealed record UpdateProfileRequest(string? BaseCurrency = null, string? TimeZone = null, string? Locale = null);

/// <summary>The signed-in user and their settings.</summary>
/// <param name="TimeZone">An IANA time zone id.</param>
/// <param name="Locale">Null until the user picks one; until then the interface follows their browser.</param>
public sealed record ProfileResponse(Guid Id, string Email, Currency BaseCurrency, string TimeZone, Locale? Locale)
{
    public static ProfileResponse From(ProfileDetails profile) =>
        new(profile.Id, profile.Email, profile.BaseCurrency, profile.TimeZone, profile.Locale);
}
