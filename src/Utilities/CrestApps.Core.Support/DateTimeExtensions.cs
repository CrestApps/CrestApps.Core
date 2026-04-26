namespace CrestApps.Core.Support;

public static class DateTimeExtensions
{
    /// <summary>
    /// Start of years of year.
    /// </summary>
    /// <param name="dateTime">The date time.</param>
    public static DateTime StartOfYear(this DateTime dateTime)
    {
        return new DateTime(dateTime.Year, 1, 1, 0, 0, 0, dateTime.Kind);
    }

    /// <summary>
    /// Ends of year.
    /// </summary>
    /// <param name="dateTime">The date time.</param>
    public static DateTime EndOfYear(this DateTime dateTime)
    {
        return new DateTime(dateTime.Year, 12, 31, 23, 59, 59, 999, dateTime.Kind);
    }

    /// <summary>
    /// Starts of day.
    /// </summary>
    /// <param name="dateTime">The date time.</param>
    public static DateTime StartOfDay(this DateTime dateTime)
    {
        return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, 0, 0, 0, dateTime.Kind);
    }

    /// <summary>
    /// Ends of day.
    /// </summary>
    /// <param name="dateTime">The date time.</param>
    public static DateTime EndOfDay(this DateTime dateTime)
    {
        return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, 23, 59, 59, 999, dateTime.Kind);
    }

    /// <summary>
    /// Starts of month.
    /// </summary>
    /// <param name="dateTime">The date time.</param>
    public static DateTime StartOfMonth(this DateTime dateTime)
    {
        return new DateTime(dateTime.Year, dateTime.Month, 1, 0, 0, 0, dateTime.Kind);
    }

    /// <summary>
    /// Ends of month.
    /// </summary>
    /// <param name="dateTime">The date time.</param>
    public static DateTime EndOfMonth(this DateTime dateTime)
    {
        var start = dateTime.StartOfMonth();

        return start.AddMonths(1).AddSeconds(-1);
    }

    /// <summary>
    /// Starts of week.
    /// </summary>
    /// <param name="dateTime">The date time.</param>
    /// <param name="startOfWeek">The start of week.</param>
    public static DateTime StartOfWeek(this DateTime dateTime, DayOfWeek startOfWeek)
    {
        var diff = (7 + (dateTime.DayOfWeek - startOfWeek)) % 7;

        return dateTime.AddDays(-1 * diff).Date;
    }

    /// <summary>
    /// Ends of week.
    /// </summary>
    /// <param name="dateTime">The date time.</param>
    /// <param name="startOfWeek">The start of week.</param>
    public static DateTime EndOfWeek(this DateTime dateTime, DayOfWeek startOfWeek)
    {
        var diff = (7 - (dateTime.DayOfWeek - startOfWeek)) % 7;

        return dateTime.AddDays(1 * diff).Date;
    }
}
