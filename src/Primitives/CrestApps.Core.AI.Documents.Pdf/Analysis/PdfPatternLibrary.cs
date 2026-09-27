using System.Numerics;
using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Recognises the kinds of value a document states in a fixed shape — email addresses, phone numbers,
/// links, card and account numbers, identity numbers, dates, amounts — and checks the ones that carry a
/// check digit, so a run of sixteen digits is only called a card number when it is one.
/// </summary>
internal static partial class PdfPatternLibrary
{
    /// <summary>
    /// An email address.
    /// </summary>
    public const string Email = "email";

    /// <summary>
    /// A telephone number.
    /// </summary>
    public const string Phone = "phone";

    /// <summary>
    /// A web address.
    /// </summary>
    public const string Url = "url";

    /// <summary>
    /// An IPv4 or IPv6 address.
    /// </summary>
    public const string IpAddress = "ip_address";

    /// <summary>
    /// A payment card number that passes the Luhn check.
    /// </summary>
    public const string CreditCard = "credit_card";

    /// <summary>
    /// An international bank account number that passes the mod-97 check.
    /// </summary>
    public const string Iban = "iban";

    /// <summary>
    /// A United States social security number.
    /// </summary>
    public const string UsSocialSecurityNumber = "us_ssn";

    /// <summary>
    /// A calendar date.
    /// </summary>
    public const string Date = "date";

    /// <summary>
    /// An amount of money.
    /// </summary>
    public const string Money = "money";

    /// <summary>
    /// A percentage.
    /// </summary>
    public const string Percentage = "percentage";

    /// <summary>
    /// A United States ZIP code in an address.
    /// </summary>
    public const string UsZipCode = "us_zip_code";

    /// <summary>
    /// Every kind this library recognises.
    /// </summary>
    public static readonly string[] AllKinds =
    [
        Email,
        Phone,
        Url,
        IpAddress,
        CreditCard,
        Iban,
        UsSocialSecurityNumber,
        Date,
        Money,
        Percentage,
        UsZipCode,
    ];

    /// <summary>
    /// The kinds that identify a person or their accounts, which is what a redaction usually removes.
    /// </summary>
    public static readonly string[] SensitiveKinds =
    [
        Email,
        Phone,
        CreditCard,
        Iban,
        UsSocialSecurityNumber,
        IpAddress,
    ];

    /// <summary>
    /// Finds every value of the given kinds in a text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="kinds">The kinds to look for, or <see langword="null"/> for all of them.</param>
    /// <returns>The values found, in the order they appear.</returns>
    public static List<PdfPatternMatch> Find(string text, IEnumerable<string> kinds = null)
    {
        var matches = new List<PdfPatternMatch>();

        if (string.IsNullOrEmpty(text))
        {
            return matches;
        }

        var wanted = new HashSet<string>(kinds ?? AllKinds, StringComparer.OrdinalIgnoreCase);

        void Collect(string kind, Regex regex, Func<string, bool> validate = null)
        {
            if (!wanted.Contains(kind))
            {
                return;
            }

            foreach (Match match in regex.Matches(text))
            {
                var value = match.Value.Trim().TrimEnd('.', ',', ';', ':', ')');

                if (value.Length > 0 && (validate is null || validate(value)))
                {
                    matches.Add(new PdfPatternMatch(kind, value, match.Index, value.Length));
                }
            }
        }

        Collect(Email, EmailPattern());
        Collect(Url, UrlPattern());
        Collect(IpAddress, IpPattern(), IsIpAddress);
        Collect(CreditCard, CardPattern(), IsLuhnValid);
        Collect(Iban, IbanPattern(), IsIbanValid);
        Collect(UsSocialSecurityNumber, SsnPattern(), IsPlausibleSsn);
        Collect(Phone, PhonePattern(), IsPlausiblePhone);
        Collect(Date, DatePattern());
        Collect(Money, MoneyPattern());
        Collect(Percentage, PercentPattern());
        Collect(UsZipCode, ZipPattern());

        // A card number also reads as a phone number and a date part can read as an amount; the more
        // specific kind wins, and the looser match inside it is dropped.
        var ordered = matches
            .OrderBy(match => match.Index)
            .ThenByDescending(match => match.Length)
            .ToList();

        var result = new List<PdfPatternMatch>(ordered.Count);
        var coveredUntil = -1;

        foreach (var match in ordered)
        {
            if (match.Index < coveredUntil)
            {
                continue;
            }

            result.Add(match);
            coveredUntil = match.Index + match.Length;
        }

        return result;
    }

    /// <summary>
    /// Masks a sensitive value for display, keeping only enough to recognise it.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="value">The value.</param>
    /// <returns>The masked value, for example <c>•••• •••• •••• 4242</c>.</returns>
    public static string Mask(string kind, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var digits = new string([.. value.Where(char.IsAsciiDigit)]);

        return kind switch
        {
            CreditCard when digits.Length >= 4 => "•••• " + digits[^4..],
            UsSocialSecurityNumber when digits.Length >= 4 => "•••-••-" + digits[^4..],
            Iban when value.Length > 6 => value[..4] + " •••• " + value[^4..],
            Email when value.Contains('@', StringComparison.Ordinal) => value[0] + "•••" + value[value.IndexOf('@', StringComparison.Ordinal)..],
            Phone when digits.Length >= 4 => "•••-" + digits[^4..],
            _ => value,
        };
    }

