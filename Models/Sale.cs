
namespace StockFlow.Models;

public enum SaleStatus
{
    Completed = 1,
    Voided = 2
}

public enum PaymentStatus
{
    Unpaid = 1,
    PartiallyPaid = 2,
    Paid = 3
}

public class Sale
{
    public int Id { get; set; }
    public string SaleNumber { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? ProcessedByUserId { get; set; }
    public ApplicationUser? ProcessedByUser { get; set; }
    public string ProcessedByName { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Completed;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
    public ICollection<SalePayment> Payments { get; set; } = new List<SalePayment>();
}
