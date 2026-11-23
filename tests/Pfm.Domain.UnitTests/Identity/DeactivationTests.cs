using Pfm.Domain.Identity;

namespace Pfm.Domain.UnitTests.Identity;

public class DeactivationTests
{
    private static readonly DateTimeOffset DeactivatedAt = new(2026, 11, 1, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_deactivated_user_can_cancel_for_thirty_days()
    {
        Assert.False(Deactivation.IsDueForErasure(DeactivatedAt, DeactivatedAt));
        Assert.False(Deactivation.IsDueForErasure(DeactivatedAt, new DateTimeOffset(2026, 12, 1, 9, 29, 59, TimeSpan.Zero)));
    }

    [Fact]
    public void After_thirty_days_their_data_is_due_for_erasure()
    {
        Assert.True(Deactivation.IsDueForErasure(DeactivatedAt, new DateTimeOffset(2026, 12, 1, 9, 30, 0, TimeSpan.Zero)));
        Assert.True(Deactivation.IsDueForErasure(DeactivatedAt, new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero)));
    }
}
