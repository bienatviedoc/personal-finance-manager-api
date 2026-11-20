namespace Pfm.Application.UseCases;

/// <summary>One field to fix, as the errors a <see cref="Result{TOutcome}"/> carries.</summary>
public static class FieldError
{
    /// <param name="property">The command property at fault.</param>
    public static IReadOnlyDictionary<string, string[]> On(string property, string message) =>
        new Dictionary<string, string[]> { [property] = [message] };
}
