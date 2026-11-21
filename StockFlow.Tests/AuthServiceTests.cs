
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using StockFlow.Components.Services;
using StockFlow.Data;
using StockFlow.Models;

namespace StockFlow.Tests;

[TestClass]
public partial class AuthenticationAuthorizationTests
{
    private const string AdminEmail = "admin@stockflow.local";
    private const string AdminPassword = "Admin@123456";
    private const string EmployeeEmail = "employee@stockflow.local";
    private const string EmployeePassword = "Employee@123456";
    private StockFlowAppFactory _factory = null!;
    private HttpClient _client = null!;

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
        await SeedTestUsersAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    private async Task SeedTestUsersAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roles.RoleExistsAsync(AppRoles.Admin))
        {
            var result = await roles.CreateAsync(new IdentityRole(AppRoles.Admin));
            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        if (!await roles.RoleExistsAsync(AppRoles.Employee))
        {
            var result = await roles.CreateAsync(new IdentityRole(AppRoles.Employee));
            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await CreateTestUserAsync(users, AdminEmail, AdminPassword, "Test", "Administrator", AppRoles.Admin);
        await CreateTestUserAsync(users, EmployeeEmail, EmployeePassword, "Test", "Employee", AppRoles.Employee);
    }

    private static async Task CreateTestUserAsync(
        UserManager<ApplicationUser> users,
        string email,
        string password,
        string firstName,
        string lastName,
        string role)
    {
        var existingUser = await users.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
            IsActive = true
        };

        var createResult = await users.CreateAsync(user, password);
        Assert.IsTrue(
            createResult.Succeeded,
            $"Could not create test user {email}: {string.Join("; ", createResult.Errors.Select(e => e.Description))}");

        var roleResult = await users.AddToRoleAsync(user, role);
        Assert.IsTrue(
            roleResult.Succeeded,
            $"Could not assign role {role} to {email}: {string.Join("; ", roleResult.Errors.Select(e => e.Description))}");
    }

    [TestMethod]
    public async Task SeededPasswordsAreHashedAndRolesAreAssigned()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var admin = await users.FindByEmailAsync(AdminEmail);
        var employee = await users.FindByEmailAsync(EmployeeEmail);

        Assert.IsNotNull(admin);
        Assert.IsNotNull(employee);
        Assert.IsNotNull(admin!.PasswordHash);
        Assert.IsNotNull(employee!.PasswordHash);

        Assert.IsFalse(admin.PasswordHash.Contains(AdminPassword, StringComparison.Ordinal));
        Assert.IsFalse(employee.PasswordHash.Contains(EmployeePassword, StringComparison.Ordinal));

        Assert.IsTrue(await users.CheckPasswordAsync(admin, AdminPassword));
        Assert.IsTrue(await users.CheckPasswordAsync(employee, EmployeePassword));

        Assert.IsTrue(await users.IsInRoleAsync(admin, AppRoles.Admin));
        Assert.IsFalse(await users.IsInRoleAsync(admin, AppRoles.Employee));
        Assert.IsTrue(await users.IsInRoleAsync(employee, AppRoles.Employee));
        Assert.IsFalse(await users.IsInRoleAsync(employee, AppRoles.Admin));
    }

    [TestMethod]
    public async Task AdminLoginRedirectsToAdminConsole()
    {
        var response = await SignInAsync(AdminEmail, AdminPassword);

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual("/admin", response.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task EmployeeLoginRedirectsToEmployeeAccess()
    {
        var response = await SignInAsync(EmployeeEmail, EmployeePassword);

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual("/employee-access", response.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task InvalidPasswordDoesNotAuthenticateUser()
    {
        var response = await SignInAsync(AdminEmail, "WrongPassword@123");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        StringAssert.Contains(response.Headers.Location?.OriginalString, "error");

        var admin = await _client.GetAsync("/admin");
        Assert.AreEqual("/login", LocationPath(admin));
    }

    [TestMethod]
    public async Task AnonymousUserCannotAccessAdminConsole()
    {
        var response = await _client.GetAsync("/admin");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual("/login", LocationPath(response));
    }

    [TestMethod]
    public async Task AnonymousUserCannotAccessEmployeeArea()
    {
        var response = await _client.GetAsync("/employee-access");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual("/login", LocationPath(response));
    }

    [TestMethod]
    public async Task EmployeeCannotAccessAdminConsole()
    {
        await SignInAsync(EmployeeEmail, EmployeePassword);

        var response = await _client.GetAsync("/admin");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual("/access-denied", LocationPath(response));
    }

    [TestMethod]
    public async Task AdminCanAccessAdminConsole()
    {
        await SignInAsync(AdminEmail, AdminPassword);

        var response = await _client.GetAsync("/admin");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task EmployeeCanAccessEmployeeArea()
    {
        await SignInAsync(EmployeeEmail, EmployeePassword);

        var response = await _client.GetAsync("/employee-access");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task LoginRejectsMissingCredentials()
    {
        var token = await GetAntiforgeryTokenAsync("/login");

        var response = await PostAsync("/account/login", token, new Dictionary<string, string>
        {
            ["email"] = "",
            ["password"] = ""
        });

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        StringAssert.Contains(response.Headers.Location?.OriginalString, "error=missing");
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

    private static string LocationPath(HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        Assert.IsNotNull(location);

        if (location.IsAbsoluteUri)
        {
            return location.AbsolutePath;
        }

        var value = location.OriginalString;
        var queryIndex = value.IndexOf('?');

        return queryIndex >= 0 ? value[..queryIndex] : value;
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
        {
            fields["returnUrl"] = returnUrl;
        }

        return await PostAsync("/account/login", token, fields);
    }

    private async Task<string> GetAntiforgeryTokenAsync(string path)
    {
        var response = await _client.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(
            response.IsSuccessStatusCode,
            $"GET {path} failed with status {(int)response.StatusCode}. Response: {content}");

        var match = Regex.Match(
            content,
            "<input\\b(?=[^>]*\\bname=\"__RequestVerificationToken\")(?=[^>]*\\bvalue=\"([^\"]+)\")[^>]*>",
            RegexOptions.IgnoreCase);

        Assert.IsTrue(
            match.Success,
            $"Antiforgery token was not found on {path}. Response: {content}");

        return match.Groups[1].Value;
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
        private readonly string _databaseName = "stockflow_test_" + Guid.NewGuid().ToString("N");
        private readonly string _adminConnection;
        private readonly string _appConnection;

        public StockFlowAppFactory()
        {
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets<Program>()
                .Build();

            var applicationConnection = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection was not found in StockFlow User Secrets.");

            var adminConnection = configuration["StockFlowTests:DatabaseAdminConnection"]
                ?? throw new InvalidOperationException(
                    "StockFlowTests:DatabaseAdminConnection was not found in StockFlow User Secrets.");

            var adminBuilder = new NpgsqlConnectionStringBuilder(adminConnection)
            {
                Database = "postgres"
            };

            _adminConnection = adminBuilder.ConnectionString;

            var appBuilder = new NpgsqlConnectionStringBuilder(applicationConnection)
            {
                Database = _databaseName
            };

            _appConnection = appBuilder.ConnectionString;

            var databaseOwner = appBuilder.Username;
            if (string.IsNullOrWhiteSpace(databaseOwner))
            {
                throw new InvalidOperationException(
                    "The PostgreSQL application username is missing from the connection string.");
            }

            CreateDatabase(databaseOwner);
            MigrateDatabase();
        }

        protected override void ConfigureWebHost(
            Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                _appConnection);
        }

        private void MigrateDatabase()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(_appConnection)
                .Options;

            using var context = new ApplicationDbContext(options);
            context.Database.Migrate();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing)
            {
                return;
            }

            try
            {
                using var connection = new NpgsqlConnection(_adminConnection);
                connection.Open();

                using (var terminate = connection.CreateCommand())
                {
                    terminate.CommandText = """
                        SELECT pg_terminate_backend(pid)
                        FROM pg_stat_activity
                        WHERE datname = @name AND pid <> pg_backend_pid();
                        """;

                    terminate.Parameters.AddWithValue("name", _databaseName);
                    terminate.ExecuteNonQuery();
                }

                using var drop = connection.CreateCommand();
                drop.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
                drop.ExecuteNonQuery();
            }
            catch (NpgsqlException exception)
            {
                Console.WriteLine(
                    $"Test database cleanup failed: {exception.Message}");
            }
        }

        private void CreateDatabase(string owner)
        {
            using var connection = new NpgsqlConnection(_adminConnection);
            connection.Open();

            var safeOwner = owner.Replace("\"", "\"\"");

            using var command = connection.CreateCommand();
            command.CommandText =
                $"CREATE DATABASE \"{_databaseName}\" OWNER \"{safeOwner}\"";

            command.ExecuteNonQuery();
        }
    }
}
