using Microsoft.AspNetCore.Identity;

namespace StockFlow.Data;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
