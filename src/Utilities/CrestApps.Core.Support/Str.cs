using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CrestApps.Core.Support;

public static partial class Str
{
    public static bool IsNumeric(string phrase)
    {
        if (string.IsNullOrEmpty(phrase))
        {
            return false;
        }

        return IsNumeric().IsMatch(phrase);
    }

    public static string Slug(string phrase, int maxLength = 100)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return string.Empty;
        }
        // invalid chars
        var str = InvalidSlugChars().Replace(phrase.ToLower(), string.Empty);

        // convert multiple spaces into one space
        str = MultipleSpaces().Replace(str, " ").Trim();

        // cut and trim
        str = str.Substring(0, str.Length <= maxLength ? str.Length : maxLength).Trim();

        // replace spaces with hyphens
        str = ReplaceSpaceWithHyphens().Replace(str, "-");

        return str;
    }

    /// <summary>
    /// Adds a space after each Capital Letter.
    /// "HelloWorldThisIsATest" would then be "Hello World This Is A Test".
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    public static string AddSpacesToWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return SpaceBeforeWords().Replace(text, " $1$2").Trim();
    }

    public static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    public static string TrimEnd(string subject, string pattern)
    {
        return TrimEnd(subject, pattern, StringComparison.Ordinal);
    }

    public static string Merge(params string[] words)
    {
        return Merge([' '], words);
    }

    public static string Merge(char[] glue, params string[] words)
    {
        var valuable = new List<string>();

        foreach (var word in words ?? [])
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }

            valuable.Add(word.Trim());
        }

        var sentence = string.Join(new string(glue), valuable);

        return sentence;
    }

    public static string UniformNewLines(string text, string newline = "\n")
    {
        var template = "[%%%%% SINGLE_NEW_LINE %%%%%]";

        var body = NewLineCRLF().Replace(text, template);
        body = NewLineEL().Replace(body, template);
        body = NewLineLF().Replace(body, template);

        body = body.Replace(template, newline);

        return body;
    }

    public static string Repeat(string value, int count)
    {
        if (count < 1 || string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return new StringBuilder(value.Length * count).Insert(0, value, count).ToString();
    }

    public static string Random(int length = 40)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuvwzyz";
        return new string(RandomNumberGenerator.GetItems<char>(chars.AsSpan(), length));
    }

    public static string ToLower(string str, string defaultValue = "")
    {
        if (str != null)
        {
            return str.ToLower();
        }

        return defaultValue;
    }

    public static string TrimEnd(string subject, string pattern, StringComparison type)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject == pattern)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(pattern) && subject.EndsWith(pattern, type))
        {
            var index = subject.Length - pattern.Length;

            return subject.Substring(0, index);
        }

        return subject;
    }

    public static int CountOccurrences(string text, string pattern)
    {
        var count = 0;

        var i = 0;

        while ((i = text.IndexOf(pattern, i)) != -1)
        {
            i += pattern.Length;
            count++;
        }

        return count;
    }

    public static string UppercaseFirst(string word, bool lowercaseTheRest = true)
    {
        if (string.IsNullOrEmpty(word))
        {
            return word;
        }

        var final = char.ToUpper(word[0]).ToString();

        if (lowercaseTheRest)
        {
            return final + word.Substring(1).ToLower();
        }

        return string.Concat(final, word.AsSpan(1));
    }

    public static string AppendOnce(string original, string toAppend = "/")
    {
        if (original == null || original.EndsWith(toAppend))
        {
            return original;
        }

        return original + toAppend;
    }

    public static string PrependOnce(string original, string toPrefix = "/")
    {
        if (original == null || original.StartsWith(toPrefix))
        {
            return original;
        }

        return toPrefix + original;
    }

    public static string TrimStart(string subject, string pattern)
    {
        return TrimStart(subject, pattern, StringComparison.CurrentCulture);
    }

    public static string TrimStart(string subject, string pattern, StringComparison type)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject == pattern)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(pattern) && subject.StartsWith(pattern, type))
        {
            return subject.Substring(pattern.Length);
        }

        return subject;
    }

    public static string SubstringUntil(string str, string until, bool trim = true, bool untilFirstOccurrence = true)
    {
        var substring = str;

        if (str != null && !string.IsNullOrEmpty(until))
        {
            var index = untilFirstOccurrence ? str.IndexOf(until) : str.LastIndexOf(until);

            if (index >= 0)
            {
                substring = str.Substring(0, index);
            }
        }

        if (trim && substring != null)
        {
            substring = substring.Trim();
        }

        return substring;
    }

    public static string AfterFirstInstance(string str, string lastString)
    {
        if (string.IsNullOrWhiteSpace(str) || string.IsNullOrEmpty(lastString))
        {
            return str;
        }

        var index = str.IndexOf(lastString);

        var substring = str.Substring(index + lastString.Length, str.Length - (index + lastString.Length));

        return substring;
    }

    public static string AfterLastInstance(string str, string lastString)
    {
        if (string.IsNullOrWhiteSpace(str) || string.IsNullOrEmpty(lastString))
        {
            return str;
        }

        var index = str.LastIndexOf(lastString);

        var substring = str.Substring(index + lastString.Length, str.Length - (index + lastString.Length));

        return substring;
    }

    public static string ReplaceFirst(string text, string search, string replace)
    {
        var pos = text.IndexOf(search);
        if (pos < 0)
        {
            return text;
        }
        return string.Concat(text.AsSpan(0, pos), replace, text.AsSpan(pos + search.Length));
    }

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex IsNumeric();

    [GeneratedRegex("([A-Z])([a-z]*)")]
    private static partial Regex SpaceBeforeWords();
    [GeneratedRegex(@"[^a-z0-9\s-]")]
    private static partial Regex InvalidSlugChars();
    [GeneratedRegex(@"\s")]
    private static partial Regex ReplaceSpaceWithHyphens();
    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpaces();
    [GeneratedRegex("\r\n")]
    private static partial Regex NewLineCRLF();
    [GeneratedRegex("\r")]
    private static partial Regex NewLineEL();
    [GeneratedRegex("\n")]
    private static partial Regex NewLineLF();
}
