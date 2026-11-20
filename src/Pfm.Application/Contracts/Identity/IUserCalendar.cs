namespace Pfm.Application.Contracts.Identity;

/// <summary>
/// Calendar dates as the user reads them: in their time zone (spec section 3, rule 8). Other modules ask it
/// what today is, so a date is in the future for the user and not for the server.
/// </summary>
public interface IUserCalendar
{
    /// <summary>Today's date in the time zone of the signed-in user.</summary>
    Task<DateOnly> TodayAsync(CancellationToken cancellationToken);
}
