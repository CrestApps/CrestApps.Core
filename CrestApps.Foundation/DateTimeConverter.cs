using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrestApps.Foundation
{
    public class DateTimeConverter : IDateTimeConverter
    {
        private IUserPassport Passport;

        public DateTimeConverter(IUserPassport passport)
        {
            Passport = passport;
        }

        public DateTime LocalToUtc(DateTime value)
        {
            return LocalToUtc(value, GetLocalTimeZoneName());
        }

        public DateTime? LocalToUtc(DateTime? value)
        {
            return LocalToUtc(value, GetLocalTimeZoneName());
        }

        public DateTime? LocalToUtc(DateTime? value, string timeZoneName)
        {
            if (value.HasValue)
            {
                return LocalToUtc(value.Value, timeZoneName);
            }

            return value;
        }

        public DateTime LocalToUtc(DateTime value, string timeZoneName)
        {
            TimeZoneInfo info = GetTimeZoneInfo(timeZoneName);

            return LocalToUtc(value, info);
        }



        public DateTime LocalToUtc(DateTime dateTimeValueToConvert, TimeZoneInfo localTimeZoneInfo)
        {
            var convertedValue = DateTime.SpecifyKind(dateTimeValueToConvert, DateTimeKind.Unspecified);

            if (localTimeZoneInfo != null)
            {
                return TimeZoneInfo.ConvertTimeToUtc(convertedValue, localTimeZoneInfo);
            }

            return TimeZoneInfo.ConvertTimeToUtc(convertedValue, GetLocalTimeZone());
        }



        public DateTime UtcToLocal(DateTime value)
        {
            return UtcToLocal(value, GetLocalTimeZoneName());
        }

        public DateTime? UtcToLocal(DateTime? value)
        {
            return UtcToLocal(value, GetLocalTimeZoneName());
        }


        public DateTime? UtcToLocal(DateTime? value, string timeZoneName)
        {
            if (value.HasValue)
            {
                return UtcToLocal(value.Value, timeZoneName);
            }

            return value;
        }


        public DateTime UtcToLocal(DateTime value, string timeZoneName)
        {
            TimeZoneInfo info = GetTimeZoneInfo(timeZoneName);

            return UtcToLocal(value, info);
        }



        public DateTime UtcToLocal(DateTime value, TimeZoneInfo destinationTimeZoneInfo)
        {
            var convertedValue = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

            if (destinationTimeZoneInfo != null)
            {
                return TimeZoneInfo.ConvertTimeFromUtc(value, destinationTimeZoneInfo);
            }

            return TimeZoneInfo.ConvertTimeFromUtc(value, GetDefaultTimeZone());
        }


        public TimeZoneInfo GetDefaultTimeZone()
        {
            var info = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneInfo.Local.StandardName);

            return info;
        }

        public string GetLocalTimeZoneName()
        {
            return Passport.TimeZoneName;
        }

        public TimeZoneInfo GetLocalTimeZone()
        {
            var info = TimeZoneInfo.FindSystemTimeZoneById(GetLocalTimeZoneName());

            return info ?? GetDefaultTimeZone();
        }

        public int GetUtcOffset()
        {
            return GetUtcOffset(GetLocalTimeZoneName());
        }

        public int GetUtcOffset(string timeZoneName)
        {
            DateTime current = DateTime.UtcNow;
            TimeZoneInfo info = GetTimeZoneInfo(timeZoneName);

            DateTime currentInZone = TimeZoneInfo.ConvertTimeFromUtc(current, info);

            int diff = (int)currentInZone.Subtract(current).TotalSeconds;

            return diff;
        }

        public TimeZoneInfo GetTimeZoneInfo(string timeZoneName)
        {
            if (string.IsNullOrWhiteSpace(timeZoneName))
            {
                timeZoneName = Passport.TimeZoneName ?? TimeZoneInfo.Local.StandardName;
            }

            TimeZoneInfo timeZone = null;

            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneName);
            }
            catch
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneInfo.Local.StandardName);
            }

            return timeZone;
        }

        public DateTime GetStartOfToday()
        {
            return GetStartOfToday(Passport.TimeZoneName);
        }

        public DateTime GetEndOfToday()
        {
            return GetEndOfToday(Passport.TimeZoneName);
        }

        public DateTime GetStartOfMonth()
        {
            return GetStartOfMonth(Passport.TimeZoneName);
        }

        public DateTime GetEndOfMonth()
        {
            return GetEndOfMonth(Passport.TimeZoneName);
        }

        public DateTime GetStartOfYear()
        {
            return GetStartOfYear(Passport.TimeZoneName);
        }

        public DateTime GetEndOfYear()
        {
            return GetEndOfYear(Passport.TimeZoneName);
        }

        public DateTime GetStartOfToday(string timeZoneName)
        {
            return UtcToLocal(DateTime.UtcNow, timeZoneName).Date;
        }

        public DateTime GetEndOfToday(string timeZoneName)
        {
            var today = GetStartOfToday();

            return new DateTime(today.Year, today.Month, today.Day, 23, 59, 59);
        }

        public DateTime GetStartOfMonth(string timeZoneName)
        {
            var today = GetStartOfToday(timeZoneName);

            DateTime firstDay = new DateTime(today.Year, today.Month, 1, 0, 0, 0);

            return firstDay;
        }

        public DateTime GetEndOfMonth(string timeZoneName)
        {
            var today = GetStartOfToday(timeZoneName);

            DateTime lastDay = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month), 23, 59, 59);

            return lastDay;
        }


        public DateTime GetStartOfYear(string timeZoneName)
        {
            var today = GetStartOfToday(timeZoneName);

            DateTime firstDay = new DateTime(today.Year, 1, 1, 0, 0, 0);

            return firstDay;
        }

        public DateTime GetEndOfYear(string timeZoneName)
        {
            var today = GetStartOfToday(timeZoneName);

            DateTime lastDay = new DateTime(today.Year, 12, DateTime.DaysInMonth(today.Year, today.Month), 23, 59, 59);

            return lastDay;
        }
    }
}
