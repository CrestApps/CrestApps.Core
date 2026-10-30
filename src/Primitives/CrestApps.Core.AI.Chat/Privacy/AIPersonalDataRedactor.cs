using System.Text;
using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Chat.Privacy;

/// <summary>
/// Replaces personal details in text with placeholders: email addresses, phone numbers, payment card numbers and
/// social security numbers by pattern, and names, addresses and other details the utility model found by the exact
/// text it found.
/// </summary>
public static partial class AIPersonalDataRedactor
{
    /// <summary>
    /// Replaces an email address.
    /// </summary>
    public const string EmailPlaceholder = "[EMAIL]";

    /// <summary>
    /// Replaces a phone number.
    /// </summary>
    public const string PhonePlaceholder = "[PHONE]";

    /// <summary>
    /// Replaces a payment card number.
    /// </summary>
    public const string CardPlaceholder = "[CARD]";

    /// <summary>
    /// Replaces an identity, account or member number.
    /// </summary>
    public const string IdPlaceholder = "[ID NUMBER]";

    /// <summary>
    /// Replaces a person's name.
    /// </summary>
    public const string NamePlaceholder = "[NAME]";

    /// <summary>
    /// Replaces a street address.
    /// </summary>
    public const string AddressPlaceholder = "[ADDRESS]";

    /// <summary>
    /// Replaces a date of birth.
    /// </summary>
    public const string BirthDatePlaceholder = "[DATE OF BIRTH]";

    /// <summary>
    /// Replaces the personal details in a text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="terms">The details the utility model found, if any.</param>
    /// <returns>The redacted text.</returns>
    public static string Redact(string text, IEnumerable<AIPersonalDataTerm> terms = null)
    {
        return RedactTerms(RedactPatterns(text), terms);
    }

    /// <summary>
    /// Replaces the details recognized by pattern: email addresses, payment card numbers (checked with the Luhn
    /// algorithm), US social security numbers, and phone numbers of ten to fifteen digits, or eight or more after
    /// a <c>+</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The redacted text.</returns>
    public static string RedactPatterns(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return RedactPatternsInTurns([text])[0];
    }

    /// <summary>
    /// Replaces the personal details in consecutive turns of one speaker, finding them in the turns joined together so
    /// a detail spoken over two turns ("mike at" and then "example.com") is found whole, and replaces every part of it
    /// in each turn it spans. Besides the patterns of <see cref="RedactPatterns(string)"/>, an email address spoken
    /// aloud ("mike at example dot com") is recognized.
    /// </summary>
    /// <param name="turns">The turns of one speaker, in order. A <see langword="null"/> or empty turn is kept as it is.</param>
    /// <param name="terms">The details the utility model found, if any.</param>
    /// <returns>The redacted turns, one for each turn.</returns>
    public static IReadOnlyList<string> RedactTurns(IReadOnlyList<string> turns, IEnumerable<AIPersonalDataTerm> terms = null)
    {
        ArgumentNullException.ThrowIfNull(turns);

        return RedactPatternsInTurns(turns).Select(turn => RedactTerms(turn, terms)).ToList();
    }

    /// <summary>
    /// The longest turn of the other speaker, in characters, that a speaker's detail may be dictated across: the
    /// caller says "mike at", the assistant asks for the rest in a sentence, the caller says "example.com".
    /// </summary>
    internal const int MaxInterleavedTurnLength = 400;

    /// <summary>
    /// Replaces the personal details in a whole conversation. Each speaker's turns are read as one stream, short turns
    /// of the other speakers between them left out, so a detail dictated over several turns ("mike at", an
    /// acknowledgement, "example.com") is found whole and replaced in each turn it spans. An email address found
    /// anywhere, such as the assistant reading it back, is then taken out of every other turn where a part of it is
    /// said next to "at", "dot" or on its own as the whole turn.
    /// </summary>
    /// <param name="speakers">Who said each turn, such as the role. Turns with the same value are the same speaker.</param>
    /// <param name="turns">The turns, in order. A <see langword="null"/> or empty turn is kept as it is.</param>
    /// <param name="terms">The details the utility model found, if any.</param>
    /// <returns>The redacted turns, one for each turn.</returns>
    public static IReadOnlyList<string> RedactConversation(IReadOnlyList<string> speakers, IReadOnlyList<string> turns, IEnumerable<AIPersonalDataTerm> terms = null)
    {
        ArgumentNullException.ThrowIfNull(speakers);
        ArgumentNullException.ThrowIfNull(turns);

        if (speakers.Count != turns.Count)
        {
            throw new ArgumentException("There must be a speaker for each turn.", nameof(speakers));
        }

        var result = new string[turns.Count];
        var emails = new List<string>();

        foreach (var speaker in speakers.Distinct(StringComparer.Ordinal))
        {
            // The speaker's turns in groups: another speaker's long turn ends a group, a short one is stepped over.
            var groups = new List<List<int>>();
            List<int> group = null;

            for (var i = 0; i < turns.Count; i++)
            {
                if (string.Equals(speakers[i], speaker, StringComparison.Ordinal))
                {
                    if (group is null)
                    {
                        group = [];
                        groups.Add(group);
                    }

                    group.Add(i);
                }
                else if ((turns[i]?.Length ?? 0) > MaxInterleavedTurnLength)
                {
                    group = null;
                }
            }

            foreach (var indexes in groups)
            {
                var redacted = RedactPatternsInTurns([.. indexes.Select(index => turns[index])], emails);

                for (var i = 0; i < indexes.Count; i++)
                {
                    result[indexes[i]] = redacted[i];
                }
            }
        }

        var parts = GetEmailParts(emails);

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = RedactTerms(RedactEmailFragments(result[i], parts), terms);
        }

