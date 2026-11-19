using System.Text.Json;
using FluentValidation;

namespace Pfm.Api.Validation;

/// <summary>
/// Turns a command that fails its validator into a 400 validation problem, with errors keyed by the
/// request field (<c>name</c>, <c>color</c>, ...). A rejected command is the caller's mistake, not an
/// error, so it is answered here rather than logged and reported by the exception handler.
/// </summary>
public static class ValidationProblems
{
    public static TBuilder AnswerValidationFailures<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                return await next(context);
            }
            catch (ValidationException exception)
            {
                return TypedResults.ValidationProblem(ByField(exception));
            }
        });

    private static Dictionary<string, string[]> ByField(ValidationException exception) =>
        exception.Errors
            .GroupBy(error => JsonNamingPolicy.CamelCase.ConvertName(error.PropertyName))
            .ToDictionary(field => field.Key, field => field.Select(error => error.ErrorMessage).Distinct().ToArray());
}
