using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace StockFlow.Components.Services;

public static class StockFlowAuthorization
{
    public const string AdminPolicy = "AdminOnly";
    public const string EmployeePolicy = "EmployeeAccess";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(AdminPolicy, policy => policy.RequireRole(AppRoles.Admin));
        options.AddPolicy(EmployeePolicy, policy => policy.RequireRole(AppRoles.Admin, AppRoles.Employee));
    }

    public static string HomeFor(ClaimsPrincipal user)
    {
        if (user.IsInRole(AppRoles.Admin))
        {
            return "/admin";
        }

        if (user.IsInRole(AppRoles.Employee))
        {
            return "/employee-access";
        }

        return "/dashboard";
    }

    public static string HomeForRoles(IEnumerable<string> roles)
    {
        if (roles.Contains(AppRoles.Admin))
        {
            return "/admin";
        }

        if (roles.Contains(AppRoles.Employee))
        {
            return "/employee-access";
        }

        return "/dashboard";
    }
}