        return result;
    }

    // The words an address is said in pieces: "mike", "example", "com" of "mike at example dot com".
    private static HashSet<string> GetEmailParts(IEnumerable<string> emails)
    {
        var parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var email in emails)
        {
            foreach (var part in EmailWordSeparator().Split(email))
            {
                if (part.Length >= 2 && !part.Equals("at", StringComparison.OrdinalIgnoreCase) && !part.Equals("dot", StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(part);
                }
            }
        }

        return parts;
    }

    // Takes a part of a found address out of a turn when it is said as part of one: next to "at" or "dot" or a
    // dot, or as the whole turn. A part alone in a longer sentence ("I will test it") is not touched.
    private static string RedactEmailFragments(string turn, HashSet<string> parts)
    {
        if (string.IsNullOrWhiteSpace(turn) || parts.Count == 0)
        {
            return turn;
        }

        var part = "(?:" + string.Join('|', parts.OrderByDescending(p => p.Length).Select(Regex.Escape)) + ")";
        var connector = @"(?:\s+(?:at|dot)(?![\p{L}\p{N}])|\s*[.@＠])";
        var options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var timeout = TimeSpan.FromSeconds(1);
        var boundary = @"(?<![\p{L}\p{N}])";

        // Whole turn: nothing but parts and connectors, such as "Test", "example.com" or "dot com".
        var words = EmailWordSeparator().Split(turn).Where(word => word.Length > 0).ToList();

        if (words.Count > 0
            && words.Any(word => parts.Contains(word))
            && words.All(word => parts.Contains(word) || word.Equals("at", StringComparison.OrdinalIgnoreCase) || word.Equals("dot", StringComparison.OrdinalIgnoreCase)))
        {
            return EmailPlaceholder;
        }

        // Parts chained by connectors ("mike at example dot com", "example.com").
        turn = Regex.Replace(turn, boundary + part + "(?:" + connector + @"\s*" + part + ")+(?![\\p{L}\\p{N}])", EmailPlaceholder, options, timeout);

        // A part and a connector that ends the turn ("my email is mike at").
        return Regex.Replace(turn, boundary + part + connector + @"[\s\p{P}]*$", EmailPlaceholder, options, timeout);
    }

    /// <summary>
    /// Replaces the details recognized by pattern in consecutive turns of one speaker. See
    /// <see cref="RedactTurns"/>.
    /// </summary>
    /// <param name="turns">The turns of one speaker, in order.</param>
    /// <returns>The redacted turns, one for each turn.</returns>
    public static IReadOnlyList<string> RedactPatternsInTurns(IReadOnlyList<string> turns)
    {
        return RedactPatternsInTurns(turns, null);
    }

    private static string[] RedactPatternsInTurns(IReadOnlyList<string> turns, List<string> emails)
    {
        ArgumentNullException.ThrowIfNull(turns);

        var starts = new int[turns.Count];
        var joined = new StringBuilder();

        for (var i = 0; i < turns.Count; i++)
        {
            if (string.IsNullOrEmpty(turns[i]))
            {
                continue;
            }

            if (joined.Length > 0)
            {
                joined.Append(' ');
            }

            starts[i] = joined.Length;
            joined.Append(turns[i]);
        }

        var text = joined.ToString();
        var found = new List<(int Start, int End, string Placeholder)>();

        // The order is the priority: a span taken by an earlier pattern is not taken again.
        AddMatches(found, EmailPattern().Matches(text), _ => EmailPlaceholder, emails);
        AddMatches(found, SpokenEmailPattern().Matches(text), _ => EmailPlaceholder, emails);
        AddMatches(found, CardPattern().Matches(text), match => IsCardNumber(match.Value) ? CardPlaceholder : null);
        AddMatches(found, SocialSecurityPattern().Matches(text), _ => IdPlaceholder);
        AddMatches(found, PhonePattern().Matches(text), match => IsPhoneNumber(match.Value) ? PhonePlaceholder : null);

        var result = new string[turns.Count];

        for (var i = 0; i < turns.Count; i++)
        {
            var turn = turns[i];

            if (string.IsNullOrEmpty(turn) || found.Count == 0)
            {
                result[i] = turn;

                continue;
            }

            var start = starts[i];
            var end = start + turn.Length;
            var builder = new StringBuilder();
            var position = start;

            foreach (var (matchStart, matchEnd, placeholder) in found.OrderBy(match => match.Start))
            {
                if (matchEnd <= position || matchStart >= end)
                {
                    continue;
                }

                builder.Append(text, position, Math.Max(matchStart, position) - position);
                builder.Append(placeholder);
                position = Math.Min(matchEnd, end);
            }

            builder.Append(text, position, end - position);
            result[i] = builder.ToString();
        }

        return result;
    }

    private static void AddMatches(List<(int Start, int End, string Placeholder)> found, MatchCollection matches, Func<Match, string> getPlaceholder, List<string> emails = null)
    {
        var taken = found.ToList();

        foreach (Match match in matches)
        {
            var placeholder = getPlaceholder(match);
            var end = match.Index + match.Length;

            if (placeholder is null || taken.Any(other => match.Index < other.End && end > other.Start))
            {
                continue;
            }

            found.Add((match.Index, end, placeholder));
            emails?.Add(match.Value);
        }
    }

    /// <summary>
    /// Replaces each found detail wherever it appears, ignoring case, longest first so a full name goes before the
    /// first name inside it.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="terms">The details.</param>
    /// <returns>The redacted text.</returns>
    public static string RedactTerms(string text, IEnumerable<AIPersonalDataTerm> terms)
    {
        if (string.IsNullOrEmpty(text) || terms is null)
        {
            return text;
        }

        foreach (var term in terms
            .Where(term => term?.Text?.Trim().Length >= 2)
            .OrderByDescending(term => term.Text.Trim().Length))
        {
            // Whole words only, so a name like "Al" does not take the letters out of "also".
            var pattern = @"(?<![\p{L}\p{N}])" + Regex.Escape(term.Text.Trim()) + @"(?![\p{L}\p{N}])";
            var placeholder = IsEmailAddress(term.Text.Trim()) ? EmailPlaceholder : GetPlaceholder(term.Kind);
            text = Regex.Replace(text, pattern, placeholder,RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }

        return text;
    }

    /// <summary>
    /// Gets the placeholder for a kind of detail.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The placeholder; <see cref="IdPlaceholder"/> for an unknown kind.</returns>
    public static string GetPlaceholder(string kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            "name" => NamePlaceholder,
            "address" => AddressPlaceholder,
            "birthdate" => BirthDatePlaceholder,
            "email" => EmailPlaceholder,
            "phone" => PhonePlaceholder,
            _ => IdPlaceholder,
        };
    }

    // A detail the model labeled an identity number can still be an address, written or dictated.
    private static bool IsEmailAddress(string value)
    {
        var match = SpokenEmailPattern().Match(value);

        return match.Success && match.Length == value.Length;
    }

    internal static bool IsCardNumber(string value)
    {
        var digits = value.Where(char.IsAsciiDigit).Select(digit => digit - '0').ToArray();

        if (digits.Length is < 13 or > 19)
        {
            return false;
        }

        var sum = 0;

        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i];

            if (i % 2 == 1)
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

    internal static bool IsPhoneNumber(string value)
    {
        if (DatePattern().IsMatch(value.Trim()))
        {
            return false;
        }

        var digits = value.Count(char.IsAsciiDigit);

        return value.TrimStart().StartsWith('+')
            ? digits is >= 8 and <= 15
            : digits is >= 10 and <= 15;
    }

    [GeneratedRegex(@"[\w.+-]+[@＠][\w-]+(?:\.[\w-]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailWordSeparator();

    // An address as it is dictated: "mike at example dot com", "mike at example.com", "mike@example dot com". The
    // last label must be letters only, so "free at 5.30pm" is not an address, and the word after "at" is not a
    // pronoun or article, so "look at the dot com boom" is not either.
    [GeneratedRegex(@"(?<![\w.+@＠-])(?:[\w+-]+(?:\.|\s+dot\s+))*[\w+-]+(?:\s*[@＠]\s*|\s+at\s+(?!(?:the|a|an|this|that|my|our|your|his|her|their|its|it|me|you|us|them)\s))[a-z0-9-]+(?:(?:\.|\s+dot\s+)[a-z0-9-]+)*(?:\.|\s+dot\s+)[a-z]{2,}(?![\w-])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SpokenEmailPattern();

    [GeneratedRegex(@"(?<![\d-])(?:\d[ -]?){12,18}\d(?![\d-])", RegexOptions.CultureInvariant)]
    private static partial Regex CardPattern();

    [GeneratedRegex(@"(?<!\d)\d{3}-\d{2}-\d{4}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex SocialSecurityPattern();

    [GeneratedRegex(@"(?<![\w+])\+?\(?\d[\d ().-]{6,}\d(?!\w)", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^\d{4}-\d{1,2}-\d{1,2}$|^\d{1,2}[./-]\d{1,2}[./-]\d{2,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();
}
