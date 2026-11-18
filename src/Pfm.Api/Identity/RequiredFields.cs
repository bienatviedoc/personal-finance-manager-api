namespace Pfm.Api.Identity;

/// <summary>
/// Names the request fields that are missing or empty, for a validation problem. A stopgap until
/// the sign-in use cases move to FluentValidation validators (#52).
/// </summary>
internal static class RequiredFields
{
    public static Dictionary<string, string[]> Missing(params (string Field, bool Present)[] fields) =>
        fields.Where(field => !field.Present).ToDictionary(field => field.Field, _ => new[] { "This field is required." });
}
