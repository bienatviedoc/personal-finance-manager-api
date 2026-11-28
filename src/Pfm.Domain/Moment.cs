namespace Pfm.Domain;

/// <summary>The moment an action is taken: the date it is in the user's time zone, and the instant.</summary>
/// <param name="Today">Today's date in the user's time zone, which no date the action enters may be after.</param>
/// <param name="Now">The instant, which the action is stamped with.</param>
public sealed record Moment(DateOnly Today, DateTimeOffset Now);
