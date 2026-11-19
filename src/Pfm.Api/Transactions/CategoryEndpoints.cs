using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Transactions.Categories;
using Pfm.Domain.Transactions;

namespace Pfm.Api.Transactions;

/// <summary>Categories over HTTP. The use cases decide everything; these only map requests and answers.</summary>
public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder api)
    {
        var categories = api.MapGroup("/categories").WithTags("Categories").ProducesProblem(StatusCodes.Status401Unauthorized);

        categories.MapGet("/", List).WithName("ListCategories")
            .WithDescription("The user's categories as a tree: top-level categories, each with its subcategories. Archived ones, which pickers never offer, only with includeArchived=true.");
        categories.MapPost("/", Create).WithName("CreateCategory")
            .WithDescription("Adds a top-level category, or a subcategory when parentId is set. A subcategory has its parent's kind; categories are at most two levels deep.");
        categories.MapPut("/{id:guid}", Edit).WithName("EditCategory")
            .WithDescription("Renames, recolours or re-icons a category, or changes a top-level category's kind together with its subcategories. The kind cannot change once any of them has transactions.");
        categories.MapDelete("/{id:guid}", Archive).WithName("ArchiveCategory")
            .WithDescription("Archives the category and its subcategories: they leave pickers and stay on the transactions that use them.");

        return api;
    }

    private static async Task<Ok<CategoryListResponse>> List(
        ListCategoriesHandler handler, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var tree = await handler.HandleAsync(new ListCategories(includeArchived), cancellationToken);
        return TypedResults.Ok(new CategoryListResponse([.. tree.Select(CategoryResponse.From)]));
    }

    private static async Task<Results<Created<CategoryResponse>, NotFound, ValidationProblem>> Create(
        CategoryRequest request, CreateCategoryHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CreateCategoryCommand(request.ParentId, request.Name, request.Kind, request.Icon, request.Color), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is { } created ? TypedResults.Created($"/api/v1/categories/{created.Id}", CategoryResponse.From(created)) : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<CategoryResponse>, NotFound, ValidationProblem>> Edit(
        Guid id, CategoryChangeRequest request, EditCategoryHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new EditCategoryCommand(id, request.Name, request.Kind, request.Icon, request.Color), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is { } edited ? TypedResults.Ok(CategoryResponse.From(edited)) : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> Archive(
        Guid id, ArchiveCategoryHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ArchiveCategoryCommand(id), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is null ? TypedResults.NotFound() : TypedResults.NoContent();
    }
}

/// <summary>A new category. Leave <c>parentId</c> out for a top-level category. Colour is <c>#rrggbb</c>; the icon is a key the SPA draws.</summary>
public sealed record CategoryRequest(string Name, CategoryKind Kind, string Icon, string Color, Guid? ParentId = null);

/// <summary>A category's new name, kind, icon and colour.</summary>
public sealed record CategoryChangeRequest(string Name, CategoryKind Kind, string Icon, string Color);

/// <summary>A category; a top-level one comes with its subcategories, a subcategory with none.</summary>
public sealed record CategoryResponse(
    Guid Id, Guid? ParentId, string Name, CategoryKind Kind, string Icon, string Color, bool IsArchived, IReadOnlyList<CategoryResponse> Subcategories)
{
    public static CategoryResponse From(CategoryDetails category) =>
        new(category.Id, category.ParentId, category.Name, category.Kind, category.Icon, category.Color, category.IsArchived,
            [.. category.Subcategories.Select(From)]);
}

/// <summary>The whole category tree, unpaged: a user has dozens of categories, and a tree cannot be cut into pages.</summary>
public sealed record CategoryListResponse(IReadOnlyList<CategoryResponse> Items);
