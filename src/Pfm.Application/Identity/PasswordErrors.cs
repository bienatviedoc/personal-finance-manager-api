using Pfm.Application.UseCases;

namespace Pfm.Application.Identity;

/// <summary>The error for a field that asked the signed-in user for their password and got another.</summary>
public static class PasswordErrors
{
    public const string IncorrectMessage = "The password is incorrect.";

    public static IReadOnlyDictionary<string, string[]> Incorrect(string property) =>
        FieldError.On(property, IncorrectMessage);
}
