// Stand-in types laid out like real modules, used only to prove the module boundary rule works.
#pragma warning disable CA1822, IDE0052

namespace Pfm.ArchitectureTests.Fixtures
{
    public static class FixtureMarker;
}

namespace ArchFixture.Domain.Accounts
{
    public sealed class AccountInternals;
}

namespace ArchFixture.Application.Contracts.Accounts
{
    public interface IAccountBalances;
}

namespace ArchFixture.Domain.Budgets
{
    public sealed class UsesAccountInternals
    {
        public ArchFixture.Domain.Accounts.AccountInternals? Account { get; set; }
    }
}

namespace ArchFixture.Application.Contracts.Budgets
{
    public interface ILeaksAccountInternals
    {
        ArchFixture.Domain.Accounts.AccountInternals? Account { get; }
    }
}

namespace ArchFixture.Application.Budgets
{
    public sealed class UsesAccountContracts
    {
        public ArchFixture.Application.Contracts.Accounts.IAccountBalances? Balances { get; set; }
    }
}
