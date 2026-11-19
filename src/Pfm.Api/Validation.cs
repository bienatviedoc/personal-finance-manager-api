using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Pfm.Api;

/// <summary>Turns a command's validation failures into a validation problem, with errors keyed by request field.</summary>
internal static class Validation
{
    public static async Task<ValidationProblem?> ProblemAsync<T>(this IValidator<T> validator, T command, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(command, cancellationToken);
        return result.IsValid ? null : TypedResults.ValidationProblem(ByField(result.Errors));
    }

    public static ValidationProblem Problem(string property, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [FieldName(property)] = [message] });

    private static Dictionary<string, string[]> ByField(IEnumerable<ValidationFailure> failures) =>
        failures.GroupBy(failure => FieldName(failure.PropertyName))
            .ToDictionary(field => field.Key, field => field.Select(failure => failure.ErrorMessage).Distinct().ToArray());

    /// <summary>A command property's name as the request spells it: camelCase.</summary>
    private static string FieldName(string property) =>
        property.Length == 0 ? property : char.ToLowerInvariant(property[0]) + property[1..];
}
