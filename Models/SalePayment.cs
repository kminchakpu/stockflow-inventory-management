
namespace StockFlow.Models;

public enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2,
    Card = 3,
    Other = 4
}

public class SalePayment
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public string? ReceivedByUserId { get; set; }
    public ApplicationUser? ReceivedByUser { get; set; }
    public string ReceivedByName { get; set; } = string.Empty;
}
