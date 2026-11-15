using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace StockFlow.Data;

public sealed class AppUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
{
    public AppUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName ?? user.Email ?? "User" : user.DisplayName;
        Replace(identity, ClaimTypes.Name, displayName);

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            Replace(identity, ClaimTypes.Email, user.Email);
        }

        return identity;
    }

    private static void Replace(ClaimsIdentity identity, string claimType, string value)
    {
        var existing = identity.FindFirst(claimType);
        if (existing is not null)
        {
            identity.RemoveClaim(existing);
        }

        identity.AddClaim(new Claim(claimType, value));
    }
}
