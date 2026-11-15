
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Models;

namespace StockFlow.Components.Services;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/login", LoginAsync)
            .AllowAnonymous();
        endpoints.MapPost("/account/logout", LogoutAsync)
            .RequireAuthorization();
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
        var returnUrl = GetSafeReturnUrl(form.ReturnUrl);

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Results.Redirect(LoginError("missing", returnUrl));

        if (!new EmailAddressAttribute().IsValid(email))
            return Results.Redirect(LoginError("email", returnUrl));

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            logger.LogWarning("Login rejected: account not found.");
            return Results.Redirect(LoginError("invalid", returnUrl));
        }

        if (!user.IsActive)
        {
            logger.LogWarning("Login rejected: account is inactive.");
            return Results.Redirect(LoginError("invalid", returnUrl));
        }

        var result = await signInManager.PasswordSignInAsync(
            user,
            password,
            form.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            logger.LogInformation("Login successful.");
            var roles = await userManager.GetRolesAsync(user);
            var destination = returnUrl ?? StockFlowAuthorization.HomeForRoles(roles);
            return Results.LocalRedirect(destination);
        }

        if (result.IsLockedOut)
            logger.LogWarning("Login rejected: account is locked out.");
        else if (result.IsNotAllowed)
            logger.LogWarning("Login rejected: sign-in is not allowed.");
        else if (result.RequiresTwoFactor)
            logger.LogWarning("Login rejected: two-factor authentication is required.");
        else
            logger.LogWarning("Login rejected: password verification failed.");

        return Results.Redirect(LoginError(
            result.IsLockedOut ? "locked" : "invalid",
            returnUrl));
    }

    private static async Task<IResult> LogoutAsync(
        SignInManager<ApplicationUser> signInManager,
        [FromForm] LogoutForm form)
    {
        await signInManager.SignOutAsync();
        return Results.Redirect("/login");
    }

    private static string? GetSafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return null;

        var candidate = returnUrl.Trim();

        if (!candidate.StartsWith("/", StringComparison.Ordinal)
            || candidate.StartsWith("//", StringComparison.Ordinal)
            || candidate.StartsWith("/\\", StringComparison.Ordinal)
            || candidate.Contains('\\')
            || candidate.Contains("://", StringComparison.Ordinal)
            || candidate.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return candidate;
    }

    private static string LoginError(string code, string? returnUrl)
    {
        var url = "/login?error=" + Uri.EscapeDataString(code);

        if (!string.IsNullOrWhiteSpace(returnUrl))
            url += "&returnUrl=" + Uri.EscapeDataString(returnUrl);

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

public sealed class LogoutForm
{
    public string? ReturnUrl { get; set; }
}
