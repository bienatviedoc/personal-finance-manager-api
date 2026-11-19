using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.UseCases;

namespace Pfm.Api;

/// <summary>Turns a use case's validation errors into Problem Details keyed by the request's field names.</summary>
public static class ValidationProblems
{
    /// <summary>
    /// A 400 naming each field to fix. Errors arrive keyed by command property (<c>UserId</c>) and
    /// leave keyed by JSON field (<c>userId</c>), segment by segment for nested ones.
    /// </summary>
    public static ValidationProblem For<TOutcome>(Result<TOutcome> result) =>
        TypedResults.ValidationProblem(result.Errors.ToDictionary(error => FieldName(error.Key), error => error.Value));

    private static string FieldName(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}
