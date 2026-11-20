
namespace StockFlow.Models;

public enum InventoryTransactionType
{
    StockReceived = 1,
    StockIssued = 2,
    Adjustment = 3
}

public class InventoryTransaction
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int? SaleId { get; set; }
    public Sale? Sale { get; set; }
    public InventoryTransactionType TransactionType { get; set; }
    public int Quantity { get; set; }
    public int PreviousQuantity { get; set; }
    public int NewQuantity { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }
}
