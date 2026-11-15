
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using StockFlow.Data;
using StockFlow.Models;

namespace StockFlow.Components.Services;

public sealed record ProductListItem(
    int Id,
    string SKU,
    string Name,
    string? Description,
    decimal CostPrice,
    decimal SellingPrice,
    int CategoryId,
    string CategoryName,
    int QuantityInStock,
    int MinimumStockLevel,
    bool IsActive,
    bool IsLowStock);

public sealed record ProductActionResult(bool Succeeded, string Code, string Detail)
{
    public static ProductActionResult Ok(string code) => new(true, code, string.Empty);
    public static ProductActionResult Fail(string code, string detail) => new(false, code, detail);
}

public sealed class ProductService
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

    public ProductService(
        ApplicationDbContext db,
        IAuthorizationService authorization)
    {
        _db = db;
        _authorization = authorization;
    }

    public async Task<IReadOnlyList<ProductListItem>> ListAsync(
        ClaimsPrincipal actor,
        string? search = null,
        bool includeInactive = false)
    {
        if (!await IsEmployeeOrAdminAsync(actor))
            return [];

        var query = _db.Products
            .AsNoTracking()
            .Include(product => product.Category)
            .AsQueryable();

        if (!includeInactive || !actor.IsInRole("Admin"))
            query = query.Where(product => product.IsActive);

        var normalizedSearch = search?.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(product =>
                product.Name.ToLower().Contains(normalizedSearch.ToLower()) ||
                product.SKU.ToLower().Contains(normalizedSearch.ToLower()) ||
                product.Category.Name.ToLower().Contains(normalizedSearch.ToLower()));
        }

        return await query
            .OrderBy(product => product.Name)
            .Select(product => new ProductListItem(
                product.Id,
                product.SKU,
                product.Name,
                product.Description,
                product.CostPrice,
                product.SellingPrice,
                product.CategoryId,
                product.Category.Name,
                product.QuantityInStock,
                product.MinimumStockLevel,
                product.IsActive,
                product.QuantityInStock <= product.MinimumStockLevel))
            .ToListAsync();
    }

    public async Task<ProductActionResult> CreateAsync(
        ClaimsPrincipal actor,
        string? sku,
        string? name,
        string? description,
        decimal costPrice,
        decimal sellingPrice,
        int categoryId,
        int quantityInStock,
        int minimumStockLevel)
    {
        if (!await IsAdminAsync(actor))
            return ProductActionResult.Fail(Forbidden, "Only an administrator can create products.");

        var validation = await ValidateProductAsync(
            sku, name, description, costPrice, sellingPrice,
            categoryId, quantityInStock, minimumStockLevel);

        if (validation is not null)
            return validation;

        var normalizedSku = sku!.Trim().ToUpperInvariant();

        var duplicateSku = await _db.Products.AnyAsync(product =>
            product.SKU.ToLower() == normalizedSku.ToLower());

        if (duplicateSku)
            return ProductActionResult.Fail(Validation, "A product with this SKU already exists.");

        var product = new Product
        {
            SKU = normalizedSku,
            Name = name!.Trim(),
            Description = string.IsNullOrWhiteSpace(description?.Trim()) ? null : description.Trim(),
            CostPrice = costPrice,
            SellingPrice = sellingPrice,
            CategoryId = categoryId,
            QuantityInStock = quantityInStock,
            MinimumStockLevel = minimumStockLevel,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        return ProductActionResult.Ok(Created);
    }

   public async Task<ProductActionResult> UpdateAsync(
        ClaimsPrincipal actor,
        int productId,
        string? sku,
        string? name,
        string? description,
        decimal costPrice,
        decimal sellingPrice,
        int categoryId,
        int minimumStockLevel)
    {
        if (!await IsAdminAsync(actor))
            return ProductActionResult.Fail(Forbidden, "Only an administrator can update products.");

        var product = await _db.Products.FindAsync(productId);

        if (product is null)
            return ProductActionResult.Fail(NotFound, "Product was not found.");

        var validation = await ValidateProductAsync(
            sku, name, description, costPrice, sellingPrice,
            categoryId, product.QuantityInStock, minimumStockLevel);

        if (validation is not null)
            return validation;

        var normalizedSku = sku!.Trim().ToUpperInvariant();

        var duplicateSku = await _db.Products.AnyAsync(existing =>
            existing.Id != productId &&
            existing.SKU.ToLower() == normalizedSku.ToLower());

        if (duplicateSku)
            return ProductActionResult.Fail(Validation, "Another product already uses this SKU.");

        product.SKU = normalizedSku;
        product.Name = name!.Trim();
        product.Description = string.IsNullOrWhiteSpace(description?.Trim()) ? null : description.Trim();
        product.CostPrice = costPrice;
        product.SellingPrice = sellingPrice;
        product.CategoryId = categoryId;
        product.MinimumStockLevel = minimumStockLevel;
        product.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return ProductActionResult.Ok(Updated);
    }

    public async Task<ProductActionResult> SetActiveAsync(
        ClaimsPrincipal actor,
        int productId,
        bool isActive)
    {
        if (!await IsAdminAsync(actor))
            return ProductActionResult.Fail(Forbidden, "Only an administrator can activate or deactivate products.");

        var product = await _db.Products.FindAsync(productId);

        if (product is null)
            return ProductActionResult.Fail(NotFound, "Product was not found.");

        if (product.IsActive == isActive)
            return ProductActionResult.Ok(isActive ? Activated : Deactivated);

        product.IsActive = isActive;
        product.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return ProductActionResult.Ok(isActive ? Activated : Deactivated);
    }

    private async Task<ProductActionResult?> ValidateProductAsync(
        string? sku,
        string? name,
        string? description,
        decimal costPrice,
        decimal sellingPrice,
        int categoryId,
        int quantityInStock,
        int minimumStockLevel)
    {
        var normalizedSku = sku?.Trim() ?? string.Empty;
        var normalizedName = name?.Trim() ?? string.Empty;
        var normalizedDescription = description?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedSku))
            return ProductActionResult.Fail(Validation, "Product SKU is required.");

        if (normalizedSku.Length > 50)
            return ProductActionResult.Fail(Validation, "Product SKU cannot exceed 50 characters.");

        if (string.IsNullOrWhiteSpace(normalizedName))
            return ProductActionResult.Fail(Validation, "Product name is required.");

        if (normalizedName.Length > 150)
            return ProductActionResult.Fail(Validation, "Product name cannot exceed 150 characters.");

        if (normalizedDescription?.Length > 1000)
            return ProductActionResult.Fail(Validation, "Product description cannot exceed 1000 characters.");

        if (costPrice < 0)
            return ProductActionResult.Fail(Validation, "Cost price cannot be negative.");

        if (sellingPrice < 0)
            return ProductActionResult.Fail(Validation, "Selling price cannot be negative.");

        if (quantityInStock < 0)
            return ProductActionResult.Fail(Validation, "Stock quantity cannot be negative.");

        if (minimumStockLevel < 0)
            return ProductActionResult.Fail(Validation, "Minimum stock level cannot be negative.");

        var categoryExists = await _db.Categories.AnyAsync(category =>
            category.Id == categoryId && category.IsActive);

        if (!categoryExists)
            return ProductActionResult.Fail(Validation, "Select a valid active product category.");

        return null;
    }

    private async Task<bool> IsAdminAsync(ClaimsPrincipal actor)
    {
        var result = await _authorization.AuthorizeAsync(
            actor,
            StockFlowAuthorization.AdminPolicy);

        return result.Succeeded;
    }

    private async Task<bool> IsEmployeeOrAdminAsync(ClaimsPrincipal actor)
    {
        var result = await _authorization.AuthorizeAsync(
            actor,
            StockFlowAuthorization.EmployeePolicy);

        return result.Succeeded;
    }
}
