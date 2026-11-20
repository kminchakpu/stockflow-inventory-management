
namespace StockFlow.Models;

public class SaleItem
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal UnitCostPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}
