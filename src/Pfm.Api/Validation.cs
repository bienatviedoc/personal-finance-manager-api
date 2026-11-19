using System.Text.Json;
using FluentValidation;

namespace Pfm.Api;

/// <summary>Runs a command's FluentValidation validator before its endpoint, so endpoints only map HTTP.</summary>
public static class Validation
{
    /// <summary>
    /// Validates the endpoint's <typeparamref name="TCommand"/> argument with its validator. An invalid one is
    /// answered with a 400 validation problem that names each field as the JSON body spells it; the
    /// endpoint does not run.
    /// </summary>
    public static RouteHandlerBuilder Validates<TCommand>(this RouteHandlerBuilder endpoint) =>
        endpoint
            .AddEndpointFilter(async (context, next) =>
            {
                var command = context.Arguments.OfType<TCommand>().Single();
                var validator = context.HttpContext.RequestServices.GetRequiredService<IValidator<TCommand>>();
                var result = await validator.ValidateAsync(command, context.HttpContext.RequestAborted);
                if (result.IsValid)
                {
                    return await next(context);
                }

                var errors = result.Errors
                    .GroupBy(error => JsonName(error.PropertyName))
                    .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());
                return TypedResults.ValidationProblem(errors);
            })
            .ProducesValidationProblem();

    private static string JsonName(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}
