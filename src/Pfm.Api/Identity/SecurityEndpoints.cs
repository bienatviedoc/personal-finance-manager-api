using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Identity;

namespace Pfm.Api.Identity;

/// <summary>How the signed-in user signs in: their password, and two-factor sign-in with an authenticator app.</summary>
public static class SecurityEndpoints
{
    public static IEndpointRouteBuilder MapSecurityEndpoints(this IEndpointRouteBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Identity").ProducesProblem(StatusCodes.Status401Unauthorized);
        me.MapGet("/two-factor", GetTwoFactor).WithName("GetTwoFactor");

        // Each of these takes a password or a code, so each is limited like signing in is.
        var guarded = me.MapGroup("").LimitAuthAttempts();
        guarded.MapPost("/password", ChangePassword).WithName("ChangePassword");
        guarded.MapPost("/two-factor/setup", SetUpTwoFactor).WithName("SetUpTwoFactor").ProducesProblem(StatusCodes.Status409Conflict);
        guarded.MapPost("/two-factor/enable", TurnOnTwoFactor).WithName("TurnOnTwoFactor");
        guarded.MapPost("/two-factor/disable", TurnOffTwoFactor).WithName("TurnOffTwoFactor");
        guarded.MapPost("/two-factor/recovery-codes", NewRecoveryCodes).WithName("NewRecoveryCodes").ProducesProblem(StatusCodes.Status409Conflict);
        return api;
    }

    private static async Task<Results<Ok<TwoFactorResponse>, UnauthorizedHttpResult>> GetTwoFactor(GetTwoFactorHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new GetTwoFactor(), cancellationToken) is { } twoFactor
            ? TypedResults.Ok(new TwoFactorResponse(twoFactor.Enabled, twoFactor.RecoveryCodesLeft))
            : TypedResults.Unauthorized();

    /// <summary>The user's other sessions end; the one that made the change carries on.</summary>
    private static async Task<Results<NoContent, ValidationProblem, UnauthorizedHttpResult>> ChangePassword(
        ChangePasswordRequest request, ChangePasswordHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ChangePasswordCommand(request.CurrentPassword, request.NewPassword), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is null ? TypedResults.Unauthorized() : TypedResults.NoContent();
    }

    /// <summary>
    /// The key to add to an authenticator app, also as the link to show as a QR code. Two-factor sign-in stays off
    /// until a code confirms it. Answers 409 when it is on already.
    /// </summary>
    private static async Task<Results<Ok<TwoFactorSetupResponse>, ProblemHttpResult, UnauthorizedHttpResult>> SetUpTwoFactor(
        SetUpTwoFactorHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new SetUpTwoFactorCommand(), cancellationToken);
        return result.Outcome switch
        {
            SetUpTwoFactorOutcome.Ready ready => TypedResults.Ok(new TwoFactorSetupResponse(ready.Key, ready.AuthenticatorUri)),
            SetUpTwoFactorOutcome.AlreadyOn => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Two-factor sign-in is already on."),
            null => TypedResults.Unauthorized(),
            var other => throw new InvalidOperationException($"No answer for {other.GetType().Name}."),
        };
    }

    /// <summary>Turns two-factor sign-in on, and answers the recovery codes: the only time they are shown.</summary>
    private static async Task<Results<Ok<RecoveryCodesResponse>, ValidationProblem, UnauthorizedHttpResult>> TurnOnTwoFactor(
        TurnOnTwoFactorRequest request, TurnOnTwoFactorHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new TurnOnTwoFactorCommand(request.Code), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is { } codes ? TypedResults.Ok(new RecoveryCodesResponse(codes.RecoveryCodes)) : TypedResults.Unauthorized();
    }

    private static async Task<Results<NoContent, ValidationProblem, UnauthorizedHttpResult>> TurnOffTwoFactor(
        TurnOffTwoFactorRequest request, TurnOffTwoFactorHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new TurnOffTwoFactorCommand(request.Password), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is null ? TypedResults.Unauthorized() : TypedResults.NoContent();
    }

    /// <summary>Replaces the recovery codes; the old ones stop working. Answers 409 while two-factor sign-in is off.</summary>
    private static async Task<Results<Ok<RecoveryCodesResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> NewRecoveryCodes(
        NewRecoveryCodesRequest request, NewRecoveryCodesHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new NewRecoveryCodesCommand(request.Password), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            NewRecoveryCodesOutcome.Replaced replaced => TypedResults.Ok(new RecoveryCodesResponse(replaced.Codes.RecoveryCodes)),
            NewRecoveryCodesOutcome.TwoFactorOff => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Two-factor sign-in is off."),
            null => TypedResults.Unauthorized(),
            var other => throw new InvalidOperationException($"No answer for {other.GetType().Name}."),
        };
    }
}

/// <param name="RecoveryCodesLeft">How many recovery codes the user has not used yet.</param>
public sealed record TwoFactorResponse(bool Enabled, int RecoveryCodesLeft);

/// <param name="Key">The key, for typing into an authenticator app by hand.</param>
/// <param name="AuthenticatorUri">The same key as an <c>otpauth://</c> link, for showing as a QR code.</param>
public sealed record TwoFactorSetupResponse(string Key, string AuthenticatorUri);

/// <param name="Code">The six-digit code the authenticator app shows now.</param>
public sealed record TurnOnTwoFactorRequest(string Code);

/// <param name="RecoveryCodes">Each signs the user in once, in place of a code from their authenticator app.</param>
public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

public sealed record TurnOffTwoFactorRequest(string Password);

public sealed record NewRecoveryCodesRequest(string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
