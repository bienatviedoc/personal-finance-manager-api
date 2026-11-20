using Pfm.Application.Abstractions;

namespace Pfm.Application.UseCases;

/// <summary>
/// For a change that names no row version (a delete, an archive, a profile setting): it has no stale
/// version to refuse, so when a row it read is changed by someone else before it saves, it is made again on
/// what was saved. An edit made from a row version answers stale instead.
/// </summary>
public static class ConcurrentChanges
{
    /// <summary>How many times a change is made before a row that keeps changing is given up on.</summary>
    public const int Attempts = 3;

    /// <summary>
    /// Runs <paramref name="readChangeAndSave"/>, which reads what it changes each time it runs. When its save
    /// meets a concurrent change, everything read is forgotten and it runs again; the last attempt's
    /// <see cref="ConcurrentChangeException"/> is thrown on.
    /// </summary>
    public static async Task<TOutcome> RetryOnConcurrentChangeAsync<TOutcome>(this IUnitOfWork unitOfWork, Func<Task<TOutcome>> readChangeAndSave)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(readChangeAndSave);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await readChangeAndSave();
            }
            catch (ConcurrentChangeException) when (attempt < Attempts)
            {
                unitOfWork.DiscardChanges();
            }
        }
    }
}
