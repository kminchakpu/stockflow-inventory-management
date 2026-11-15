
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using StockFlow.Data;
using StockFlow.Models;

namespace StockFlow.Components.Services;

public sealed record InventoryTransactionListItem(
    int Id,
    int ProductId,
    string ProductName,
    string SKU,
    InventoryTransactionType TransactionType,
    int Quantity,
    int PreviousQuantity,
    int NewQuantity,
    string? Notes,
    DateTime CreatedAt,
    string? CreatedByName);

public sealed record InventoryActionResult(bool Succeeded, string Code, string Detail)
{
    public static InventoryActionResult Ok(string code) => new(true, code, string.Empty);
    public static InventoryActionResult Fail(string code, string detail) => new(false, code, detail);
}

public sealed class InventoryService
{
    private const string Received = "received";
    private const string Issued = "issued";
    private const string Adjusted = "adjusted";
    private const string Validation = "validation";
    private const string NotFound = "notfound";
    private const string Forbidden = "forbidden";
    private const string InsufficientStock = "insufficientstock";
    private const string InactiveProduct = "inactiveproduct";

    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _authorization;

    public InventoryService(ApplicationDbContext db, IAuthorizationService authorization)
    {
        _db = db;
        _authorization = authorization;
    }

    public async Task<IReadOnlyList<InventoryTransactionListItem>> ListAsync(
        ClaimsPrincipal actor,
        string? search = null,
        InventoryTransactionType? transactionType = null)
    {
        var authorized = await _authorization.AuthorizeAsync(
            actor, StockFlowAuthorization.EmployeePolicy);

        if (!authorized.Succeeded)
            return Array.Empty<InventoryTransactionListItem>();

        var query = _db.InventoryTransactions
            .AsNoTracking()
            .Include(transaction => transaction.Product)
            .Include(transaction => transaction.CreatedByUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(transaction =>
                transaction.Product.Name.Contains(term) ||
                transaction.Product.SKU.Contains(term) ||
                (transaction.Notes != null && transaction.Notes.Contains(term)));
        }

        if (transactionType.HasValue)
            query = query.Where(transaction => transaction.TransactionType == transactionType.Value);

        return await query
            .OrderByDescending(transaction => transaction.CreatedAt)
            .ThenByDescending(transaction => transaction.Id)
            .Select(transaction => new InventoryTransactionListItem(
                transaction.Id,
                transaction.ProductId,
                transaction.Product.Name,
                transaction.Product.SKU,
                transaction.TransactionType,
                transaction.Quantity,
                transaction.PreviousQuantity,
                transaction.NewQuantity,
                transaction.Notes,
                transaction.CreatedAt,
                transaction.CreatedByUser == null
                    ? null
                    : transaction.CreatedByUser.FirstName + " " + transaction.CreatedByUser.LastName))
            .ToListAsync();
    }

    public Task<InventoryActionResult> ReceiveStockAsync(
        ClaimsPrincipal actor,
        int productId,
        int quantity,
        string? notes)
    {
        return RecordMovementAsync(
            actor, productId, quantity, InventoryTransactionType.StockReceived, notes);
    }

    public Task<InventoryActionResult> IssueStockAsync(
        ClaimsPrincipal actor,
        int productId,
        int quantity,
        string? notes)
    {
        return RecordMovementAsync(
            actor, productId, quantity, InventoryTransactionType.StockIssued, notes);
    }

    public Task<InventoryActionResult> AdjustStockAsync(
        ClaimsPrincipal actor,
        int productId,
        int newQuantity,
        string? notes)
    {
        return RecordAdjustmentAsync(actor, productId, newQuantity, notes);
    }

