using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using StockFlow.Data;

namespace StockFlow.Components.Services;

public sealed record EmployeeListItem(string Id, string DisplayName, string Email, bool IsActive);

public sealed record EmployeeActionResult(bool Succeeded, string Code, string Detail)
{
    public static EmployeeActionResult Ok(string code) => new(true, code, string.Empty);

    public static EmployeeActionResult Fail(string code, string detail) => new(false, code, detail);
}

public sealed class EmployeeAccountService
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Disabled = "disabled";
    public const string Enabled = "enabled";
    public const string Deleted = "deleted";
    public const string Validation = "validation";
    public const string NotFound = "notfound";
    public const string Forbidden = "forbidden";

    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuthorizationService _authorization;

    public EmployeeAccountService(UserManager<ApplicationUser> users, IAuthorizationService authorization)
    {
        _users = users;
        _authorization = authorization;
    }

    public async Task<IReadOnlyList<EmployeeListItem>> ListAsync(ClaimsPrincipal actor)
    {
        if (!await IsAdminAsync(actor))
        {
            return [];
        }

        var employees = await _users.GetUsersInRoleAsync(AppRoles.Employee);
        return employees
            .Where(user => user.Email is not null)
            .OrderBy(user => user.DisplayName)
            .Select(user => new EmployeeListItem(user.Id, user.DisplayName, user.Email!, user.IsActive))
            .ToList();
    }

    public async Task<EmployeeActionResult> CreateAsync(
        ClaimsPrincipal actor,
        string? displayName,
        string? email,
        string? password)
    {
        if (!await IsAdminAsync(actor))
        {
            return EmployeeActionResult.Fail(Forbidden, "Only an administrator can create employee accounts.");
        }

        var name = displayName?.Trim() ?? string.Empty;
        var normalizedEmail = email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(password))
        {
            return EmployeeActionResult.Fail(Validation, "Name, email, and password are required.");
        }

        if (!new EmailAddressAttribute().IsValid(normalizedEmail))
        {
            return EmployeeActionResult.Fail(Validation, "Enter a valid email address.");
        }

        var user = new ApplicationUser
        {
            UserName = normalizedEmail,
            Email = normalizedEmail,
            EmailConfirmed = true,
            DisplayName = name,
            IsActive = true
        };

        var created = await _users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            return EmployeeActionResult.Fail(Validation, Describe(created));
        }

        var assigned = await _users.AddToRoleAsync(user, AppRoles.Employee);
        if (!assigned.Succeeded)
        {
            await _users.DeleteAsync(user);
            return EmployeeActionResult.Fail(Validation, Describe(assigned));
        }

        return EmployeeActionResult.Ok(Created);
    }

    public async Task<EmployeeActionResult> UpdateAsync(
        ClaimsPrincipal actor,
        string? userId,
        string? displayName,
        string? email,
        string? password)
    {
        if (!await IsAdminAsync(actor))
        {
            return EmployeeActionResult.Fail(Forbidden, "Only an administrator can update employee accounts.");
        }

        var user = await FindEmployeeAsync(userId);
        if (user is null)
        {
            return EmployeeActionResult.Fail(NotFound, "Employee account was not found.");
        }

        var blocked = await RejectAdminAccountAsync(user);
        if (blocked is not null)
        {
            return blocked;
        }

        var name = displayName?.Trim() ?? string.Empty;
        var normalizedEmail = email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return EmployeeActionResult.Fail(Validation, "Name and email are required.");
        }

        if (!new EmailAddressAttribute().IsValid(normalizedEmail))
        {
            return EmployeeActionResult.Fail(Validation, "Enter a valid email address.");
        }

        user.DisplayName = name;
        var updated = await _users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return EmployeeActionResult.Fail(Validation, Describe(updated));
        }

        if (!string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
        {
            var emailResult = await _users.SetEmailAsync(user, normalizedEmail);
            if (!emailResult.Succeeded)
            {
                return EmployeeActionResult.Fail(Validation, Describe(emailResult));
            }

            var userNameResult = await _users.SetUserNameAsync(user, normalizedEmail);
            if (!userNameResult.Succeeded)
            {
                return EmployeeActionResult.Fail(Validation, Describe(userNameResult));
            }
        }

        if (!string.IsNullOrWhiteSpace(password))
        {
            var token = await _users.GeneratePasswordResetTokenAsync(user);
            var reset = await _users.ResetPasswordAsync(user, token, password);
            if (!reset.Succeeded)
            {
                return EmployeeActionResult.Fail(Validation, Describe(reset));
            }
        }

        return EmployeeActionResult.Ok(Updated);
    }

    public async Task<EmployeeActionResult> SetActiveAsync(ClaimsPrincipal actor, string? userId, bool isActive)
    {
        if (!await IsAdminAsync(actor))
        {
            return EmployeeActionResult.Fail(Forbidden, "Only an administrator can enable or disable employee accounts.");
        }

        var user = await FindEmployeeAsync(userId);
        if (user is null)
        {
            return EmployeeActionResult.Fail(NotFound, "Employee account was not found.");
        }

        var blocked = await RejectAdminAccountAsync(user);
        if (blocked is not null)
        {
            return blocked;
        }

        user.IsActive = isActive;
        var updated = await _users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return EmployeeActionResult.Fail(Validation, Describe(updated));
        }

        await _users.UpdateSecurityStampAsync(user);
        return EmployeeActionResult.Ok(isActive ? Enabled : Disabled);
    }

    public async Task<EmployeeActionResult> DeleteAsync(ClaimsPrincipal actor, string? userId)
    {
        if (!await IsAdminAsync(actor))
        {
            return EmployeeActionResult.Fail(Forbidden, "Only an administrator can remove employee accounts.");
        }

        var user = await FindEmployeeAsync(userId);
        if (user is null)
        {
            return EmployeeActionResult.Fail(NotFound, "Employee account was not found.");
        }

        var blocked = await RejectAdminAccountAsync(user);
        if (blocked is not null)
        {
            return blocked;
        }

        var deleted = await _users.DeleteAsync(user);
        if (!deleted.Succeeded)
        {
            return EmployeeActionResult.Fail(Validation, Describe(deleted));
        }

        return EmployeeActionResult.Ok(Deleted);
    }

    private async Task<ApplicationUser?> FindEmployeeAsync(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var user = await _users.FindByIdAsync(userId);
        if (user is null || !await _users.IsInRoleAsync(user, AppRoles.Employee))
        {
            return null;
        }

        return user;
    }

    private async Task<EmployeeActionResult?> RejectAdminAccountAsync(ApplicationUser user)
    {
        if (await _users.IsInRoleAsync(user, AppRoles.Admin))
        {
            return EmployeeActionResult.Fail(Forbidden, "Administrator accounts cannot be changed here.");
        }

        return null;
    }

    private async Task<bool> IsAdminAsync(ClaimsPrincipal actor)
    {
        var result = await _authorization.AuthorizeAsync(actor, StockFlowAuthorization.AdminPolicy);
        return result.Succeeded;
    }

    private static string Describe(IdentityResult result)
    {
        return string.Join(" ", result.Errors.Select(error => error.Description));
    }
}
