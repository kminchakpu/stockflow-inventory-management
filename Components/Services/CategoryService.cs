
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using StockFlow.Data;
using StockFlow.Models;

namespace StockFlow.Components.Services;

public sealed record CategoryListItem(
    int Id,
    string Name,
    string? Description,
    bool IsActive,
    int ProductCount);

public sealed record CategoryActionResult(bool Succeeded, string Code, string Detail)
{
    public static CategoryActionResult Ok(string code) => new(true, code, string.Empty);
    public static CategoryActionResult Fail(string code, string detail) => new(false, code, detail);
}

public sealed class CategoryService
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

    public CategoryService(
        ApplicationDbContext db,
        IAuthorizationService authorization)
    {
        _db = db;
        _authorization = authorization;
    }

    public async Task<IReadOnlyList<CategoryListItem>> ListAsync(
        ClaimsPrincipal actor,
        bool includeInactive = false)
    {
        if (!await IsAdminAsync(actor))
            return [];

        var query = _db.Categories.AsNoTracking();

        if (!includeInactive)
            query = query.Where(category => category.IsActive);

        return await query
            .OrderBy(category => category.Name)
            .Select(category => new CategoryListItem(
                category.Id,
                category.Name,
                category.Description,
                category.IsActive,
                _db.Products.Count(product => product.CategoryId == category.Id)))
            .ToListAsync();
    }

    public async Task<CategoryActionResult> CreateAsync(
        ClaimsPrincipal actor,
        string? name,
        string? description)
    {
        if (!await IsAdminAsync(actor))
            return CategoryActionResult.Fail(Forbidden, "Only an administrator can create categories.");

        var normalizedName = name?.Trim() ?? string.Empty;
        var normalizedDescription = description?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedName))
            return CategoryActionResult.Fail(Validation, "Category name is required.");

        if (normalizedName.Length > 100)
            return CategoryActionResult.Fail(Validation, "Category name cannot exceed 100 characters.");

        if (normalizedDescription?.Length > 500)
            return CategoryActionResult.Fail(Validation, "Category description cannot exceed 500 characters.");

        var existing = await _db.Categories
            .AnyAsync(category => category.Name.ToLower() == normalizedName.ToLower());

        if (existing)
            return CategoryActionResult.Fail(Validation, "A category with this name already exists.");

        var category = new Category
        {
            Name = normalizedName,
            Description = string.IsNullOrWhiteSpace(normalizedDescription) ? null : normalizedDescription,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        return CategoryActionResult.Ok(Created);
    }

    public async Task<CategoryActionResult> UpdateAsync(
        ClaimsPrincipal actor,
        int categoryId,
        string? name,
        string? description)
    {
        if (!await IsAdminAsync(actor))
            return CategoryActionResult.Fail(Forbidden, "Only an administrator can update categories.");

        var category = await _db.Categories.FindAsync(categoryId);

        if (category is null)
            return CategoryActionResult.Fail(NotFound, "Category was not found.");

        var normalizedName = name?.Trim() ?? string.Empty;
        var normalizedDescription = description?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedName))
            return CategoryActionResult.Fail(Validation, "Category name is required.");

        if (normalizedName.Length > 100)
            return CategoryActionResult.Fail(Validation, "Category name cannot exceed 100 characters.");

        if (normalizedDescription?.Length > 500)
            return CategoryActionResult.Fail(Validation, "Category description cannot exceed 500 characters.");

        var duplicate = await _db.Categories.AnyAsync(existing =>
            existing.Id != categoryId &&
            existing.Name.ToLower() == normalizedName.ToLower());

        if (duplicate)
            return CategoryActionResult.Fail(Validation, "Another category already uses this name.");

        category.Name = normalizedName;
        category.Description = string.IsNullOrWhiteSpace(normalizedDescription) ? null : normalizedDescription;
        category.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return CategoryActionResult.Ok(Updated);
    }

    public async Task<CategoryActionResult> SetActiveAsync(
        ClaimsPrincipal actor,
        int categoryId,
        bool isActive)
    {
        if (!await IsAdminAsync(actor))
            return CategoryActionResult.Fail(Forbidden, "Only an administrator can activate or deactivate categories.");

        var category = await _db.Categories.FindAsync(categoryId);

        if (category is null)
            return CategoryActionResult.Fail(NotFound, "Category was not found.");

        if (category.IsActive == isActive)
            return CategoryActionResult.Ok(isActive ? Activated : Deactivated);

        category.IsActive = isActive;
        category.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return CategoryActionResult.Ok(isActive ? Activated : Deactivated);
    }

    private async Task<bool> IsAdminAsync(ClaimsPrincipal actor)
    {
        var result = await _authorization.AuthorizeAsync(
            actor,
            StockFlowAuthorization.AdminPolicy);

        return result.Succeeded;
    }
}
