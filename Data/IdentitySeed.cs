using Microsoft.AspNetCore.Identity;
using StockFlow.Components.Services;

namespace StockFlow.Data;

public static class IdentitySeed
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in new[] { AppRoles.Admin, AppRoles.Employee })
        {
            if (!await roles.RoleExistsAsync(role))
            {
                var created = await roles.CreateAsync(new IdentityRole(role));
                if (!created.Succeeded)
                {
                    throw new InvalidOperationException(Describe(created));
                }
            }
        }

        await EnsureUserAsync(users, "admin@stockflow.local", "Admin@123", "Kevin Cross", AppRoles.Admin);
        await EnsureUserAsync(users, "employee@stockflow.local", "Employee@123", "Aisha Brand", AppRoles.Employee);
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> users,
        string email,
        string password,
        string displayName,
        string role)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = displayName,
                IsActive = true
            };

            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(Describe(created));
            }
        }

        if (!await users.IsInRoleAsync(user, role))
        {
            var assigned = await users.AddToRoleAsync(user, role);
            if (!assigned.Succeeded)
            {
                throw new InvalidOperationException(Describe(assigned));
            }
        }
    }

    private static string Describe(IdentityResult result)
    {
        return string.Join("; ", result.Errors.Select(error => error.Description));
    }
}
