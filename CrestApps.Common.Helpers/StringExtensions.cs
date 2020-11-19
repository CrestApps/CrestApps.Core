using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CrestApps.Common.Helpers
{
    public static class StringExtensions
    {
        public static string RemoveAccent(this string txt)
        {
            byte[] bytes = System.Text.Encoding.GetEncoding("Cyrillic").GetBytes(txt);

            return System.Text.Encoding.ASCII.GetString(bytes);
        }

        public static bool Like(this string toSearch, string toFind)
        {
            var match = new Regex(@"\.|\$|\^|\{|\[|\(|\||\)|\*|\+|\?|\\").Replace(toFind, ch => @"\" + ch)
                                                                         .Replace('_', '.')
                                                                         .Replace("%", ".*");

            return new Regex(@"\A" + match + @"\z", RegexOptions.Singleline).IsMatch(toSearch);
        }

        public static bool Like(this string toSearch, string toFind, StringComparison comparison)
        {
            if (comparison == StringComparison.CurrentCultureIgnoreCase || comparison == StringComparison.OrdinalIgnoreCase || comparison == StringComparison.InvariantCultureIgnoreCase)
            {
                return Like(toSearch.ToLower(), toFind.ToLower());
            }

            return Like(toSearch, toFind);
        }


        public static string ToCamelCase(this string str)
        {
            Regex pattern = new Regex(@"[A-Z]{2,}(?=[A-Z][a-z]+[0-9]*|\b)|[A-Z]?[a-z]+[0-9]*|[A-Z]|[0-9]+");
            return new string(
              new CultureInfo("en-US", false)
                .TextInfo
                .ToTitleCase(
                  string.Join(" ", pattern.Matches(str)).ToLower()
                )
                .Replace(@" ", "")
                .Select((x, i) => i == 0 ? char.ToLower(x) : x)
                .ToArray()
            );
        }


        public static string ToSnakeCase(this string str)
        {
            Regex pattern = new Regex(@"[A-Z]{2,}(?=[A-Z][a-z]+[0-9]*|\b)|[A-Z]?[a-z]+[0-9]*|[A-Z]|[0-9]+");

            return string.Join("_", pattern.Matches(str)).ToLower();
        }
    }
}
