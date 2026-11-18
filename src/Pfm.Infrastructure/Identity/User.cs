using Microsoft.AspNetCore.Identity;

namespace Pfm.Infrastructure.Identity;

/// <summary>A person who signs in and owns their own separate set of financial data.</summary>
public sealed class User : IdentityUser<Guid>
{
    public User()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }
}
