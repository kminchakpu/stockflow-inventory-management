
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using StockFlow.Components.Services;
using StockFlow.Constants;
using StockFlow.Data;
using StockFlow.Models;

namespace StockFlow.Tests;

[TestClass]
public class AuthenticationAuthorizationTests
{
    private const string AdminEmail = "admin@test.stockflow.local";
    private const string AdminPassword = "Admin@Test123!";
    private const string EmployeeEmail = "employee@test.stockflow.local";
    private const string EmployeePassword = "Employee@Test123!";
    private StockFlowAppFactory _factory = null!;
    private HttpClient _client = null!;
    private ApplicationUser _admin = null!;
    private ApplicationUser _employee = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _factory = new StockFlowAppFactory();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        _admin = await CreateUserAsync(
            AdminEmail,
            AdminPassword,
            "Test",
            "Administrator",
            Roles.Admin);
        _employee = await CreateUserAsync(
            EmployeeEmail,
            EmployeePassword,
            "Test",
            "Employee",
            Roles.Employee);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task TestAccountsHaveHashedPasswordsAndCorrectRoles()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.FindByEmailAsync(AdminEmail);
        var employee = await users.FindByEmailAsync(EmployeeEmail);

        Assert.IsNotNull(admin);
        Assert.IsNotNull(employee);
        Assert.IsNotNull(admin.PasswordHash);
        Assert.IsNotNull(employee.PasswordHash);
        Assert.AreNotEqual(AdminPassword, admin.PasswordHash);
        Assert.AreNotEqual(EmployeePassword, employee.PasswordHash);
        Assert.IsTrue(await users.CheckPasswordAsync(admin, AdminPassword));
        Assert.IsTrue(await users.CheckPasswordAsync(employee, EmployeePassword));
        Assert.IsTrue(await users.IsInRoleAsync(admin, Roles.Admin));
        Assert.IsFalse(await users.IsInRoleAsync(admin, Roles.Employee));
        Assert.IsTrue(await users.IsInRoleAsync(employee, Roles.Employee));
        Assert.IsFalse(await users.IsInRoleAsync(employee, Roles.Admin));
    }

    [TestMethod]
    public async Task AdminLoginRedirectsToAdminConsole()
    {
        var response = await SignInAsync(AdminEmail, AdminPassword);

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual("/admin", response.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task EmployeeLoginRedirectsToConfiguredEmployeeArea()
    {
        var response = await SignInAsync(EmployeeEmail, EmployeePassword);

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual("/employee-access", response.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task InvalidCredentialsAreRejected()
    {
        var response = await SignInAsync(AdminEmail, "Wrong@Test123!");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        StringAssert.Contains(
            response.Headers.Location?.OriginalString,
            "error=invalid");
    }

    [TestMethod]
    public async Task IncompleteLoginIsRejected()
    {
        var response = await SignInAsync("", "");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        StringAssert.Contains(
            response.Headers.Location?.OriginalString,
            "error=missing");
    }

    [TestMethod]
    public async Task ProtectedPagesRequireAuthentication()
    {
        var admin = await _client.GetAsync("/admin");
        var employees = await _client.GetAsync("/admin/employees");

        Assert.AreEqual(HttpStatusCode.Redirect, admin.StatusCode);
        Assert.AreEqual(HttpStatusCode.Redirect, employees.StatusCode);
        Assert.AreEqual("/login", LocationPath(admin));
        Assert.AreEqual("/login", LocationPath(employees));
    }

    [TestMethod]
    public async Task AdminAuthenticationSurvivesNavigationBetweenProtectedPages()
    {
        var signIn = await SignInAsync(AdminEmail, AdminPassword);
        Assert.AreEqual(HttpStatusCode.Redirect, signIn.StatusCode);

        var admin = await _client.GetAsync("/admin");
        var employees = await _client.GetAsync("/admin/employees");

        Assert.AreEqual(HttpStatusCode.OK, admin.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, employees.StatusCode);
    }

    [TestMethod]
    public async Task LogoutClearsAccessToProtectedPages()
    {
        await SignInAsync(AdminEmail, AdminPassword);

        var token = await GetAntiforgeryTokenAsync("/admin");
        var logout = await PostAsync("/account/logout", token);

        Assert.AreEqual(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.AreEqual("/login", LocationPath(logout));

        var admin = await _client.GetAsync("/admin");

        Assert.AreEqual(HttpStatusCode.Redirect, admin.StatusCode);
        Assert.AreEqual("/login", LocationPath(admin));
    }

    [TestMethod]
    public async Task EmployeeCannotOpenAdminPagesByUrl()
    {
        await SignInAsync(EmployeeEmail, EmployeePassword);

        var admin = await _client.GetAsync("/admin");
        var employees = await _client.GetAsync("/admin/employees");

        Assert.AreEqual(HttpStatusCode.Redirect, admin.StatusCode);
        Assert.AreEqual(HttpStatusCode.Redirect, employees.StatusCode);
        Assert.AreEqual("/access-denied", LocationPath(admin));
        Assert.AreEqual("/access-denied", LocationPath(employees));
    }

    [TestMethod]
    public async Task AdminCanOpenAdminDashboardAndEmployeeManagement()
    {
        await SignInAsync(AdminEmail, AdminPassword);

        var admin = await _client.GetAsync("/admin");
        var employees = await _client.GetAsync("/admin/employees");

        Assert.AreEqual(HttpStatusCode.OK, admin.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, employees.StatusCode);
    }

    [TestMethod]
    public async Task AdminCanCreateEmployeeAndCreatedAccountReceivesEmployeeRole()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();
        var actor = CreatePrincipal(_admin, Roles.Admin);

        var result = await service.CreateAsync(
            actor,
            "New",
            "Hire",
            "new.hire@test.stockflow.local",
            "Hire@Test123!");

        Assert.IsTrue(result.Succeeded, result.Detail);
        Assert.AreEqual(EmployeeAccountService.Created, result.Code);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var hire = await users.FindByEmailAsync("new.hire@test.stockflow.local");

        Assert.IsNotNull(hire);
        Assert.IsTrue(await users.IsInRoleAsync(hire, Roles.Employee));
        Assert.IsFalse(await users.IsInRoleAsync(hire, Roles.Admin));
        Assert.IsTrue(await users.CheckPasswordAsync(hire, "Hire@Test123!"));
        Assert.AreNotEqual("Hire@Test123!", hire.PasswordHash);
    }

    [TestMethod]
    public async Task EmployeeCannotCreateOrDisableAccounts()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();
        var actor = CreatePrincipal(_employee, Roles.Employee);

        var create = await service.CreateAsync(
            actor,
            "Blocked",
            "Hire",
            "blocked.hire@test.stockflow.local",
            "Hire@Test123!");

        var disable = await service.SetActiveAsync(
            actor,
            _employee.Id,
            false);

        Assert.IsFalse(create.Succeeded);
        Assert.AreEqual(EmployeeAccountService.Forbidden, create.Code);
        Assert.IsFalse(disable.Succeeded);
        Assert.AreEqual(EmployeeAccountService.Forbidden, disable.Code);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.IsNull(await users.FindByEmailAsync("blocked.hire@test.stockflow.local"));

        var employee = await users.FindByIdAsync(_employee.Id);
        Assert.IsNotNull(employee);
        Assert.IsTrue(employee.IsActive);
    }

    [TestMethod]
    public async Task AdminCanUpdateEnableDisableAndDeleteEmployee()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();
        var actor = CreatePrincipal(_admin, Roles.Admin);

        var update = await service.UpdateAsync(
            actor,
            _employee.Id,
            "Updated",
            "Employee",
            "updated.employee@test.stockflow.local",
            null);

        Assert.IsTrue(update.Succeeded, update.Detail);
        Assert.AreEqual(EmployeeAccountService.Updated, update.Code);

        var disable = await service.SetActiveAsync(actor, _employee.Id, false);
        Assert.IsTrue(disable.Succeeded, disable.Detail);
        Assert.AreEqual(EmployeeAccountService.Disabled, disable.Code);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var updatedEmployee = await users.FindByIdAsync(_employee.Id);

        Assert.IsNotNull(updatedEmployee);
        Assert.IsFalse(updatedEmployee.IsActive);
        Assert.AreEqual("Updated", updatedEmployee.FirstName);
        Assert.AreEqual("updated.employee@test.stockflow.local", updatedEmployee.Email);

        var enable = await service.SetActiveAsync(actor, _employee.Id, true);
        Assert.IsTrue(enable.Succeeded, enable.Detail);
        Assert.AreEqual(EmployeeAccountService.Enabled, enable.Code);

        updatedEmployee = await users.FindByIdAsync(_employee.Id);
        Assert.IsNotNull(updatedEmployee);
        Assert.IsTrue(updatedEmployee.IsActive);

        var delete = await service.DeleteAsync(actor, _employee.Id);
        Assert.IsTrue(delete.Succeeded, delete.Detail);
        Assert.AreEqual(EmployeeAccountService.Deleted, delete.Code);

        Assert.IsNull(await users.FindByIdAsync(_employee.Id));
    }

    [TestMethod]
    public async Task DisabledEmployeeCannotSignIn()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<EmployeeAccountService>();
            var actor = CreatePrincipal(_admin, Roles.Admin);

            var result = await service.SetActiveAsync(actor, _employee.Id, false);

            Assert.IsTrue(result.Succeeded, result.Detail);
            Assert.AreEqual(EmployeeAccountService.Disabled, result.Code);
        }

        var response = await SignInAsync(EmployeeEmail, EmployeePassword);

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        StringAssert.Contains(
            response.Headers.Location?.OriginalString,
            "error=invalid");
    }

    [TestMethod]
    public async Task ExternalReturnUrlIsNotFollowed()
    {
        var response = await SignInAsync(
            AdminEmail,
            AdminPassword,
            "https://evil.example/phish");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual("/admin", response.Headers.Location?.OriginalString);
    }

    private async Task<ApplicationUser> CreateUserAsync(
        string email,
        string password,
        string firstName,
        string lastName,
        string role)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            FirstName = firstName,
            LastName = lastName,
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true
        };

        var created = await users.CreateAsync(user, password);
        Assert.IsTrue(
            created.Succeeded,
            string.Join(" ", created.Errors.Select(error => error.Description)));

        var assigned = await users.AddToRoleAsync(user, role);
        Assert.IsTrue(
            assigned.Succeeded,
            string.Join(" ", assigned.Errors.Select(error => error.Description)));

        return user;
    }

    private static ClaimsPrincipal CreatePrincipal(ApplicationUser user, string role)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.Email ?? user.UserName ?? string.Empty),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(ClaimTypes.Role, role)
            },
            "TestAuthentication");

        return new ClaimsPrincipal(identity);
    }

    private static string LocationPath(HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        Assert.IsNotNull(location);

        if (location.IsAbsoluteUri)
            return location.AbsolutePath;

        var value = location.OriginalString;
        var query = value.IndexOf('?', StringComparison.Ordinal);

        return query >= 0 ? value[..query] : value;
    }

    private async Task<HttpResponseMessage> SignInAsync(
        string email,
        string password,
        string? returnUrl = null)
    {
        var token = await GetAntiforgeryTokenAsync("/login");

        var fields = new Dictionary<string, string>
        {
            ["email"] = email,
            ["password"] = password
        };

        if (!string.IsNullOrWhiteSpace(returnUrl))
            fields["returnUrl"] = returnUrl;

        return await PostAsync("/account/login", token, fields);
    }

    private async Task<string> GetAntiforgeryTokenAsync(string path)
    {
        var response = await _client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

        if (!match.Success)
        {
            match = Regex.Match(
                html,
                "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"");
        }

        Assert.IsTrue(
            match.Success,
            $"Antiforgery token was not rendered on {path}. HTTP status: {(int)response.StatusCode}");

        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private Task<HttpResponseMessage> PostAsync(
        string path,
        string token,
        Dictionary<string, string>? fields = null)
    {
        var form = fields is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(fields);

        form["__RequestVerificationToken"] = token;

        return _client.PostAsync(path, new FormUrlEncodedContent(form));
    }


private sealed class StockFlowAppFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName =
        "stockflow_test_" + Guid.NewGuid().ToString("N");
    private readonly string _adminConnection;
    private readonly string _appConnection;
    private readonly string? _previousDefaultConnection;
    private bool _databaseCreated;
    public StockFlowAppFactory()
    {
        var testConnection = Environment.GetEnvironmentVariable(
            "STOCKFLOW_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(testConnection))
        {
            throw new InvalidOperationException(
                "STOCKFLOW_TEST_CONNECTION is not configured. Configure the dedicated PostgreSQL test runner connection before running tests.");
        }
        var builder = new NpgsqlConnectionStringBuilder(testConnection)
        {
            Database = "postgres",
            Pooling = false
        };
        _adminConnection = builder.ConnectionString;
        builder.Database = _databaseName;
        _appConnection = builder.ConnectionString;
        _previousDefaultConnection = Environment.GetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection");
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            _appConnection);
        CreateDatabase();
    }
    protected override void ConfigureWebHost(
        Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = _appConnection,
                    ["BootstrapAdmin:Email"] = string.Empty,
                    ["BootstrapAdmin:Password"] = string.Empty,
                    ["BootstrapAdmin:FirstName"] = string.Empty,
                    ["BootstrapAdmin:LastName"] = string.Empty,
                    ["BootstrapAdmin:ResetPassword"] = "false"
                });
        });
    }
    protected override void Dispose(bool disposing)
    {
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            if (disposing)
            {
                try
                {
                    if (_databaseCreated)
                    {
                        using var connection = new NpgsqlConnection(_adminConnection);
                        connection.Open();
                        using (var terminate = connection.CreateCommand())
                        {
                            terminate.CommandText = """
                                SELECT pg_terminate_backend(pid)
                                FROM pg_stat_activity
                                WHERE datname = @name
                                  AND pid <> pg_backend_pid();
                                """;
                            terminate.Parameters.AddWithValue("name", _databaseName);
                            terminate.ExecuteNonQuery();
                        }
                        using var drop = connection.CreateCommand();
                        drop.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
                        drop.ExecuteNonQuery();
                        _databaseCreated = false;
                    }
                }
                finally
                {
                    Environment.SetEnvironmentVariable(
                        "ConnectionStrings__DefaultConnection",
                        _previousDefaultConnection);
                }
            }
        }
    }
    private void CreateDatabase()
    {
        using var connection = new NpgsqlConnection(_adminConnection);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
        command.ExecuteNonQuery();
        _databaseCreated = true;
    }
}

}