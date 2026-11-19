using FluentValidation;

namespace Pfm.Application.UseCases;

/// <summary>
/// Carries out one command. The command's validator runs first; the handler's own work runs
/// only for a valid command. Subclasses are registered automatically by <see cref="ApplicationServices"/>.
/// </summary>
public abstract class CommandHandler<TCommand, TOutcome>(IValidator<TCommand> validator)
{
    public async Task<Result<TOutcome>> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        return validation.IsValid
            ? await ExecuteAsync(command, cancellationToken)
            : Result<TOutcome>.Invalid(validation.ToDictionary().AsReadOnly());
    }

    /// <summary>The use case itself, for a command its validator accepted.</summary>
    protected abstract Task<Result<TOutcome>> ExecuteAsync(TCommand command, CancellationToken cancellationToken);
}
