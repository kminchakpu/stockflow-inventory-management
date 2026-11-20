using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Data;

namespace StockFlow.Components.Services;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/login", LoginAsync);
        endpoints.MapPost("/account/logout", LogoutAsync).RequireAuthorization();
        endpoints.MapPost("/admin/employees", CreateEmployeeAsync)
            .RequireAuthorization(StockFlowAuthorization.AdminPolicy);
        endpoints.MapPost("/admin/employees/update", UpdateEmployeeAsync)
            .RequireAuthorization(StockFlowAuthorization.AdminPolicy);
        endpoints.MapPost("/admin/employees/active", SetEmployeeActiveAsync)
            .RequireAuthorization(StockFlowAuthorization.AdminPolicy);
        endpoints.MapPost("/admin/employees/delete", DeleteEmployeeAsync)
            .RequireAuthorization(StockFlowAuthorization.AdminPolicy);
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILoggerFactory loggerFactory,
        [FromForm] LoginForm form)
    {
        var logger = loggerFactory.CreateLogger("StockFlow.Auth");
        var email = form.Email?.Trim() ?? string.Empty;
        var password = form.Password ?? string.Empty;
        var returnUrl = string.IsNullOrWhiteSpace(form.ReturnUrl) ? null : form.ReturnUrl;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Results.Redirect(LoginError("missing", returnUrl));
        }

        if (!new EmailAddressAttribute().IsValid(email))
        {
            return Results.Redirect(LoginError("email", returnUrl));
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            logger.LogWarning("Rejected sign-in for {Email}", email);
            return Results.Redirect(LoginError("invalid", returnUrl));
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return Results.Redirect(LoginError("locked", returnUrl));
        }

        if (!user.IsActive)
        {
            var passwordMatches = await userManager.CheckPasswordAsync(user, password);
            logger.LogWarning("Rejected sign-in for disabled account {Email}", email);
            return Results.Redirect(LoginError(passwordMatches ? "disabled" : "invalid", returnUrl));
        }

        var result = await signInManager.PasswordSignInAsync(
            user.UserName!,
            password,
            form.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var roles = await userManager.GetRolesAsync(user);
            var destination = string.IsNullOrWhiteSpace(returnUrl)
                ? StockFlowAuthorization.HomeForRoles(roles)
                : LocalRedirect.Normalize(returnUrl);
            return Results.Redirect(destination);
        }

        logger.LogWarning("Rejected sign-in for {Email}", email);
        return Results.Redirect(LoginError(result.IsLockedOut ? "locked" : "invalid", returnUrl));
    }

    private static async Task<IResult> LogoutAsync(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.Redirect("/login");
    }

    private static async Task<IResult> CreateEmployeeAsync(
        HttpContext httpContext,
        EmployeeAccountService employees,
        [FromForm] EmployeeCreateForm form)
    {
        var result = await employees.CreateAsync(httpContext.User, form.DisplayName, form.Email, form.Password);
        return EmployeeRedirect(result);
    }

    private static async Task<IResult> UpdateEmployeeAsync(
        HttpContext httpContext,
        EmployeeAccountService employees,
        [FromForm] EmployeeUpdateForm form)
    {
        var result = await employees.UpdateAsync(
            httpContext.User,
            form.UserId,
            form.DisplayName,
            form.Email,
            form.Password);
        return EmployeeRedirect(result);
    }

    private static async Task<IResult> SetEmployeeActiveAsync(
        HttpContext httpContext,
        EmployeeAccountService employees,
        [FromForm] EmployeeActiveForm form)
    {
        var result = await employees.SetActiveAsync(httpContext.User, form.UserId, form.IsActive);
        return EmployeeRedirect(result);
    }

    private static async Task<IResult> DeleteEmployeeAsync(
        HttpContext httpContext,
        EmployeeAccountService employees,
        [FromForm] string? userId)
    {
        var result = await employees.DeleteAsync(httpContext.User, userId);
        return EmployeeRedirect(result);
    }

    private static IResult EmployeeRedirect(EmployeeActionResult result)
    {
        if (result.Succeeded)
        {
            return Results.Redirect("/admin/employees?notice=" + result.Code);
        }

        return Results.Redirect("/admin/employees?notice=" + result.Code + "&detail=" + Uri.EscapeDataString(result.Detail));
    }

    private static string LoginError(string code, string? returnUrl)
    {
        var url = "/login?error=" + code;
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            url += "&returnUrl=" + Uri.EscapeDataString(returnUrl);
        }

        return url;
    }
}

public sealed class LoginForm
{
    public string? Email { get; set; }
    public string? Password { get; set; }
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class EmployeeCreateForm
{
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
}

public sealed class EmployeeUpdateForm
{
    public string? UserId { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
}

public sealed class EmployeeActiveForm
{
    public string? UserId { get; set; }
    public bool IsActive { get; set; }
}
