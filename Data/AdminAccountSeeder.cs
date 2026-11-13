using Microsoft.AspNetCore.Identity;
using StockFlow.Constants;
using StockFlow.Models;

namespace StockFlow.Data;

public static class AdminAccountSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var email = configuration["BootstrapAdmin:Email"];
        var password = configuration["BootstrapAdmin:Password"];
        var firstName = configuration["BootstrapAdmin:FirstName"];
        var lastName = configuration["BootstrapAdmin:LastName"];

        if (string.IsNullOrWhiteSpace(email) &&
            string.IsNullOrWhiteSpace(password) &&
            string.IsNullOrWhiteSpace(firstName) &&
            string.IsNullOrWhiteSpace(lastName))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(firstName) ||
            string.IsNullOrWhiteSpace(lastName))
        {
            throw new InvalidOperationException(
                "All BootstrapAdmin settings must be provided to provision the initial administrator.");
        }

        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var existingUser = await userManager.FindByEmailAsync(email);

        if (existingUser is not null)
        {
            if (!await userManager.IsInRoleAsync(existingUser, Roles.Admin))
            {
                throw new InvalidOperationException(
                    "The configured BootstrapAdmin email already belongs to a user who is not an Admin. Review account before proceeding.");
            }

            return;
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            IsActive = true
        };

        var createResult = await userManager.CreateAsync(admin, password);

        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Failed to create the initial Admin account: {errors}");
        }

        var roleResult = await userManager.AddToRoleAsync(admin, Roles.Admin);

        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(admin);

            var errors = string.Join("; ", roleResult.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Failed to assign the Admin role: {errors}");
        }
    }
}