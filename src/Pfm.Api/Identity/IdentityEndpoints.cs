using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Pfm.Application.Identity;

namespace Pfm.Api.Identity;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder api)
    {
        // Every endpoint here can also answer 401 (not signed in) and, for POSTs, 400 (anti-forgery).
        api.MapPost("/auth/logout", SignOut).WithName("SignOut").WithTags("Identity").ProducesProblem(StatusCodes.Status401Unauthorized);

        var auth = api.MapGroup("/auth").WithTags("Identity").AllowAnonymous();
        auth.MapGet("/antiforgery", GetAntiforgeryToken).WithName("GetAntiforgeryToken");
        auth.MapPost("/register", Register).WithName("Register");
        auth.MapPost("/verify-email", VerifyEmail).WithName("VerifyEmail").ProducesProblem(StatusCodes.Status400BadRequest);
        auth.MapPost("/resend-verification", ResendVerification).WithName("ResendVerification");
        auth.MapPost("/forgot-password", RequestPasswordReset).WithName("RequestPasswordReset");
        // Its 400 is a validation problem, or a problem of type ResetLinkInvalid with no errors.
        auth.MapPost("/reset-password", ResetPassword).WithName("ResetPassword");
        auth.MapPost("/login", SignIn).WithName("SignIn")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return api;
    }

    /// <summary>Problem type returned when the password is right but the email address is not confirmed yet.</summary>
    public const string EmailNotVerified = "urn:pfm:problem:email-not-verified";

    /// <summary>
    /// Problem type returned when a password reset link has been tampered with, used or has expired, so the
    /// screen can tell it apart from a password to fix.
    /// </summary>
    public const string ResetLinkInvalid = "urn:pfm:problem:reset-link-invalid";

    /// <summary>A fresh request token, tied to whoever is signed in. Fetch it again after signing in or out.</summary>
    private static Ok<AntiforgeryTokenResponse> GetAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(new AntiforgeryTokenResponse(tokens.RequestToken!));
    }

    // Each endpoint maps its request to a use case in Pfm.Application.Identity and the outcome to a response.

    /// <summary>
    /// Answers the same way whether or not the email already has a user, so it cannot be used to find
    /// out who is registered. An existing user is told by email instead.
    /// </summary>
    private static async Task<Results<Accepted, ValidationProblem>> Register(
        RegisterRequest request, RegisterHandler register, CancellationToken cancellationToken)
    {
        var result = await register.HandleAsync(new RegisterCommand(request.Email, request.Password, request.Locale), cancellationToken);
        return result.IsValid ? TypedResults.Accepted((string?)null) : ValidationProblems.For(result);
    }

    private static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> VerifyEmail(
        VerifyEmailRequest request, VerifyEmailHandler verify, CancellationToken cancellationToken)
    {
        var result = await verify.HandleAsync(new VerifyEmailCommand(request.UserId, request.Token), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            VerifyEmailOutcome.Confirmed => TypedResults.NoContent(),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The link is invalid or has expired."),
        };
    }

    /// <summary>Always answers the same way, so it cannot be used to find out which emails have a user.</summary>
    private static async Task<Results<Accepted, ValidationProblem>> ResendVerification(
        ResendVerificationRequest request, ResendVerificationHandler resend, CancellationToken cancellationToken)
    {
        var result = await resend.HandleAsync(new ResendVerificationCommand(request.Email), cancellationToken);
        return result.IsValid ? TypedResults.Accepted((string?)null) : ValidationProblems.For(result);
    }

    /// <summary>Always answers the same way, so it cannot be used to find out which emails have a user.</summary>
    private static async Task<Results<Accepted, ValidationProblem>> RequestPasswordReset(
        RequestPasswordResetRequest request, RequestPasswordResetHandler requestReset, CancellationToken cancellationToken)
    {
        var result = await requestReset.HandleAsync(new RequestPasswordResetCommand(request.Email), cancellationToken);
        return result.IsValid ? TypedResults.Accepted((string?)null) : ValidationProblems.For(result);
    }

    private static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> ResetPassword(
        ResetPasswordRequest request, ResetPasswordHandler reset, CancellationToken cancellationToken)
    {
        var result = await reset.HandleAsync(new ResetPasswordCommand(request.UserId, request.Token, request.Password), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            ResetPasswordOutcome.PasswordChanged => TypedResults.NoContent(),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, type: ResetLinkInvalid, title: "The link is invalid, has been used or has expired."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> SignIn(
        SignInRequest request, SignInHandler signIn, CancellationToken cancellationToken)
    {
        var result = await signIn.HandleAsync(new SignInCommand(request.Email, request.Password), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            SignInOutcome.SignedIn => TypedResults.NoContent(),
            SignInOutcome.EmailNotConfirmed => TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                type: EmailNotVerified,
                title: "Confirm your email address before signing in."),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The email or password is incorrect."),
        };
    }

    private static async Task<NoContent> SignOut(SignOutHandler signOut, CancellationToken cancellationToken)
    {
        await signOut.HandleAsync(new SignOutCommand(), cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed record AntiforgeryTokenResponse(string Token);

/// <param name="Locale">
/// The language tag the user registers in, <c>en</c> or <c>vi</c>: saved as their locale, and the language of their default
/// categories. When left out, the user has no locale yet and the categories are in English.
/// </param>
public sealed record RegisterRequest(string Email, string Password, string? Locale = null);

public sealed record VerifyEmailRequest(Guid UserId, string Token);

public sealed record ResendVerificationRequest(string Email);

public sealed record RequestPasswordResetRequest(string Email);

public sealed record ResetPasswordRequest(Guid UserId, string Token, string Password);

public sealed record SignInRequest(string Email, string Password);

