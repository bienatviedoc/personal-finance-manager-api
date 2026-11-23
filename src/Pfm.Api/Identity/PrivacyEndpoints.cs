using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Identity;

namespace Pfm.Api.Identity;

/// <summary>
/// What a user may do with everything the app holds about them: download it, and delete themselves (spec section 8,
/// Privacy).
/// </summary>
public static class PrivacyEndpoints
{
    public static IEndpointRouteBuilder MapPrivacyEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/export/all.zip", ExportData).WithName("ExportData").WithTags("Export")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // Takes a password, so it is limited like signing in is.
        api.MapPost("/me/deletion", DeleteUser).WithName("DeleteUser").WithTags("Identity")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .LimitAuthAttempts();
        return api;
    }

    /// <summary>
    /// Everything the app holds about the signed-in user, as a ZIP archive to download: <c>profile.json</c> and
    /// one CSV file for each kind of data.
    /// </summary>
    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult>> ExportData(ExportDataHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new ExportData(), cancellationToken) is { } export
            ? TypedResults.File(export.Zip, "application/zip", export.FileName)
            : TypedResults.Unauthorized();

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
