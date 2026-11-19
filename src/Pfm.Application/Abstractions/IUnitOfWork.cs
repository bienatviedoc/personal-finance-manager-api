namespace Pfm.Application.Abstractions;

/// <summary>Saves every change a use case made, together, or none of them.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
