using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Accounts.Institutions;

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

    private static async Task<Ok<InstitutionListResponse>> List(
        ListInstitutionsHandler handler, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var found = await handler.HandleAsync(new ListInstitutions(includeArchived), cancellationToken);
        return TypedResults.Ok(new InstitutionListResponse([.. found.Select(InstitutionResponse.From)]));
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

public sealed record InstitutionListResponse(IReadOnlyList<InstitutionResponse> Items);
