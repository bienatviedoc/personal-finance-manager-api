namespace Pfm.Domain;

/// <summary>
/// Data that belongs to one user. Only that user can see or change it: persistence filters every
/// query by the signed-in user and stamps new rows with them.
/// </summary>
public interface IUserOwned
{
    Guid UserId { get; }
}
