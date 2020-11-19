using System;

namespace CrestApps.Common.Helpers
{
    public class EnumHelper
    {
        public static T ValueOrFirst<T>(string value) where T : struct
        {
            Enum.TryParse(value, out T status);

            return status;
        }

        public static bool IsEqual<T>(T enumValue, string value) where T : struct
        {
            T t = ValueOrFirst<T>(value);

            return t.Equals(enumValue);
        }

        public static T? ValueOrNull<T>(string value) where T : struct
        {
            if (Enum.TryParse(value, out T status))
            {
                return status;
            }

            return null;
        }

    }
}
