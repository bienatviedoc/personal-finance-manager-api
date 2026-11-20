namespace Pfm.Domain;

/// <summary>
/// The name a user gives something of theirs, such as an institution or a category: required, at most
/// <see cref="MaxLength"/> characters, and kept without the spaces around it.
/// </summary>
public static class Names
{
    public const int MaxLength = 100;

    public static bool IsValid(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= MaxLength;
}
