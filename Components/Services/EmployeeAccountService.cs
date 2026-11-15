using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using StockFlow.Constants;
using StockFlow.Models;

namespace StockFlow.Components.Services;

public sealed record EmployeeListItem(string Id, string FirstName, string LastName, string Email, bool IsActive);

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
    public const string Validation = "validation";
    public const string NotFound = "notfound";
    public const string Forbidden = "forbidden";

    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuthorizationService _authorization;

    public EmployeeAccountService(
        UserManager<ApplicationUser> users,
        IAuthorizationService authorization)
    {
        _users = users;
        _authorization = authorization;
    }

    public async Task<IReadOnlyList<EmployeeListItem>> ListAsync(ClaimsPrincipal actor)
    {
        if (!await IsAdminAsync(actor))
            return [];

        var employees = await _users.GetUsersInRoleAsync(Roles.Employee);
        return employees
            .Where(user => user.Email is not null)
            .OrderBy(user => user.LastName)
            .ThenBy(user => user.FirstName)
            .Select(user => new EmployeeListItem(
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email!,
                user.IsActive))
            .ToList();
    }

    public async Task<EmployeeActionResult> CreateAsync(
        ClaimsPrincipal actor,
        string? firstName,
        string? lastName,
        string? email,
        string? password)
    {
        if (!await IsAdminAsync(actor))
            return EmployeeActionResult.Fail(Forbidden, "Only an administrator can create employee accounts.");

        var first = firstName?.Trim() ?? string.Empty;
        var last = lastName?.Trim() ?? string.Empty;
        var normalizedEmail = email?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(first) ||
            string.IsNullOrWhiteSpace(last) ||
            string.IsNullOrWhiteSpace(normalizedEmail) ||
            string.IsNullOrWhiteSpace(password))
            return EmployeeActionResult.Fail(Validation, "First name, last name, email, and password are required.");

        if (!new EmailAddressAttribute().IsValid(normalizedEmail))
            return EmployeeActionResult.Fail(Validation, "Enter a valid email address.");

        var user = new ApplicationUser
        {
            FirstName = first,
            LastName = last,
            UserName = normalizedEmail,
            Email = normalizedEmail,
            EmailConfirmed = false,
            IsActive = true
        };

        var created = await _users.CreateAsync(user, password);
        if (!created.Succeeded)
            return EmployeeActionResult.Fail(Validation, Describe(created));

        var assigned = await _users.AddToRoleAsync(user, Roles.Employee);
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
        string? firstName,
        string? lastName,
        string? email,
        string? password)
    {
        if (!await IsAdminAsync(actor))
            return EmployeeActionResult.Fail(Forbidden, "Only an administrator can update employee accounts.");

        var user = await FindEmployeeAsync(userId);
        if (user is null)
            return EmployeeActionResult.Fail(NotFound, "Employee account was not found.");

        if (await _users.IsInRoleAsync(user, Roles.Admin))
            return EmployeeActionResult.Fail(Forbidden, "Administrator accounts cannot be changed here.");

        var first = firstName?.Trim() ?? string.Empty;
        var last = lastName?.Trim() ?? string.Empty;
        var normalizedEmail = email?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(first) ||
            string.IsNullOrWhiteSpace(last) ||
            string.IsNullOrWhiteSpace(normalizedEmail))
            return EmployeeActionResult.Fail(Validation, "First name, last name, and email are required.");

        if (!new EmailAddressAttribute().IsValid(normalizedEmail))
            return EmployeeActionResult.Fail(Validation, "Enter a valid email address.");

        var emailChanged = !string.Equals(
            user.Email,
            normalizedEmail,
            StringComparison.OrdinalIgnoreCase);

        user.FirstName = first;
        user.LastName = last;
        user.Email = normalizedEmail;
        user.UserName = normalizedEmail;

        if (emailChanged)
            user.EmailConfirmed = false;

        var updated = await _users.UpdateAsync(user);
        if (!updated.Succeeded)
            return EmployeeActionResult.Fail(Validation, Describe(updated));

        if (!string.IsNullOrWhiteSpace(password))
        {
            var token = await _users.GeneratePasswordResetTokenAsync(user);
            var reset = await _users.ResetPasswordAsync(user, token, password);
            if (!reset.Succeeded)
                return EmployeeActionResult.Fail(Validation, Describe(reset));
        }

        return EmployeeActionResult.Ok(Updated);
    }

    public async Task<EmployeeActionResult> SetActiveAsync(
        ClaimsPrincipal actor,
        string? userId,
        bool isActive)
    {
        if (!await IsAdminAsync(actor))
            return EmployeeActionResult.Fail(Forbidden, "Only an administrator can enable or disable employee accounts.");

        var user = await FindEmployeeAsync(userId);
        if (user is null)
            return EmployeeActionResult.Fail(NotFound, "Employee account was not found.");

        if (await _users.IsInRoleAsync(user, Roles.Admin))
            return EmployeeActionResult.Fail(Forbidden, "Administrator accounts cannot be changed here.");

        user.IsActive = isActive;
        var updated = await _users.UpdateAsync(user);
        if (!updated.Succeeded)
            return EmployeeActionResult.Fail(Validation, Describe(updated));

        var stampUpdated = await _users.UpdateSecurityStampAsync(user);
        if (!stampUpdated.Succeeded)
            return EmployeeActionResult.Fail(Validation, Describe(stampUpdated));

        return EmployeeActionResult.Ok(isActive ? Enabled : Disabled);
    }

    private async Task<ApplicationUser?> FindEmployeeAsync(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        var user = await _users.FindByIdAsync(userId);
        if (user is null || !await _users.IsInRoleAsync(user, Roles.Employee))
            return null;

        return user;
    }

    private async Task<bool> IsAdminAsync(ClaimsPrincipal actor)
    {
        var result = await _authorization.AuthorizeAsync(
            actor,
            StockFlowAuthorization.AdminPolicy);

        return result.Succeeded;
    }

    private static string Describe(IdentityResult result)
    {
        return string.Join(" ", result.Errors.Select(error => error.Description));
    }
}
