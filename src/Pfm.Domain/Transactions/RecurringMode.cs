namespace Pfm.Domain.Transactions;

/// <summary>What a recurring rule does when an occurrence falls due (spec REC-2). No mode is zero, so an unset one is never valid.</summary>
public enum RecurringMode
{
    /// <summary>The worker posts the occurrence as a transaction on its due date.</summary>
    AutoPost = 1,

    /// <summary>The occurrence becomes a reminder, posted only when the user confirms it.</summary>
    Remind = 2,
}
