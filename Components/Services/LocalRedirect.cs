namespace StockFlow.Components.Services;

public static class LocalRedirect
{
    public static string Normalize(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/dashboard";
        }

        var candidate = returnUrl.Trim();
        if (!candidate.StartsWith('/')
            || candidate.StartsWith("//", StringComparison.Ordinal)
            || candidate.StartsWith("/\\", StringComparison.Ordinal)
            || candidate.Contains('\\', StringComparison.Ordinal)
            || candidate.Contains("://", StringComparison.Ordinal)
            || candidate.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
        {
            return "/dashboard";
        }

        return candidate;
    }
}
