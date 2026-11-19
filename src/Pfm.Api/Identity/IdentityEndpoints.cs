using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pfm.Application.Transactions.Categories;
using Pfm.Domain;
using Pfm.Infrastructure.Identity;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Api.Identity;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder api)
    {
        // Every endpoint here can also answer 401 (not signed in) and, for POSTs, 400 (anti-forgery).
        api.MapGet("/me", GetMe).WithName("GetMe").WithTags("Identity").ProducesProblem(StatusCodes.Status401Unauthorized);
        api.MapPost("/auth/logout", SignOut).WithName("SignOut").WithTags("Identity").ProducesProblem(StatusCodes.Status401Unauthorized);

        var auth = api.MapGroup("/auth").WithTags("Identity").AllowAnonymous();
        auth.MapGet("/antiforgery", GetAntiforgeryToken).WithName("GetAntiforgeryToken");
        auth.MapPost("/register", Register).WithName("Register");
        auth.MapPost("/verify-email", VerifyEmail).WithName("VerifyEmail").ProducesProblem(StatusCodes.Status400BadRequest);
        auth.MapPost("/resend-verification", ResendVerification).WithName("ResendVerification");
        auth.MapPost("/login", SignIn).WithName("SignIn")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

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
        RegisterRequest request, UserManager<User> users, IdentityEmails emails, PfmDbContext db,
        CreateDefaultCategoriesHandler defaultCategories, CancellationToken cancellationToken)
    {
        request = request with { Email = request.Email?.Trim()! };

        // Validate first: the answer must not depend on whether the email already has a user.
        var errors = await ValidateRegistrationAsync(request, users);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var existing = await users.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            return await AlreadyRegisteredAsync(existing, request.Password, users, emails, cancellationToken);
        }

        var user = new User { UserName = request.Email, Email = request.Email };
        var created = await CreateWithDefaultCategoriesAsync(user, request, users, db, defaultCategories, cancellationToken);

        if (created.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName")
            && await users.FindByEmailAsync(request.Email) is { } registeredMeanwhile)
        {
            return await AlreadyRegisteredAsync(registeredMeanwhile, request.Password, users, emails, cancellationToken);
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

    /// <summary>
    /// Creates the user and their default categories together: either both exist afterwards or neither does.
    /// </summary>
    private static async Task<IdentityResult> CreateWithDefaultCategoriesAsync(
        User user, RegisterRequest request, UserManager<User> users, PfmDbContext db,
        CreateDefaultCategoriesHandler defaultCategories, CancellationToken cancellationToken)
    {
        // Disposing without committing rolls back, including after a failed statement.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        IdentityResult created;
        try
        {
            created = await users.CreateAsync(user, request.Password);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request registered this email a moment ago: answer as for any existing email.
            return IdentityResult.Failed(new IdentityError { Code = "DuplicateEmail" });
        }

        if (created.Succeeded)
        {
            await defaultCategories.HandleAsync(new CreateDefaultCategories(user.Id, LanguageOf(request)), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return created;
    }

    /// <summary>The languages a user can register in, by the code the SPA sends. English when none is sent.</summary>
    private static readonly Dictionary<string, Language> Languages = new() { ["en"] = Language.English, ["vi"] = Language.Vietnamese };

    private static Language LanguageOf(RegisterRequest request) => request.Language is null ? Language.English : Languages[request.Language];

    /// <summary>
    /// The answer for an email that already has a user: the same as for a new one, and as slow, since a
    /// new registration hashes the password. The user is told by email; nothing about them changes.
    /// </summary>
    private static async Task<Accepted> AlreadyRegisteredAsync(
        User existing, string password, UserManager<User> users, IdentityEmails emails, CancellationToken cancellationToken)
    {
        users.PasswordHasher.HashPassword(existing, password);
        await emails.SendAlreadyRegisteredAsync(existing, cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Dictionary<string, string[]>> ValidateRegistrationAsync(RegisterRequest request, UserManager<User> users)
    {
        var errors = new Dictionary<string, string[]>();

        var email = request.Email ?? "";
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

        if (request.Language is not null && !Languages.ContainsKey(request.Language))
        {
            errors["language"] = ["Choose English (en) or Vietnamese (vi)."];
        }

        return errors;
    }

    private static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> VerifyEmail(VerifyEmailRequest request, UserManager<User> users)
    {
        var missing = RequiredFields.Missing(("userId", request.UserId != Guid.Empty), ("token", !string.IsNullOrEmpty(request.Token)));
        if (missing.Count > 0)
        {
            return TypedResults.ValidationProblem(missing);
        }

        var user = await users.FindByIdAsync(request.UserId.ToString());
        var confirmed = user is not null && (await users.ConfirmEmailAsync(user, request.Token)).Succeeded;

        return confirmed
            ? TypedResults.NoContent()
            : TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The link is invalid or has expired.");
    }

    /// <summary>Always answers the same way, so it cannot be used to find out which emails have a user.</summary>
    private static async Task<Results<Accepted, ValidationProblem>> ResendVerification(
        ResendVerificationRequest request, UserManager<User> users, IdentityEmails emails, CancellationToken cancellationToken)
    {
        request = request with { Email = request.Email?.Trim()! };
        var missing = RequiredFields.Missing(("email", !string.IsNullOrWhiteSpace(request.Email)));
        if (missing.Count > 0)
        {
            return TypedResults.ValidationProblem(missing);
        }

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
    private static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> SignIn(
        SignInRequest request, UserManager<User> users, SignInManager<User> signIn)
    {
        request = request with { Email = request.Email?.Trim()! };
        var missing = RequiredFields.Missing(("email", !string.IsNullOrWhiteSpace(request.Email)), ("password", !string.IsNullOrEmpty(request.Password)));
        if (missing.Count > 0)
        {
            return TypedResults.ValidationProblem(missing);
        }

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

/// <param name="Language">The language for the new user's default categories: <c>en</c> or <c>vi</c>. English when left out.</param>
public sealed record RegisterRequest(string Email, string Password, string? Language = null);

public sealed record VerifyEmailRequest(Guid UserId, string Token);

public sealed record ResendVerificationRequest(string Email);

public sealed record SignInRequest(string Email, string Password);

/// <summary>The signed-in user.</summary>
public sealed record MeResponse(Guid Id, string Email);
