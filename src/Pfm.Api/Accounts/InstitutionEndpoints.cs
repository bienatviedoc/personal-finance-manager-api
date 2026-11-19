using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Accounts.Institutions;
using Pfm.Application.UseCases;

namespace Pfm.Api.Accounts;

/// <summary>Institutions over HTTP. The use cases decide everything; these only map requests and answers.</summary>
public static class InstitutionEndpoints
{
    public static IEndpointRouteBuilder MapInstitutionEndpoints(this IEndpointRouteBuilder api)
    {
        var institutions = api.MapGroup("/institutions").WithTags("Institutions").ProducesProblem(StatusCodes.Status401Unauthorized);

        institutions.MapGet("/", List).WithName("ListInstitutions");
        institutions.MapGet("/{id:guid}", Get).WithName("GetInstitution");
        institutions.MapPost("/", Create).WithName("CreateInstitution");
        institutions.MapPut("/{id:guid}", Edit).WithName("EditInstitution");
        institutions.MapDelete("/{id:guid}", Archive).WithName("ArchiveInstitution")
            .WithDescription("Archives the institution: it leaves pickers and stays on the accounts that use it.");

        return api;
    }

    /// <param name="limit">How many institutions a page holds: 1 to 100, 50 when left out.</param>
    /// <param name="cursor">The <c>nextCursor</c> of the page before; left out for the first page.</param>
    private static async Task<Results<Ok<InstitutionListResponse>, ValidationProblem>> List(
        ListInstitutionsHandler handler, CancellationToken cancellationToken,
        bool includeArchived = false, int limit = Paging.DefaultLimit, string? cursor = null)
    {
        if (!Paging.IsValidLimit(limit))
        {
            return Paged.Invalid("limit", $"Ask for 1 to {Paging.MaxLimit} at a time.");
        }

        NameCursor? after = null;
        if (cursor is not null && !NameCursor.TryParse(cursor, out after))
        {
            return Paged.Invalid("cursor", "Use the nextCursor of the page before.");
        }

        var page = await handler.HandleAsync(new ListInstitutions(includeArchived, limit, after), cancellationToken);
        return TypedResults.Ok(new InstitutionListResponse([.. page.Items.Select(InstitutionResponse.From)], page.NextCursor));
    }

    private static async Task<Results<Ok<InstitutionResponse>, NotFound>> Get(Guid id, GetInstitutionHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new GetInstitution(id), cancellationToken) is { } institution
            ? TypedResults.Ok(InstitutionResponse.From(institution))
            : TypedResults.NotFound();

    private static async Task<Results<Created<InstitutionResponse>, ValidationProblem>> Create(
        InstitutionRequest request, CreateInstitutionHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CreateInstitutionCommand(request.Name, request.Color, request.LogoKey), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return TypedResults.Created($"/api/v1/institutions/{result.Outcome.Id}", InstitutionResponse.From(result.Outcome));
    }

    private static async Task<Results<Ok<InstitutionResponse>, NotFound, ValidationProblem>> Edit(
        Guid id, InstitutionRequest request, EditInstitutionHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new EditInstitutionCommand(id, request.Name, request.Color, request.LogoKey), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is { } edited ? TypedResults.Ok(InstitutionResponse.From(edited)) : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> Archive(
        Guid id, ArchiveInstitutionHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ArchiveInstitutionCommand(id), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is null ? TypedResults.NotFound() : TypedResults.NoContent();
    }
}

/// <summary>What creating or editing an institution sends. Colour is <c>#rrggbb</c>; the logo is one of the logo keys on offer.</summary>
public sealed record InstitutionRequest(string Name, string? Color = null, string? LogoKey = null);

public sealed record InstitutionResponse(Guid Id, string Name, string? Color, string? LogoKey, bool IsArchived)
{
    public static InstitutionResponse From(InstitutionDetails institution) =>
        new(institution.Id, institution.Name, institution.Color, institution.LogoKey, institution.IsArchived);
}

/// <summary>A page of institutions. <c>nextCursor</c> asks for the next page; it is null on the last.</summary>
public sealed record InstitutionListResponse(IReadOnlyList<InstitutionResponse> Items, string? NextCursor);