    /// <summary>
    /// Checks a card number with the Luhn algorithm.
    /// </summary>
    /// <param name="value">The number, with or without separators.</param>
    /// <returns><see langword="true"/> when the number is 13 to 19 digits and passes the check.</returns>
    public static bool IsLuhnValid(string value)
    {
        var digits = value.Where(char.IsAsciiDigit).Select(character => character - '0').ToArray();

        if (digits.Length is < 13 or > 19 || digits.All(digit => digit == digits[0]))
        {
            return false;
        }

        var sum = 0;

        for (var index = 0; index < digits.Length; index++)
        {
            var digit = digits[digits.Length - 1 - index];

            if (index % 2 == 1)
            {
                digit *= 2;

                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return sum % 10 == 0;
    }

    /// <summary>
    /// Checks an IBAN with the ISO 7064 mod-97 check.
    /// </summary>
    /// <param name="value">The IBAN, with or without spaces.</param>
    /// <returns><see langword="true"/> when the IBAN is well formed and passes the check.</returns>
    public static bool IsIbanValid(string value)
    {
        var compact = new string([.. value.Where(character => !char.IsWhiteSpace(character))]).ToUpperInvariant();

        if (compact.Length is < 15 or > 34 || !char.IsAsciiLetter(compact[0]) || !char.IsAsciiLetter(compact[1]))
        {
            return false;
        }

        var rearranged = compact[4..] + compact[..4];
        var numeric = new System.Text.StringBuilder(rearranged.Length * 2);

        foreach (var character in rearranged)
        {
            if (char.IsAsciiDigit(character))
            {
                numeric.Append(character);
            }
            else if (char.IsAsciiLetterUpper(character))
            {
                numeric.Append((character - 'A' + 10).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                return false;
            }
        }

        return BigInteger.Parse(numeric.ToString(), System.Globalization.CultureInfo.InvariantCulture) % 97 == 1;
    }

    private static bool IsPlausibleSsn(string value)
    {
        var digits = new string([.. value.Where(char.IsAsciiDigit)]);

        // Area 000, 666 and 900-999, group 00 and serial 0000 are never issued.
        return digits.Length == 9 &&
            digits[..3] is not ("000" or "666") &&
            digits[0] != '9' &&
            digits[3..5] != "00" &&
            digits[5..] != "0000";
    }

    private static bool IsPlausiblePhone(string value)
    {
        var digits = value.Count(char.IsAsciiDigit);

        return digits is >= 7 and <= 15;
    }

    private static bool IsIpAddress(string value)
    {
        return System.Net.IPAddress.TryParse(value, out var address) &&
            (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 || value.Count(character => character == '.') == 3);
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\b(?:https?://|www\.)[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b|\b(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}\b", RegexOptions.CultureInvariant)]
    private static partial Regex IpPattern();

    [GeneratedRegex(@"\b(?:\d[ \-]?){12,18}\d\b", RegexOptions.CultureInvariant)]
    private static partial Regex CardPattern();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}(?:[ ]?[A-Z0-9]{4}){2,7}(?:[ ]?[A-Z0-9]{1,4})?\b", RegexOptions.CultureInvariant)]
    private static partial Regex IbanPattern();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.CultureInvariant)]
    private static partial Regex SsnPattern();

    [GeneratedRegex(@"(?<![\w@])(?:\+\d{1,3}[\s.\-]?)?(?:\(\d{1,4}\)[\s.\-]?)?\d{2,4}[\s.\-]\d{2,4}(?:[\s.\-]\d{2,5}){0,2}(?![\w])", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\b(?:\d{4}-\d{2}-\d{2}|\d{1,2}[/.\-]\d{1,2}[/.\-]\d{2,4}|(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+\d{1,2}(?:st|nd|rd|th)?,?\s+\d{4}|\d{1,2}\s+(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"(?:[$€£¥₹]\s?\d[\d,]*(?:\.\d+)?(?:\s?(?:million|billion|thousand|[mMbBkK])\b)?|\b\d[\d,]*(?:\.\d+)?\s?(?:USD|EUR|GBP|JPY|CAD|AUD|CHF|INR|dollars|euros|pounds)\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MoneyPattern();

    [GeneratedRegex(@"[-+]?\b\d+(?:\.\d+)?\s?%", RegexOptions.CultureInvariant)]
    private static partial Regex PercentPattern();

    [GeneratedRegex(@"(?<=\b[A-Z]{2}\s)\d{5}(?:-\d{4})?\b", RegexOptions.CultureInvariant)]
    private static partial Regex ZipPattern();
}
