using System.Globalization;

namespace CrestApps.Core.Blazor.Web.ViewModels;

public static class UsageCostFormatter
{
    /// <summary>
    /// Formats an estimated cost, with four decimals below one so that small costs do not read as zero.
    /// </summary>
    public static string Format(string currency, decimal? cost)
    {
        if (cost is null || string.IsNullOrEmpty(currency))
        {
            return "-";
        }

        var format = cost.Value is > 0 and < 1 ? "N4" : "N2";

        return $"{currency} {cost.Value.ToString(format, CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Sums the estimated costs per currency.
    /// </summary>
    public static IReadOnlyList<string> Totals(IEnumerable<(string Currency, decimal? Cost)> costs)
        => costs
            .Where(entry => entry.Cost is not null && !string.IsNullOrEmpty(entry.Currency))
            .GroupBy(entry => entry.Currency, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => Format(group.Key, group.Sum(entry => entry.Cost.Value)))
            .ToList();
}
