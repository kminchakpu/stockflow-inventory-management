
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StockFlow.Models;

namespace StockFlow.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Category>(entity =>
        {
            entity.Property(category => category.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(category => category.Description)
                .HasMaxLength(500);

            entity.HasIndex(category => category.Name)
                .IsUnique();
        });

        builder.Entity<Product>(entity =>
        {
            entity.Property(product => product.SKU)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(product => product.Name)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(product => product.Description)
                .HasMaxLength(1000);

            entity.Property(product => product.CostPrice)
                .HasPrecision(18, 2);

            entity.Property(product => product.SellingPrice)
                .HasPrecision(18, 2);

            entity.HasIndex(product => product.SKU)
                .IsUnique();

            entity.HasOne(product => product.Category)
                .WithMany()
                .HasForeignKey(product => product.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<InventoryTransaction>(entity =>
        {
            entity.Property(transaction => transaction.Notes)
                .HasMaxLength(500);

            entity.HasOne(transaction => transaction.Product)
                .WithMany()
                .HasForeignKey(transaction => transaction.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(transaction => transaction.CreatedByUser)
                .WithMany()
                .HasForeignKey(transaction => transaction.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(transaction => transaction.Sale)
                .WithMany()
                .HasForeignKey(transaction => transaction.SaleId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Customer>(entity =>
        {
            entity.Property(customer => customer.FirstName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(customer => customer.LastName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(customer => customer.Email)
                .HasMaxLength(254);

            entity.Property(customer => customer.PhoneNumber)
                .IsRequired()
                .HasMaxLength(30);

            entity.Property(customer => customer.CompanyName)
                .HasMaxLength(150);

            entity.Property(customer => customer.Address)
                .HasMaxLength(500);

            entity.Property(customer => customer.Notes)
                .HasMaxLength(1000);

            entity.HasIndex(customer => customer.PhoneNumber)
                .IsUnique();

            entity.HasIndex(customer => customer.Email)
                .IsUnique()
                .HasFilter("\"Email\" IS NOT NULL");
        });

        builder.Entity<Sale>(entity =>
        {
            entity.Property(sale => sale.SaleNumber)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(sale => sale.CustomerName)
                .HasMaxLength(200);

            entity.Property(sale => sale.CustomerPhone)
                .HasMaxLength(30);

            entity.Property(sale => sale.ProcessedByName)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(sale => sale.Subtotal)
                .HasPrecision(18, 2);

            entity.Property(sale => sale.DiscountAmount)
                .HasPrecision(18, 2);

            entity.Property(sale => sale.TotalAmount)
                .HasPrecision(18, 2);

            entity.HasIndex(sale => sale.SaleNumber)
                .IsUnique();

            entity.HasOne(sale => sale.Customer)
                .WithMany()
                .HasForeignKey(sale => sale.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(sale => sale.ProcessedByUser)
                .WithMany()
                .HasForeignKey(sale => sale.ProcessedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SaleItem>(entity =>
        {
            entity.Property(item => item.ProductName)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(item => item.SKU)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(item => item.UnitPrice)
                .HasPrecision(18, 2);

            entity.Property(item => item.UnitCostPrice)
                .HasPrecision(18, 2);

            entity.Property(item => item.LineTotal)
                .HasPrecision(18, 2);

            entity.HasOne(item => item.Sale)
                .WithMany(sale => sale.Items)
                .HasForeignKey(item => item.SaleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(item => item.Product)
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SalePayment>(entity =>
        {
            entity.Property(payment => payment.Amount)
                .HasPrecision(18, 2);

            entity.Property(payment => payment.Reference)
                .HasMaxLength(150);

            entity.Property(payment => payment.ReceivedByName)
                .IsRequired()
                .HasMaxLength(200);

            entity.HasOne(payment => payment.Sale)
                .WithMany(sale => sale.Payments)
                .HasForeignKey(payment => payment.SaleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(payment => payment.ReceivedByUser)
                .WithMany()
                .HasForeignKey(payment => payment.ReceivedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
