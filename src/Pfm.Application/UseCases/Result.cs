namespace Pfm.Application.UseCases;

/// <summary>
/// What a use case answers: either its outcome, or the fields to fix. Errors are keyed by the
/// command's property name (<c>Email</c>); the API turns that into the request's field name.
/// </summary>
public sealed class Result<TOutcome>
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    private readonly TOutcome? _outcome;

    private Result(TOutcome? outcome, IReadOnlyDictionary<string, string[]> errors)
    {
        _outcome = outcome;
        Errors = errors;
    }

    public bool IsValid => Errors.Count == 0;

    /// <summary>The fields to fix and why. Empty when the command was valid.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public TOutcome Outcome => IsValid ? _outcome! : throw new InvalidOperationException("An invalid command has no outcome.");

    public static implicit operator Result<TOutcome>(TOutcome outcome) => new(outcome, NoErrors);

    public static Result<TOutcome> Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        errors.Count > 0 ? new(default, errors) : throw new ArgumentException("An invalid result needs at least one error.", nameof(errors));
}

/// <summary>The outcome of a use case that has nothing to say beyond having been done.</summary>
public readonly record struct Done;
