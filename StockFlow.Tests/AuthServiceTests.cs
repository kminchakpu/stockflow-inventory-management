using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Components.Services;
using StockFlow.Data;

namespace StockFlow.Tests;

[TestClass]
public class AuthenticationAuthorizationTests
{
    private StockFlowAppFactory _factory = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public void Initialize()
    {
        _factory = new StockFlowAppFactory();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task SeededPasswordsAreHashedAndRolesAreAssigned()
    {
        await _client.GetAsync("/login");

        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var admin = await users.FindByEmailAsync("admin@stockflow.local");
        var employee = await users.FindByEmailAsync("employee@stockflow.local");

        Assert.IsNotNull(admin);
        Assert.IsNotNull(employee);
        Assert.IsFalse(admin!.PasswordHash!.Contains("Admin@123", StringComparison.Ordinal));
        Assert.IsFalse(employee!.PasswordHash!.Contains("Employee@123", StringComparison.Ordinal));
        Assert.IsTrue(await users.IsInRoleAsync(admin, AppRoles.Admin));
        Assert.IsFalse(await users.IsInRoleAsync(admin, AppRoles.Employee));
        Assert.IsTrue(await users.IsInRoleAsync(employee, AppRoles.Employee));
        Assert.IsFalse(await users.IsInRoleAsync(employee, AppRoles.Admin));
    }

    [TestMethod]
    public async Task AdminLoginRedirectsToAdminConsole()
    {
        var response = await SignInAsync("admin@stockflow.local", "Admin@123");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual("/admin", response.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task EmployeeLoginRedirectsToEmployeeArea()
    {
        var response = await SignInAsync("employee@stockflow.local", "Employee@123");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual("/employee-access", response.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task InvalidCredentialsAreRejectedWithoutRevealingWhichFieldFailed()
    {
        var response = await SignInAsync("admin@stockflow.local", "wrong-pass");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        StringAssert.Contains(response.Headers.Location?.OriginalString, "error=invalid");
    }

    [TestMethod]
    public async Task IncompleteLoginIsRejected()
    {
        var response = await SignInAsync("", "");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        StringAssert.Contains(response.Headers.Location?.OriginalString, "error=missing");
    }

    [TestMethod]
    public async Task ProtectedPagesRequireAuthentication()
    {
        var dashboard = await _client.GetAsync("/dashboard");
        var admin = await _client.GetAsync("/admin");

        Assert.AreEqual(HttpStatusCode.Redirect, dashboard.StatusCode);
        Assert.AreEqual(HttpStatusCode.Redirect, admin.StatusCode);
        Assert.AreEqual("/login", LocationPath(dashboard));
        Assert.AreEqual("/login", LocationPath(admin));
    }

    [TestMethod]
    public async Task AuthenticationStateSurvivesNavigation()
    {
        await SignInAsync("employee@stockflow.local", "Employee@123");

        var dashboard = await _client.GetAsync("/dashboard");
        var employeeArea = await _client.GetAsync("/employee-access");

        Assert.AreEqual(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, employeeArea.StatusCode);
    }

    [TestMethod]
    public async Task LogoutClearsAccessToProtectedPages()
    {
        await SignInAsync("admin@stockflow.local", "Admin@123");
        var token = await GetAntiforgeryTokenAsync("/admin");
        var logout = await PostAsync("/account/logout", token);

        Assert.AreEqual(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.AreEqual("/login", logout.Headers.Location?.OriginalString);

        var dashboard = await _client.GetAsync("/dashboard");
        Assert.AreEqual(HttpStatusCode.Redirect, dashboard.StatusCode);
        Assert.AreEqual("/login", LocationPath(dashboard));
    }

    [TestMethod]
    public async Task EmployeeCannotOpenAdminPagesByUrl()
    {
        await SignInAsync("employee@stockflow.local", "Employee@123");

        var admin = await _client.GetAsync("/admin");
        var employees = await _client.GetAsync("/admin/employees");

        Assert.AreEqual(HttpStatusCode.Redirect, admin.StatusCode);
        Assert.AreEqual(HttpStatusCode.Redirect, employees.StatusCode);
        Assert.AreEqual("/access-denied", LocationPath(admin));
        Assert.AreEqual("/access-denied", LocationPath(employees));
    }

    [TestMethod]
    public async Task AdminCanOpenEmployeeAreaAndEmployeeManagement()
    {
        await SignInAsync("admin@stockflow.local", "Admin@123");

        var admin = await _client.GetAsync("/admin");
        var employees = await _client.GetAsync("/admin/employees");
        var employeeArea = await _client.GetAsync("/employee-access");

        Assert.AreEqual(HttpStatusCode.OK, admin.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, employees.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, employeeArea.StatusCode);
        var html = await employees.Content.ReadAsStringAsync();
        StringAssert.Contains(html, "Aisha Brand");
    }

    [TestMethod]
    public async Task AdminCanCreateEmployeeAndThatAccountReceivesTheEmployeeRole()
    {
        await SignInAsync("admin@stockflow.local", "Admin@123");
        var token = await GetAntiforgeryTokenAsync("/admin/employees");
        var created = await PostAsync("/admin/employees", token, new Dictionary<string, string>
        {
            ["displayName"] = "New Hire",
            ["email"] = "new.hire@stockflow.local",
            ["password"] = "Hire@1234"
        });

        Assert.AreEqual(HttpStatusCode.Redirect, created.StatusCode);
        StringAssert.Contains(created.Headers.Location?.OriginalString, "notice=created");

        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var hire = await users.FindByEmailAsync("new.hire@stockflow.local");
        Assert.IsNotNull(hire);
        Assert.IsTrue(await users.IsInRoleAsync(hire!, AppRoles.Employee));
        Assert.IsFalse(await users.IsInRoleAsync(hire, AppRoles.Admin));
        Assert.IsFalse(hire.PasswordHash!.Contains("Hire@1234", StringComparison.Ordinal));

        _client.Dispose();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var signIn = await SignInAsync("new.hire@stockflow.local", "Hire@1234");
        Assert.AreEqual("/employee-access", signIn.Headers.Location?.OriginalString);

        var admin = await _client.GetAsync("/admin");
        Assert.AreEqual("/access-denied", LocationPath(admin));
    }

    [TestMethod]
    public async Task EmployeeCannotCreateOrDisableAccounts()
    {
        await SignInAsync("employee@stockflow.local", "Employee@123");
        var token = await GetAntiforgeryTokenAsync("/employee-access");
        var create = await PostAsync("/admin/employees", token, new Dictionary<string, string>
        {
            ["displayName"] = "Blocked Hire",
            ["email"] = "blocked.hire@stockflow.local",
            ["password"] = "Hire@1234"
        });

        Assert.AreEqual(HttpStatusCode.Redirect, create.StatusCode);
        Assert.AreEqual("/access-denied", LocationPath(create));

        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.IsNull(await users.FindByEmailAsync("blocked.hire@stockflow.local"));
    }

    [TestMethod]
    public async Task DisabledEmployeeCannotSignIn()
    {
        await SignInAsync("admin@stockflow.local", "Admin@123");
        string employeeId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var employee = await users.FindByEmailAsync("employee@stockflow.local");
            employeeId = employee!.Id;
        }

        var token = await GetAntiforgeryTokenAsync("/admin/employees");
        var disabled = await PostAsync("/admin/employees/active", token, new Dictionary<string, string>
        {
            ["userId"] = employeeId,
            ["isActive"] = "false"
        });
        StringAssert.Contains(disabled.Headers.Location?.OriginalString, "notice=disabled");

        _client.Dispose();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var signIn = await SignInAsync("employee@stockflow.local", "Employee@123");
        StringAssert.Contains(signIn.Headers.Location?.OriginalString, "error=disabled");
    }

    [TestMethod]
    public async Task ExternalReturnUrlIsNotFollowed()
    {
        var response = await SignInAsync("admin@stockflow.local", "Admin@123", "https://evil.example/phish");

        Assert.AreEqual("/dashboard", response.Headers.Location?.OriginalString);
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
        var query = value.IndexOf('?', StringComparison.Ordinal);
        return query >= 0 ? value[..query] : value;
    }

    private async Task<HttpResponseMessage> SignInAsync(string email, string password, string? returnUrl = null)
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
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!match.Success)
        {
            match = Regex.Match(html, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"");
        }

        Assert.IsTrue(match.Success, "Antiforgery token was not rendered.");
        return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private Task<HttpResponseMessage> PostAsync(string path, string token, Dictionary<string, string>? fields = null)
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
            var settingsPath = FindLocalSettings();
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            var connection = document.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()
                ?? throw new InvalidOperationException("appsettings.Local.json is missing ConnectionStrings:DefaultConnection.");
            var builder = new NpgsqlConnectionStringBuilder(connection)
            {
                Database = "postgres"
            };
            _adminConnection = builder.ConnectionString;
            builder.Database = _databaseName;
            _appConnection = builder.ConnectionString;
            CreateDatabase();
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", _appConnection);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
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
            catch (NpgsqlException)
            {
            }
        }

        private void CreateDatabase()
        {
            using var connection = new NpgsqlConnection(_adminConnection);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
            command.ExecuteNonQuery();
        }

        private static string FindLocalSettings()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "appsettings.Local.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Could not find appsettings.Local.json.");
        }
    }
}