    private async Task<InventoryActionResult> RecordMovementAsync(
        ClaimsPrincipal actor,
        int productId,
        int quantity,
        InventoryTransactionType transactionType,
        string? notes)
    {
        var authorized = await _authorization.AuthorizeAsync(
            actor, StockFlowAuthorization.EmployeePolicy);

        if (!authorized.Succeeded)
            return InventoryActionResult.Fail(Forbidden, "You are not authorized to manage inventory.");

        if (productId <= 0)
            return InventoryActionResult.Fail(Validation, "Select a valid product.");

        if (quantity <= 0)
            return InventoryActionResult.Fail(Validation, "Quantity must be greater than zero.");

        if (notes?.Length > 500)
            return InventoryActionResult.Fail(Validation, "Notes cannot exceed 500 characters.");

        await using var dbTransaction = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        try
        {
            var product = await _db.Products
                .FirstOrDefaultAsync(item => item.Id == productId);

            if (product == null)
                return InventoryActionResult.Fail(NotFound, "The selected product was not found.");

            if (!product.IsActive)
                return InventoryActionResult.Fail(InactiveProduct, "Inactive products cannot receive inventory transactions.");

            var previousQuantity = product.QuantityInStock;
            int newQuantity;

            if (transactionType == InventoryTransactionType.StockReceived)
            {
                if (quantity > int.MaxValue - previousQuantity)
                    return InventoryActionResult.Fail(Validation, "The resulting stock quantity is too large.");

                newQuantity = previousQuantity + quantity;
            }
            else
            {
                if (quantity > previousQuantity)
                    return InventoryActionResult.Fail(InsufficientStock, "There is not enough stock available for this issue.");

                newQuantity = previousQuantity - quantity;
            }

            product.QuantityInStock = newQuantity;

            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = product.Id,
                TransactionType = transactionType,
                Quantity = quantity,
                PreviousQuantity = previousQuantity,
                NewQuantity = newQuantity,
                Notes = NormalizeNotes(notes),
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = actor.FindFirstValue(ClaimTypes.NameIdentifier)
            });

            await _db.SaveChangesAsync();
            await dbTransaction.CommitAsync();

            return InventoryActionResult.Ok(
                transactionType == InventoryTransactionType.StockReceived ? Received : Issued);
        }
        catch (DbUpdateException)
        {
            await dbTransaction.RollbackAsync();
            return InventoryActionResult.Fail(
                Validation, "The inventory transaction could not be saved. Please try again.");
        }
        catch (Exception)
        {
            await dbTransaction.RollbackAsync();
            throw;
        }
    }

    private async Task<InventoryActionResult> RecordAdjustmentAsync(
        ClaimsPrincipal actor,
        int productId,
        int newQuantity,
        string? notes)
    {
        var authorized = await _authorization.AuthorizeAsync(
            actor, StockFlowAuthorization.EmployeePolicy);

        if (!authorized.Succeeded)
            return InventoryActionResult.Fail(Forbidden, "You are not authorized to manage inventory.");

        if (productId <= 0)
            return InventoryActionResult.Fail(Validation, "Select a valid product.");

        if (newQuantity < 0)
            return InventoryActionResult.Fail(Validation, "The adjusted stock quantity cannot be negative.");

        if (notes?.Length > 500)
            return InventoryActionResult.Fail(Validation, "Notes cannot exceed 500 characters.");

        await using var dbTransaction = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        try
        {
            var product = await _db.Products
                .FirstOrDefaultAsync(item => item.Id == productId);

            if (product == null)
                return InventoryActionResult.Fail(NotFound, "The selected product was not found.");

            if (!product.IsActive)
                return InventoryActionResult.Fail(InactiveProduct, "Inactive products cannot receive inventory transactions.");

            var previousQuantity = product.QuantityInStock;

            if (previousQuantity == newQuantity)
                return InventoryActionResult.Fail(Validation, "The adjusted quantity is the same as the current stock.");

            product.QuantityInStock = newQuantity;

            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = product.Id,
                TransactionType = InventoryTransactionType.Adjustment,
                Quantity = newQuantity - previousQuantity,
                PreviousQuantity = previousQuantity,
                NewQuantity = newQuantity,
                Notes = NormalizeNotes(notes),
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = actor.FindFirstValue(ClaimTypes.NameIdentifier)
            });

            await _db.SaveChangesAsync();
            await dbTransaction.CommitAsync();

            return InventoryActionResult.Ok(Adjusted);
        }
        catch (DbUpdateException)
        {
            await dbTransaction.RollbackAsync();
            return InventoryActionResult.Fail(
                Validation, "The inventory adjustment could not be saved. Please try again.");
        }
        catch (Exception)
        {
            await dbTransaction.RollbackAsync();
            throw;
        }
    }

    private static string? NormalizeNotes(string? notes)
    {
        return string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
