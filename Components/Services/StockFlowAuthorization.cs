using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using StockFlow.Constants;

namespace StockFlow.Components.Services;

public static class StockFlowAuthorization
{
    public const string AdminPolicy = "AdminOnly";
    public const string EmployeePolicy = "EmployeeAccess";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(AdminPolicy, policy =>
            policy.RequireRole(Roles.Admin));
        options.AddPolicy(EmployeePolicy, policy =>
            policy.RequireRole(Roles.Admin, Roles.Employee));
    }

    public static string HomeFor(ClaimsPrincipal user)
    {
        if (user.IsInRole(Roles.Admin))
            return "/admin";
        if (user.IsInRole(Roles.Employee))
            return "/employee-access";
        return "/dashboard";
    }

    public static string HomeForRoles(IEnumerable<string> roles)
    {
        if (roles.Contains(Roles.Admin))
            return "/admin";
        if (roles.Contains(Roles.Employee))
            return "/employee-access";
        return "/dashboard";
    }
}
