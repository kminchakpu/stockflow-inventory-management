
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using StockFlow.Data;
using StockFlow.Models;

namespace StockFlow.Components.Services;

public sealed record CustomerListItem(
    int Id,
    string FirstName,
    string LastName,
    string? Email,
    string PhoneNumber,
    string? CompanyName,
    string? Address,
    string? Notes,
    bool IsActive,
    DateTime CreatedAt);

public sealed record CustomerActionResult(bool Succeeded, string Code, string Detail)
{
    public static CustomerActionResult Ok(string code) => new(true, code, string.Empty);
    public static CustomerActionResult Fail(string code, string detail) => new(false, code, detail);
}

public sealed class CustomerService
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Activated = "activated";
    public const string Deactivated = "deactivated";
    public const string Validation = "validation";
    public const string NotFound = "notfound";
    public const string Forbidden = "forbidden";

    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _authorization;

    public CustomerService(
        ApplicationDbContext db,
        IAuthorizationService authorization)
    {
        _db = db;
        _authorization = authorization;
    }

    public async Task<IReadOnlyList<CustomerListItem>> ListAsync(
        ClaimsPrincipal actor,
        bool includeInactive = false,
        string? searchTerm = null)
    {
        if (!await IsAdminAsync(actor))
            return [];

        var query = _db.Customers.AsNoTracking();

        if (!includeInactive)
            query = query.Where(customer => customer.IsActive);

        var normalizedSearch = searchTerm?.Trim().ToLower();

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(customer =>
                customer.FirstName.ToLower().Contains(normalizedSearch) ||
                customer.LastName.ToLower().Contains(normalizedSearch) ||
                (customer.Email != null && customer.Email.ToLower().Contains(normalizedSearch)) ||
                customer.PhoneNumber.Contains(normalizedSearch) ||
                (customer.CompanyName != null && customer.CompanyName.ToLower().Contains(normalizedSearch)));
        }

        return await query
            .OrderBy(customer => customer.LastName)
            .ThenBy(customer => customer.FirstName)
            .Select(customer => new CustomerListItem(
                customer.Id,
                customer.FirstName,
                customer.LastName,
                customer.Email,
                customer.PhoneNumber,
                customer.CompanyName,
                customer.Address,
                customer.Notes,
                customer.IsActive,
                customer.CreatedAt))
            .ToListAsync();
    }

    public async Task<CustomerActionResult> CreateAsync(
        ClaimsPrincipal actor,
        string? firstName,
        string? lastName,
        string? email,
        string? phoneNumber,
        string? companyName,
        string? address,
        string? notes)
    {
        if (!await IsAdminAsync(actor))
            return CustomerActionResult.Fail(Forbidden, "Only an administrator can create customers.");

        var validation = ValidateCustomer(
            firstName,
            lastName,
            email,
            phoneNumber,
            companyName,
            address,
            notes);

        if (validation is not null)
            return CustomerActionResult.Fail(Validation, validation);

        var normalizedFirstName = firstName!.Trim();
        var normalizedLastName = lastName!.Trim();
        var normalizedEmail = NormalizeOptional(email);
        var normalizedPhone = phoneNumber!.Trim();

        var duplicatePhone = await _db.Customers
            .AnyAsync(customer => customer.PhoneNumber == normalizedPhone);

        if (duplicatePhone)
            return CustomerActionResult.Fail(Validation, "A customer with this phone number already exists.");

        if (normalizedEmail is not null)
        {
            var duplicateEmail = await _db.Customers
                .AnyAsync(customer =>
                    customer.Email != null &&
                    customer.Email.ToLower() == normalizedEmail.ToLower());

            if (duplicateEmail)
                return CustomerActionResult.Fail(Validation, "A customer with this email address already exists.");
        }

        var customer = new Customer
        {
            FirstName = normalizedFirstName,
            LastName = normalizedLastName,
            Email = normalizedEmail,
            PhoneNumber = normalizedPhone,
            CompanyName = NormalizeOptional(companyName),
            Address = NormalizeOptional(address),
            Notes = NormalizeOptional(notes),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        return CustomerActionResult.Ok(Created);
    }

    public async Task<CustomerActionResult> UpdateAsync(
        ClaimsPrincipal actor,
        int customerId,
        string? firstName,
        string? lastName,
        string? email,
        string? phoneNumber,
        string? companyName,
        string? address,
        string? notes)
    {
        if (!await IsAdminAsync(actor))
            return CustomerActionResult.Fail(Forbidden, "Only an administrator can update customers.");

        var customer = await _db.Customers.FindAsync(customerId);

        if (customer is null)
            return CustomerActionResult.Fail(NotFound, "Customer was not found.");

        var validation = ValidateCustomer(
            firstName,
            lastName,
            email,
            phoneNumber,
            companyName,
            address,
            notes);

        if (validation is not null)
            return CustomerActionResult.Fail(Validation, validation);

        var normalizedFirstName = firstName!.Trim();
        var normalizedLastName = lastName!.Trim();
        var normalizedEmail = NormalizeOptional(email);
        var normalizedPhone = phoneNumber!.Trim();

        var duplicatePhone = await _db.Customers
            .AnyAsync(existing =>
                existing.Id != customerId &&
                existing.PhoneNumber == normalizedPhone);

        if (duplicatePhone)
            return CustomerActionResult.Fail(Validation, "Another customer already uses this phone number.");

        if (normalizedEmail is not null)
        {
            var duplicateEmail = await _db.Customers
                .AnyAsync(existing =>
                    existing.Id != customerId &&
                    existing.Email != null &&
                    existing.Email.ToLower() == normalizedEmail.ToLower());

            if (duplicateEmail)
                return CustomerActionResult.Fail(Validation, "Another customer already uses this email address.");
        }

        customer.FirstName = normalizedFirstName;
        customer.LastName = normalizedLastName;
        customer.Email = normalizedEmail;
        customer.PhoneNumber = normalizedPhone;
        customer.CompanyName = NormalizeOptional(companyName);
        customer.Address = NormalizeOptional(address);
        customer.Notes = NormalizeOptional(notes);
        customer.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return CustomerActionResult.Ok(Updated);
    }

    public async Task<CustomerActionResult> SetActiveAsync(
        ClaimsPrincipal actor,
        int customerId,
        bool isActive)
    {
        if (!await IsAdminAsync(actor))
            return CustomerActionResult.Fail(Forbidden, "Only an administrator can activate or deactivate customers.");

        var customer = await _db.Customers.FindAsync(customerId);

        if (customer is null)
            return CustomerActionResult.Fail(NotFound, "Customer was not found.");

        if (customer.IsActive == isActive)
            return CustomerActionResult.Ok(isActive ? Activated : Deactivated);

        customer.IsActive = isActive;
        customer.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return CustomerActionResult.Ok(isActive ? Activated : Deactivated);
    }

    private static string? ValidateCustomer(
        string? firstName,
        string? lastName,
        string? email,
        string? phoneNumber,
        string? companyName,
        string? address,
        string? notes)
    {
        var normalizedFirstName = firstName?.Trim() ?? string.Empty;
        var normalizedLastName = lastName?.Trim() ?? string.Empty;
        var normalizedEmail = NormalizeOptional(email);
        var normalizedPhone = phoneNumber?.Trim() ?? string.Empty;
        var normalizedCompanyName = NormalizeOptional(companyName);
        var normalizedAddress = NormalizeOptional(address);
        var normalizedNotes = NormalizeOptional(notes);

        if (string.IsNullOrWhiteSpace(normalizedFirstName))
            return "First name is required.";

        if (normalizedFirstName.Length > 100)
            return "First name cannot exceed 100 characters.";

        if (string.IsNullOrWhiteSpace(normalizedLastName))
            return "Last name is required.";

        if (normalizedLastName.Length > 100)
            return "Last name cannot exceed 100 characters.";

        if (string.IsNullOrWhiteSpace(normalizedPhone))
            return "Phone number is required.";

        if (normalizedPhone.Length > 30)
            return "Phone number cannot exceed 30 characters.";

        if (normalizedEmail?.Length > 254)
            return "Email address cannot exceed 254 characters.";

        if (normalizedEmail is not null &&
            !new EmailAddressAttribute().IsValid(normalizedEmail))
            return "Enter a valid email address.";

        if (normalizedCompanyName?.Length > 150)
            return "Company name cannot exceed 150 characters.";

        if (normalizedAddress?.Length > 500)
            return "Address cannot exceed 500 characters.";

        if (normalizedNotes?.Length > 1000)
            return "Notes cannot exceed 1000 characters.";

        return null;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private async Task<bool> IsAdminAsync(ClaimsPrincipal actor)
    {
        var result = await _authorization.AuthorizeAsync(
            actor,
            StockFlowAuthorization.AdminPolicy);

        return result.Succeeded;
    }
}
