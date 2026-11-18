using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Pfm.Infrastructure.Identity;

namespace Pfm.Api.Identity;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/me", GetMe).WithName("GetMe").WithTags("Identity");
        api.MapPost("/auth/logout", SignOut).WithName("SignOut").WithTags("Identity");

        var auth = api.MapGroup("/auth").WithTags("Identity").AllowAnonymous();
        auth.MapGet("/antiforgery", GetAntiforgeryToken).WithName("GetAntiforgeryToken");
        auth.MapPost("/register", Register).WithName("Register");
        auth.MapPost("/verify-email", VerifyEmail).WithName("VerifyEmail");
        auth.MapPost("/resend-verification", ResendVerification).WithName("ResendVerification");
        auth.MapPost("/login", SignIn).WithName("SignIn");

        return api;
    }

    /// <summary>Problem type returned when the password is right but the email address is not confirmed yet.</summary>
    public const string EmailNotVerified = "urn:pfm:problem:email-not-verified";

    /// <summary>A fresh request token, tied to whoever is signed in. Fetch it again after signing in or out.</summary>
    private static Ok<AntiforgeryTokenResponse> GetAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(new AntiforgeryTokenResponse(tokens.RequestToken!));
    }

    /// <summary>
    /// Answers the same way whether or not the email already has a user, so it cannot be used to find
    /// out who is registered. An existing user is told by email instead, and nothing about them changes.
    /// </summary>
    private static async Task<Results<Accepted, ValidationProblem>> Register(
        RegisterRequest request, UserManager<User> users, IdentityEmails emails, CancellationToken cancellationToken)
    {
        // Validate first: the answer must not depend on whether the email already has a user.
        var errors = await ValidateRegistrationAsync(request, users);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var existing = await users.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            await emails.SendAlreadyRegisteredAsync(existing, cancellationToken);
            return TypedResults.Accepted((string?)null);
        }

        var user = new User { UserName = request.Email, Email = request.Email };
        var created = await users.CreateAsync(user, request.Password);
        if (created.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName"))
        {
            // Registered by a concurrent request a moment ago: answer as for any existing email.
            return TypedResults.Accepted((string?)null);
        }

        if (!created.Succeeded)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = [.. created.Errors.Select(error => error.Description)],
            });
        }

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        await emails.SendVerificationAsync(user, token, cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Dictionary<string, string[]>> ValidateRegistrationAsync(RegisterRequest request, UserManager<User> users)
    {
        var errors = new Dictionary<string, string[]>();

        var email = request.Email?.Trim() ?? "";
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email || address.DisplayName.Length > 0)
        {
            errors["email"] = ["Enter a valid email address."];
        }

        var passwordErrors = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, new User { Email = email }, request.Password ?? "");
            passwordErrors.AddRange(result.Errors.Select(error => error.Description));
        }

        if (passwordErrors.Count > 0)
        {
            errors["password"] = [.. passwordErrors];
        }

        return errors;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> VerifyEmail(VerifyEmailRequest request, UserManager<User> users)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        var confirmed = user is not null && (await users.ConfirmEmailAsync(user, request.Token)).Succeeded;

        return confirmed
            ? TypedResults.NoContent()
            : TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The link is invalid or has expired.");
    }

    /// <summary>Always answers the same way, so it cannot be used to find out which emails have a user.</summary>
    private static async Task<Accepted> ResendVerification(
        ResendVerificationRequest request, UserManager<User> users, IdentityEmails emails, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is { EmailConfirmed: false })
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            await emails.SendVerificationAsync(user, token, cancellationToken);
        }

        return TypedResults.Accepted((string?)null);
    }

    /// <summary>
    /// Checks the password before anything else, so only someone who knows it learns whether the
    /// email address is confirmed. Unknown emails cost a password hash too, so timing reveals nothing.
    /// </summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> SignIn(
        SignInRequest request, UserManager<User> users, SignInManager<User> signIn)
    {
        var user = await users.FindByEmailAsync(request.Email);
        var passwordMatches = user is null
            ? HashAndReject(users, request.Password)
            : await users.CheckPasswordAsync(user, request.Password);

        if (user is null || !passwordMatches)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The email or password is incorrect.");
        }

        if (!user.EmailConfirmed)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                type: EmailNotVerified,
                title: "Confirm your email address before signing in.");
        }

        await signIn.SignInAsync(user, isPersistent: true);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> SignOut(SignInManager<User> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static bool HashAndReject(UserManager<User> users, string password)
    {
        users.PasswordHasher.HashPassword(new User(), password);
        return false;
    }

    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> GetMe(ClaimsPrincipal principal, UserManager<User> users)
    {
        var user = await users.GetUserAsync(principal);
        return user is null ? TypedResults.Unauthorized() : TypedResults.Ok(new MeResponse(user.Id, user.Email!));
    }
}

public sealed record AntiforgeryTokenResponse(string Token);

public sealed record RegisterRequest(string Email, string Password);

public sealed record VerifyEmailRequest(Guid UserId, string Token);

public sealed record ResendVerificationRequest(string Email);

public sealed record SignInRequest(string Email, string Password);

/// <summary>The signed-in user.</summary>
public sealed record MeResponse(Guid Id, string Email);
