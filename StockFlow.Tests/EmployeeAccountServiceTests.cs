
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StockFlow.Components.Services;
using StockFlow.Constants;
using StockFlow.Models;

namespace StockFlow.Tests;

public partial class AuthenticationAuthorizationTests
{
    private static ClaimsPrincipal CreateAdminActor()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "test-admin-actor"),
            new Claim(ClaimTypes.Name, "admin@stockflow.local"),
            new Claim(ClaimTypes.Role, Roles.Admin)
        ], "TestAuthentication");

        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreateEmployeeActor()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "test-employee-actor"),
            new Claim(ClaimTypes.Name, "employee@stockflow.local"),
            new Claim(ClaimTypes.Role, Roles.Employee)
        ], "TestAuthentication");

        return new ClaimsPrincipal(identity);
    }

    private async Task<string> CreateEmployeeForServiceTestAsync(
        EmployeeAccountService service,
        string email = "service.employee@stockflow.local")
    {
        var result = await service.CreateAsync(
            CreateAdminActor(),
            "Service",
            "Employee",
            email,
            "Employee@456789");

        Assert.IsTrue(
            result.Succeeded,
            $"Employee creation failed: {result.Code} - {result.Detail}");

        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var employee = await users.FindByEmailAsync(email);

        Assert.IsNotNull(employee);
        return employee!.Id;
    }

    [TestMethod]
    public async Task AdminCanCreateEmployeeAccountThroughService()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var result = await service.CreateAsync(
            CreateAdminActor(),
            "  John ",
            "  Smith ",
            "john.smith@stockflow.local",
            "Employee@456789");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(EmployeeAccountService.Created, result.Code);

        var employee = await users.FindByEmailAsync("john.smith@stockflow.local");

        Assert.IsNotNull(employee);
        Assert.AreEqual("John", employee!.FirstName);
        Assert.AreEqual("Smith", employee.LastName);
        Assert.IsTrue(employee.IsActive);
        Assert.IsFalse(employee.EmailConfirmed);
        Assert.IsNotNull(employee.PasswordHash);
        Assert.IsFalse(employee.PasswordHash.Contains("Employee@456789", StringComparison.Ordinal));
        Assert.IsTrue(await users.CheckPasswordAsync(employee, "Employee@456789"));
        Assert.IsTrue(await users.IsInRoleAsync(employee, Roles.Employee));
        Assert.IsFalse(await users.IsInRoleAsync(employee, Roles.Admin));
    }

    [TestMethod]
    public async Task EmployeeCannotCreateAnotherEmployeeAccount()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var result = await service.CreateAsync(
            CreateEmployeeActor(),
            "Blocked",
            "Employee",
            "blocked.employee@stockflow.local",
            "Employee@456789");

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(EmployeeAccountService.Forbidden, result.Code);

        var employee = await users.FindByEmailAsync("blocked.employee@stockflow.local");
        Assert.IsNull(employee);
    }

    [TestMethod]
    public async Task AdminCanUpdateEmployeeAccount()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var employeeId = await CreateEmployeeForServiceTestAsync(
            service,
            "update.employee@stockflow.local");

        var result = await service.UpdateAsync(
            CreateAdminActor(),
            employeeId,
            "Updated",
            "Employee",
            "updated.employee@stockflow.local",
            "Updated@456789");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(EmployeeAccountService.Updated, result.Code);

        var employee = await users.FindByIdAsync(employeeId);

        Assert.IsNotNull(employee);
        Assert.AreEqual("Updated", employee!.FirstName);
        Assert.AreEqual("Employee", employee.LastName);
        Assert.AreEqual("updated.employee@stockflow.local", employee.Email);
        Assert.AreEqual("updated.employee@stockflow.local", employee.UserName);
        Assert.IsFalse(employee.EmailConfirmed);
        Assert.IsTrue(await users.CheckPasswordAsync(employee, "Updated@456789"));
        Assert.IsTrue(await users.IsInRoleAsync(employee, Roles.Employee));
    }

    [TestMethod]
    public async Task EmployeeCannotUpdateAnotherEmployeeAccount()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var employeeId = await CreateEmployeeForServiceTestAsync(
            service,
            "protected.employee@stockflow.local");

        var result = await service.UpdateAsync(
            CreateEmployeeActor(),
            employeeId,
            "Changed",
            "Name",
            "changed.employee@stockflow.local",
            null);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(EmployeeAccountService.Forbidden, result.Code);

        var employee = await users.FindByIdAsync(employeeId);

        Assert.IsNotNull(employee);
        Assert.AreEqual("Service", employee!.FirstName);
        Assert.AreEqual("protected.employee@stockflow.local", employee.Email);
    }

    [TestMethod]
    public async Task AdminCanDisableAndEnableEmployeeAccount()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var employeeId = await CreateEmployeeForServiceTestAsync(
            service,
            "status.employee@stockflow.local");

        var employee = await users.FindByIdAsync(employeeId);
        Assert.IsNotNull(employee);
        var originalSecurityStamp = employee!.SecurityStamp;

        var disableResult = await service.SetActiveAsync(
            CreateAdminActor(),
            employeeId,
            false);

        Assert.IsTrue(disableResult.Succeeded);
        Assert.AreEqual(EmployeeAccountService.Disabled, disableResult.Code);

        employee = await users.FindByIdAsync(employeeId);
        Assert.IsNotNull(employee);
        Assert.IsFalse(employee!.IsActive);
        Assert.AreNotEqual(originalSecurityStamp, employee.SecurityStamp);

        var enableResult = await service.SetActiveAsync(
            CreateAdminActor(),
            employeeId,
            true);

        Assert.IsTrue(enableResult.Succeeded);
        Assert.AreEqual(EmployeeAccountService.Enabled, enableResult.Code);

        employee = await users.FindByIdAsync(employeeId);
        Assert.IsNotNull(employee);
        Assert.IsTrue(employee!.IsActive);
    }

    [TestMethod]
    public async Task EmployeeCannotDisableAnotherEmployeeAccount()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var employeeId = await CreateEmployeeForServiceTestAsync(
            service,
            "active.employee@stockflow.local");

        var result = await service.SetActiveAsync(
            CreateEmployeeActor(),
            employeeId,
            false);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(EmployeeAccountService.Forbidden, result.Code);

        var employee = await users.FindByIdAsync(employeeId);
        Assert.IsNotNull(employee);
        Assert.IsTrue(employee!.IsActive);
    }

    [TestMethod]
    public async Task AdminCanDeleteEmployeeAccount()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var employeeId = await CreateEmployeeForServiceTestAsync(
            service,
            "delete.employee@stockflow.local");

        var result = await service.DeleteAsync(
            CreateAdminActor(),
            employeeId);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(EmployeeAccountService.Deleted, result.Code);

        var deletedEmployee = await users.FindByIdAsync(employeeId);
        Assert.IsNull(deletedEmployee);
    }

    [TestMethod]
    public async Task EmployeeCannotDeleteAnotherEmployeeAccount()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var employeeId = await CreateEmployeeForServiceTestAsync(
            service,
            "undeleted.employee@stockflow.local");

        var result = await service.DeleteAsync(
            CreateEmployeeActor(),
            employeeId);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(EmployeeAccountService.Forbidden, result.Code);

        var employee = await users.FindByIdAsync(employeeId);
        Assert.IsNotNull(employee);
    }

    [TestMethod]
    public async Task AdminCanListEmployeeAccounts()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();

        await CreateEmployeeForServiceTestAsync(
            service,
            "list.employee@stockflow.local");

        var employees = await service.ListAsync(CreateAdminActor());

        Assert.IsTrue(
            employees.Any(employee =>
                employee.Email == "list.employee@stockflow.local"));

        Assert.IsTrue(
            employees.Any(employee =>
                employee.Email == "employee@stockflow.local"));
    }

    [TestMethod]
    public async Task EmployeeCannotListEmployeeAccounts()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();

        var employees = await service.ListAsync(CreateEmployeeActor());

        Assert.AreEqual(0, employees.Count);
    }

    [TestMethod]
    public async Task CreateEmployeeRejectsInvalidEmail()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();

        var result = await service.CreateAsync(
            CreateAdminActor(),
            "Invalid",
            "Email",
            "not-an-email",
            "Employee@456789");

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(EmployeeAccountService.Validation, result.Code);
    }

    [TestMethod]
    public async Task CreateEmployeeRejectsMissingRequiredFields()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();

        var result = await service.CreateAsync(
            CreateAdminActor(),
            "",
            "Employee",
            "missing.name@stockflow.local",
            "Employee@456789");

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(EmployeeAccountService.Validation, result.Code);
    }

    [TestMethod]
    public async Task UpdateEmployeeRejectsUnknownEmployee()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();

        var result = await service.UpdateAsync(
            CreateAdminActor(),
            "unknown-employee-id",
            "Unknown",
            "Employee",
            "unknown@stockflow.local",
            null);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(EmployeeAccountService.NotFound, result.Code);
    }

    [TestMethod]
    public async Task DeleteEmployeeRejectsUnknownEmployee()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();

        var result = await service.DeleteAsync(
            CreateAdminActor(),
            "unknown-employee-id");

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(EmployeeAccountService.NotFound, result.Code);
    }
}
