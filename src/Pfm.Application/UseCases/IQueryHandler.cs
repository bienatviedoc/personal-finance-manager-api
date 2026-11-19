namespace Pfm.Application.UseCases;

/// <summary>
/// Marks a handler that answers a query: it reads and changes nothing, so it has no validator or
/// <see cref="Result{TOutcome}"/>. Implementations are registered automatically by <see cref="ApplicationServices"/>.
/// </summary>
public interface IQueryHandler;
