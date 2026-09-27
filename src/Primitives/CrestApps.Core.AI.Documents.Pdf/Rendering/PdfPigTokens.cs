using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace CrestApps.Core.AI.Documents.Pdf.Rendering;

/// <summary>
/// Reads values out of PdfPig's raw dictionaries, following indirect references.
/// </summary>
internal static class PdfPigTokens
{
    /// <summary>
    /// Follows an indirect reference to the object it points at.
    /// </summary>
    /// <param name="document">The document the token belongs to.</param>
    /// <param name="token">The token.</param>
    /// <returns>The direct token, or <see langword="null"/> when it cannot be resolved.</returns>
    public static IToken Resolve(PdfDocument document, IToken token)
    {
        var current = token;

        for (var depth = 0; depth < 8 && current is IndirectReferenceToken reference; depth++)
        {
            try
            {
                current = document.Structure.GetObject(reference.Data)?.Data;
            }
            catch (Exception)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>
    /// Reads an entry of a dictionary as a dictionary.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The entry name, without the slash.</param>
    /// <returns>The dictionary, or <see langword="null"/>.</returns>
    public static DictionaryToken GetDictionary(PdfDocument document, DictionaryToken dictionary, string key)
    {
        if (dictionary is null || !dictionary.TryGet(NameToken.Create(key), out var value))
        {
            return null;
        }

        return Resolve(document, value) switch
        {
            DictionaryToken direct => direct,
            StreamToken stream => stream.StreamDictionary,
            _ => null,
        };
    }

    /// <summary>
    /// Reads an entry of a dictionary as a number.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The entry name, without the slash.</param>
    /// <returns>The number, or <see langword="null"/>.</returns>
    public static double? GetNumber(PdfDocument document, DictionaryToken dictionary, string key)
    {
        if (dictionary is null || !dictionary.TryGet(NameToken.Create(key), out var value))
        {
            return null;
        }

        return Resolve(document, value) is NumericToken number
            ? number.Double
            : null;
    }

    /// <summary>
    /// Reads an entry of a dictionary as text.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The entry name, without the slash.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    public static string GetText(PdfDocument document, DictionaryToken dictionary, string key)
    {
        if (dictionary is null || !dictionary.TryGet(NameToken.Create(key), out var value))
        {
            return null;
        }

        return Resolve(document, value) switch
        {
            StringToken text => text.Data,
            HexToken hex => hex.Data,
            NameToken name => name.Data,
            _ => null,
        };
    }

    /// <summary>
    /// Reads an entry of a dictionary as an array of numbers.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The entry name, without the slash.</param>
    /// <returns>The numbers, or an empty list.</returns>
    public static List<double> GetNumbers(PdfDocument document, DictionaryToken dictionary, string key)
    {
        var numbers = new List<double>();

        if (dictionary is null ||
            !dictionary.TryGet(NameToken.Create(key), out var value) ||
            Resolve(document, value) is not ArrayToken array)
        {
            return numbers;
        }

        foreach (var item in array.Data)
        {
            if (Resolve(document, item) is NumericToken number)
            {
                numbers.Add(number.Double);
            }
        }

        return numbers;
    }
}
