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
        var created = await handler.HandleAsync(new CreateInstitution(request.Name, request.Color, request.LogoKey), cancellationToken);
        return TypedResults.Created($"/api/v1/institutions/{created.Id}", InstitutionResponse.From(created));
    }

    private static async Task<Results<Ok<InstitutionResponse>, NotFound, ValidationProblem>> Edit(
        Guid id, InstitutionRequest request, EditInstitutionHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new EditInstitution(id, request.Name, request.Color, request.LogoKey), cancellationToken) is { } edited
            ? TypedResults.Ok(InstitutionResponse.From(edited))
            : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> Archive(Guid id, ArchiveInstitutionHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new ArchiveInstitution(id), cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();
}

/// <summary>What creating or editing an institution sends. Colour is <c>#rrggbb</c>; the logo is one of the logo keys on offer.</summary>
public sealed record InstitutionRequest(string Name, string? Color = null, string? LogoKey = null);

public sealed record InstitutionResponse(Guid Id, string Name, string? Color, string? LogoKey, bool IsArchived)
{
    public static InstitutionResponse From(InstitutionDetails institution) =>
        new(institution.Id, institution.Name, institution.Color, institution.LogoKey, institution.IsArchived);
}

public sealed record InstitutionListResponse(IReadOnlyList<InstitutionResponse> Items);
