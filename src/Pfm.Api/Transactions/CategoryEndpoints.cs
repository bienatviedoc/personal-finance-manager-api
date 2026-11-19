using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Transactions.Categories;
using Pfm.Domain.Transactions;

namespace Pfm.Api.Transactions;

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder api)
    {
        // Every endpoint here can also answer 401 (not signed in) and, for changes, 400 (anti-forgery).
        var categories = api.MapGroup("/categories").WithTags("Categories")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .AddEndpointFilter(RefuseRuleViolations);

        categories.MapGet("/", List).WithName("ListCategories");
        categories.MapPost("/", Create).WithName("CreateCategory");
        categories.MapPut("/{id:guid}", Update).WithName("UpdateCategory");
        categories.MapDelete("/{id:guid}", Archive).WithName("ArchiveCategory");

        return api;
    }

    /// <summary>
    /// The user's categories as a tree: top-level categories, each with its subcategories. Archived
    /// categories, which pickers never offer, are left out unless <paramref name="includeArchived"/> is set.
    /// </summary>
    private static async Task<Ok<IReadOnlyList<CategoryView>>> List(
        ListCategoriesHandler handler, CancellationToken cancellationToken, bool includeArchived = false) =>
        TypedResults.Ok(await handler.HandleAsync(new ListCategories(includeArchived), cancellationToken));

    /// <summary>Adds a top-level category, or a subcategory when <c>parentId</c> is set. A subcategory has its parent's kind.</summary>
    private static async Task<Results<Created<CategoryView>, NotFound, ValidationProblem>> Create(
        CategoryRequest request, IValidator<CreateCategory> validator, CreateCategoryHandler handler, CancellationToken cancellationToken)
    {
        var command = new CreateCategory(request.ParentId, request.Name, request.Kind, request.Icon, request.Color);
        if (await validator.ProblemAsync(command, cancellationToken) is { } problem)
        {
            return problem;
        }

        var created = await handler.HandleAsync(command, cancellationToken);
        return created is null ? TypedResults.NotFound() : TypedResults.Created($"/api/v1/categories/{created.Id}", created);
    }

    /// <summary>
    /// Renames, recolours or re-icons a category, or changes its kind. Changing a top-level category's kind
    /// changes its subcategories too, and is refused once any of them has transactions.
    /// </summary>
    private static async Task<Results<Ok<CategoryView>, NotFound, ValidationProblem>> Update(
        Guid id, CategoryChangeRequest request, IValidator<UpdateCategory> validator, UpdateCategoryHandler handler, CancellationToken cancellationToken)
    {
        var command = new UpdateCategory(id, request.Name, request.Kind, request.Icon, request.Color);
        if (await validator.ProblemAsync(command, cancellationToken) is { } problem)
        {
            return problem;
        }

        var updated = await handler.HandleAsync(command, cancellationToken);
        return updated is null ? TypedResults.NotFound() : TypedResults.Ok(updated);
    }

    /// <summary>Archives the category and its subcategories: gone from pickers, still on existing transactions.</summary>
    private static async Task<Results<NoContent, NotFound, ValidationProblem>> Archive(
        Guid id, IValidator<ArchiveCategory> validator, ArchiveCategoryHandler handler, CancellationToken cancellationToken)
    {
        var command = new ArchiveCategory(id);
        if (await validator.ProblemAsync(command, cancellationToken) is { } problem)
        {
            return problem;
        }

        return await handler.HandleAsync(command, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    /// <summary>A change that breaks a category rule is refused like invalid input, naming the field at fault.</summary>
    private static async ValueTask<object?> RefuseRuleViolations(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (CategoryRuleViolation violation)
        {
            return Validation.Problem(violation.Property, violation.Message);
        }
    }
}

/// <summary>A new category. Leave <c>parentId</c> out for a top-level category.</summary>
public sealed record CategoryRequest(Guid? ParentId, string Name, CategoryKind Kind, string Icon, string Color);

/// <summary>A category's new name, kind, icon and colour.</summary>
public sealed record CategoryChangeRequest(string Name, CategoryKind Kind, string Icon, string Color);
