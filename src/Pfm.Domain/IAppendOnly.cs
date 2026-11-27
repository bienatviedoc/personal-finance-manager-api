namespace Pfm.Domain;

/// <summary>
/// Data that is only ever added: once saved, a row is never changed or deleted by anything the app does. Persistence
/// refuses to save a change or delete of one, and the database refuses them too.
/// </summary>
public interface IAppendOnly;
