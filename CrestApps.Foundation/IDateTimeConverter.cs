using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrestApps.Foundation
{
    public interface IDateTimeConverter
    {
        DateTime LocalToUtc(DateTime value);
        DateTime? LocalToUtc(DateTime? value);
        DateTime LocalToUtc(DateTime value, string timeZoneName);
        DateTime? LocalToUtc(DateTime? value, string timeZoneName);
        DateTime LocalToUtc(DateTime dateTimeValueToConvert, TimeZoneInfo localTimeZoneInfo);


        DateTime UtcToLocal(DateTime value);
        DateTime? UtcToLocal(DateTime? value);
        DateTime UtcToLocal(DateTime value, string timeZoneName);
        DateTime? UtcToLocal(DateTime? value, string timeZoneName);
        DateTime UtcToLocal(DateTime value, TimeZoneInfo destinationTimeZoneInfo);

        TimeZoneInfo GetDefaultTimeZone();
        string GetLocalTimeZoneName();
        int GetUtcOffset(string timeZoneName);
        int GetUtcOffset();
        TimeZoneInfo GetTimeZoneInfo(string timeZoneName);

        DateTime GetStartOfToday();
        DateTime GetEndOfToday();
        DateTime GetStartOfMonth();
        DateTime GetEndOfMonth();
        DateTime GetStartOfYear();
        DateTime GetEndOfYear();
        DateTime GetStartOfToday(string timeZoneName);
        DateTime GetEndOfToday(string timeZoneName);
        DateTime GetStartOfMonth(string timeZoneName);
        DateTime GetEndOfMonth(string timeZoneName);
        DateTime GetStartOfYear(string timeZoneName);
        DateTime GetEndOfYear(string timeZoneName);
    }
}
