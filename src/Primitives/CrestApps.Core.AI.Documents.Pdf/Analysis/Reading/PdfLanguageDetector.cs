using System.Runtime.InteropServices;
using CrestApps.Core.AI.Documents.Pdf.Rendering;
using UglyToad.PdfPig;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Tells what language text is written in, without a model: first by the writing system its letters belong
/// to, and for text in the Latin alphabet by the very common words — articles, prepositions, conjunctions —
/// every language uses constantly and no two languages share all of.
/// </summary>
/// <remarks>
/// A script names the language outright for most non-Latin writing (Hangul is Korean, Thai is Thai), with
/// letters peculiar to one language settling the rest (Ukrainian <c>ї</c>, Persian <c>پ</c>). Latin text is
/// scored against a short profile of common words per language, words shared by several languages counting
/// for less, with letters such as <c>ß</c>, <c>ñ</c> or <c>ł</c> breaking ties. That is reliable on a
/// paragraph or more and says so when the text is too short to tell.
/// </remarks>
internal static class PdfLanguageDetector
{
    private const string Undetermined = "und";

    private static readonly (int Start, int End, string Script)[] _scripts =
    [
        (0x0370, 0x03FF, "Greek"),
        (0x1F00, 0x1FFF, "Greek"),
        (0x0400, 0x052F, "Cyrillic"),
        (0x0530, 0x058F, "Armenian"),
        (0x0590, 0x05FF, "Hebrew"),
        (0x0600, 0x06FF, "Arabic"),
        (0x0750, 0x077F, "Arabic"),
        (0xFB50, 0xFDFF, "Arabic"),
        (0xFE70, 0xFEFF, "Arabic"),
        (0x0900, 0x097F, "Devanagari"),
        (0x0980, 0x09FF, "Bengali"),
        (0x0A00, 0x0A7F, "Gurmukhi"),
        (0x0A80, 0x0AFF, "Gujarati"),
        (0x0B80, 0x0BFF, "Tamil"),
        (0x0C00, 0x0C7F, "Telugu"),
        (0x0C80, 0x0CFF, "Kannada"),
        (0x0D00, 0x0D7F, "Malayalam"),
        (0x0D80, 0x0DFF, "Sinhala"),
        (0x0E00, 0x0E7F, "Thai"),
        (0x0E80, 0x0EFF, "Lao"),
        (0x1000, 0x109F, "Myanmar"),
        (0x10A0, 0x10FF, "Georgian"),
        (0x1100, 0x11FF, "Hangul"),
        (0x3130, 0x318F, "Hangul"),
        (0xAC00, 0xD7AF, "Hangul"),
        (0x1200, 0x137F, "Ethiopic"),
        (0x1780, 0x17FF, "Khmer"),
        (0x1E00, 0x1EFF, "Latin"),
        (0x3040, 0x30FF, "Kana"),
        (0x31F0, 0x31FF, "Kana"),
        (0xFF66, 0xFF9F, "Kana"),
        (0x3400, 0x4DBF, "Han"),
        (0x4E00, 0x9FFF, "Han"),
        (0xF900, 0xFAFF, "Han"),
    ];

    private static readonly Dictionary<string, string> _scriptLanguages = new(StringComparer.Ordinal)
    {
        ["Greek"] = "el",
        ["Armenian"] = "hy",
        ["Hebrew"] = "he",
        ["Devanagari"] = "hi",
        ["Bengali"] = "bn",
        ["Gurmukhi"] = "pa",
        ["Gujarati"] = "gu",
        ["Tamil"] = "ta",
        ["Telugu"] = "te",
        ["Kannada"] = "kn",
        ["Malayalam"] = "ml",
        ["Sinhala"] = "si",
        ["Thai"] = "th",
        ["Lao"] = "lo",
        ["Myanmar"] = "my",
        ["Georgian"] = "ka",
        ["Hangul"] = "ko",
        ["Ethiopic"] = "am",
        ["Khmer"] = "km",
        ["Kana"] = "ja",
        ["Han"] = "zh",
    };

    private static readonly Dictionary<string, string> _names = new(StringComparer.Ordinal)
    {
        [Undetermined] = "Undetermined",
        ["en"] = "English",
        ["fr"] = "French",
        ["de"] = "German",
        ["es"] = "Spanish",
        ["it"] = "Italian",
        ["pt"] = "Portuguese",
        ["nl"] = "Dutch",
        ["sv"] = "Swedish",
        ["da"] = "Danish",
        ["no"] = "Norwegian",
        ["fi"] = "Finnish",
        ["pl"] = "Polish",
        ["tr"] = "Turkish",
        ["cs"] = "Czech",
        ["ro"] = "Romanian",
        ["hu"] = "Hungarian",
        ["id"] = "Indonesian",
        ["vi"] = "Vietnamese",
        ["el"] = "Greek",
        ["ru"] = "Russian",
        ["uk"] = "Ukrainian",
        ["be"] = "Belarusian",
        ["bg"] = "Bulgarian",
        ["sr"] = "Serbian",
        ["mk"] = "Macedonian",
        ["hy"] = "Armenian",
        ["he"] = "Hebrew",
        ["ar"] = "Arabic",
        ["fa"] = "Persian",
        ["ur"] = "Urdu",
        ["hi"] = "Hindi",
        ["bn"] = "Bengali",
        ["pa"] = "Punjabi",
        ["gu"] = "Gujarati",
        ["ta"] = "Tamil",
        ["te"] = "Telugu",
        ["kn"] = "Kannada",
        ["ml"] = "Malayalam",
        ["si"] = "Sinhala",
        ["th"] = "Thai",
        ["lo"] = "Lao",
        ["my"] = "Burmese",
        ["ka"] = "Georgian",
        ["ko"] = "Korean",
        ["am"] = "Amharic",
        ["km"] = "Khmer",
        ["ja"] = "Japanese",
        ["zh"] = "Chinese",
    };

    private static readonly Dictionary<string, string[]> _stopWords = new(StringComparer.Ordinal)
    {
        ["en"] = ["the", "and", "of", "to", "in", "is", "that", "for", "it", "with", "as", "was", "on", "are", "be", "this", "by", "not", "or", "have", "from", "which", "at", "an", "they", "were", "has", "their", "will", "would"],
        ["fr"] = ["le", "la", "les", "et", "des", "est", "un", "une", "du", "que", "qui", "dans", "pour", "pas", "sur", "au", "avec", "ce", "il", "sont", "par", "plus", "ne", "se", "nous", "vous", "aux", "mais", "leur", "été"],
        ["de"] = ["der", "die", "und", "das", "ist", "nicht", "den", "mit", "von", "zu", "sich", "des", "auf", "ein", "eine", "dem", "für", "im", "auch", "es", "wird", "sind", "oder", "werden", "bei", "wir", "nach", "wie", "über", "einer"],
        ["es"] = ["el", "la", "de", "que", "y", "en", "los", "las", "del", "se", "por", "un", "una", "con", "para", "es", "al", "no", "lo", "como", "más", "pero", "su", "sus", "está", "este", "esta", "son", "entre", "también"],
        ["it"] = ["il", "di", "che", "e", "la", "per", "un", "una", "del", "della", "non", "sono", "in", "con", "gli", "le", "si", "da", "dei", "alla", "è", "anche", "come", "più", "questo", "nel", "delle", "ha", "essere", "questa"],
        ["pt"] = ["o", "a", "de", "que", "e", "do", "da", "em", "um", "uma", "para", "com", "não", "os", "as", "no", "na", "por", "se", "mais", "dos", "das", "ao", "é", "são", "foi", "como", "mas", "também", "pelo"],
        ["nl"] = ["de", "het", "een", "en", "van", "is", "dat", "in", "op", "te", "zijn", "voor", "met", "niet", "die", "aan", "er", "ook", "als", "bij", "door", "wordt", "maar", "om", "naar", "dit", "worden", "deze", "uit", "heeft"],
        ["sv"] = ["och", "att", "det", "som", "en", "på", "är", "av", "för", "med", "till", "den", "har", "de", "inte", "om", "ett", "var", "jag", "men", "från", "eller", "vid", "kan", "sig", "detta", "också", "efter", "vi", "under"],
        ["da"] = ["og", "at", "det", "er", "en", "til", "af", "på", "som", "med", "for", "ikke", "den", "de", "har", "et", "jeg", "men", "fra", "eller", "der", "var", "efter", "hvad", "mig", "også", "kan", "vi", "skal", "blev"],
        ["no"] = ["og", "i", "det", "er", "som", "på", "en", "av", "for", "til", "med", "at", "ikke", "har", "de", "den", "et", "jeg", "men", "fra", "eller", "var", "etter", "hva", "meg", "også", "kan", "vi", "skal", "ble"],
        ["fi"] = ["ja", "on", "ei", "se", "että", "oli", "hän", "mutta", "kun", "niin", "tai", "jos", "ovat", "myös", "sen", "joka", "mitä", "tämä", "kuin", "vain", "olla", "ole", "sekä", "jotka", "voi", "hänen", "siitä", "kanssa", "mukaan", "ovat"],
        ["pl"] = ["i", "w", "na", "z", "się", "nie", "do", "to", "że", "jest", "o", "jak", "ale", "po", "co", "tak", "za", "od", "przez", "dla", "są", "może", "jego", "oraz", "lub", "także", "który", "które", "być", "już"],
        ["tr"] = ["ve", "bir", "bu", "da", "de", "için", "ile", "çok", "olarak", "daha", "gibi", "olan", "ne", "ama", "ki", "en", "kadar", "her", "sonra", "değil", "var", "veya", "göre", "ise", "olduğu", "şu", "ancak", "mi", "ya", "çünkü"],
        ["cs"] = ["a", "se", "na", "je", "v", "že", "to", "s", "z", "do", "o", "jako", "pro", "ale", "by", "jsou", "jeho", "které", "když", "jak", "tak", "byl", "však", "nebo", "také", "není", "jsem", "podle", "až", "který"],
        ["ro"] = ["și", "şi", "de", "la", "în", "a", "cu", "pe", "că", "nu", "este", "un", "o", "din", "care", "pentru", "mai", "se", "sunt", "ca", "sau", "fost", "această", "lui", "dar", "au", "fi", "prin", "ale", "acest"],
        ["hu"] = ["a", "az", "és", "hogy", "nem", "is", "egy", "meg", "van", "de", "ez", "csak", "már", "mint", "még", "volt", "el", "azt", "ki", "ha", "vagy", "lesz", "pedig", "kell", "minden", "amely", "után", "szerint", "között", "lehet"],
        ["id"] = ["yang", "dan", "di", "ini", "itu", "dengan", "untuk", "dari", "dalam", "tidak", "akan", "pada", "ke", "juga", "ada", "adalah", "oleh", "karena", "atau", "bisa", "mereka", "kami", "telah", "sudah", "saya", "dapat", "lebih", "tersebut", "seperti", "hanya"],
        ["vi"] = ["và", "của", "là", "có", "trong", "không", "được", "cho", "các", "một", "những", "với", "người", "này", "đã", "để", "khi", "từ", "đến", "như", "cũng", "về", "thì", "nhưng", "ra", "năm", "sẽ", "còn", "đó", "nhiều"],
    };

    private static readonly Dictionary<char, string[]> _letterHints = new()
    {
        ['ß'] = ["de"],
        ['ñ'] = ["es"],
        ['ã'] = ["pt"],
        ['õ'] = ["pt"],
        ['ç'] = ["fr", "pt", "tr"],
        ['œ'] = ["fr"],
        ['è'] = ["fr", "it"],
        ['ê'] = ["fr", "pt"],
        ['ù'] = ["fr", "it"],
        ['û'] = ["fr"],
        ['ë'] = ["fr", "nl"],
        ['ï'] = ["fr", "nl"],
        ['ò'] = ["it"],
        ['ì'] = ["it"],
        ['ä'] = ["de", "sv", "fi"],
        ['ö'] = ["de", "sv", "fi", "tr", "hu"],
        ['ü'] = ["de", "tr", "hu"],
        ['å'] = ["sv", "da", "no"],
        ['æ'] = ["da", "no"],
        ['ø'] = ["da", "no"],
        ['ő'] = ["hu"],
        ['ű'] = ["hu"],
        ['ł'] = ["pl"],
        ['ś'] = ["pl"],
        ['ź'] = ["pl"],
        ['ż'] = ["pl"],
        ['ć'] = ["pl"],
        ['ń'] = ["pl"],
        ['ę'] = ["pl"],
        ['ą'] = ["pl"],
        ['ğ'] = ["tr"],
        ['ş'] = ["tr", "ro"],
        ['ı'] = ["tr"],
        ['ř'] = ["cs"],
        ['ů'] = ["cs"],
        ['ě'] = ["cs"],
        ['ș'] = ["ro"],
        ['ț'] = ["ro"],
        ['ţ'] = ["ro"],
        ['ă'] = ["ro", "vi"],
        ['đ'] = ["vi"],
        ['ơ'] = ["vi"],
        ['ư'] = ["vi"],
    };

    private static readonly Dictionary<string, double> _wordWeights = BuildWordWeights();

    /// <summary>
    /// Detects the language of text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The language, or <c>und</c> when the text holds too few letters to tell.</returns>
    public static PdfLanguageDetection Detect(string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var letters = 0;

        foreach (var character in text ?? string.Empty)
        {
            if (!char.IsLetter(character))
            {
                continue;
            }

            letters++;

            var script = ScriptOf(character);

            CollectionsMarshal.GetValueRefOrAddDefault(counts, script, out _)++;
        }

        if (letters < 10)
        {
            return new PdfLanguageDetection(Undetermined, NameOf(Undetermined), null, 0, letters, null);
        }

        var (dominant, dominantCount) = counts.MaxBy(entry => entry.Value);

        // Chinese characters are written in Japanese too; kana among them make it Japanese.
        if (dominant == "Han" && counts.TryGetValue("Kana", out var kana) && kana >= (kana + dominantCount) * 0.05)
        {
            dominant = "Kana";
            dominantCount += kana;
        }

        var share = (double)dominantCount / letters;
        var sizeFactor = 0.5 + (0.5 * Math.Min(1, letters / 200.0));

        if (dominant == "Latin")
        {
            return DetectLatin(text, letters, share);
        }

        var code = dominant switch
        {
            "Cyrillic" => CyrillicLanguage(text, dominantCount),
            "Arabic" => ArabicLanguage(text),
            _ => _scriptLanguages.GetValueOrDefault(dominant, Undetermined),
        };

        var scriptName = dominant == "Kana"
            ? "Japanese"
            : dominant;

        return new PdfLanguageDetection(code, NameOf(code), scriptName, Math.Round(Math.Clamp(share * sizeFactor, 0, 1), 2), letters, null);
    }

    /// <summary>
    /// Reads the language a document declares for its text, in its catalog's <c>/Lang</c> entry.
    /// </summary>
    /// <param name="pdf">The document.</param>
    /// <returns>The BCP 47 tag, or <see langword="null"/> when the document declares none.</returns>
    public static string ReadDeclaredLanguage(PdfDocument pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        try
        {
            var value = PdfPigTokens.GetText(pdf, pdf.Structure.Catalog.CatalogDictionary, "Lang")?.Trim();

            return string.IsNullOrEmpty(value)
                ? null
                : value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the English name of a language.
    /// </summary>
    /// <param name="code">The BCP 47 tag; only its primary language subtag is read.</param>
    /// <returns>The name, or the tag itself when it is not one this detector knows.</returns>
    public static string NameOf(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return _names[Undetermined];
        }

        var primary = PrimaryTag(code);

        return _names.TryGetValue(primary, out var name)
            ? name
            : code;
    }

    /// <summary>
    /// Reads the primary language subtag of a BCP 47 tag, lower-cased, treating the Norwegian written
    /// standards as Norwegian.
    /// </summary>
    /// <param name="code">The tag, such as <c>en-US</c>.</param>
    /// <returns>The primary subtag, such as <c>en</c>.</returns>
    public static string PrimaryTag(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Undetermined;
        }

        var primary = code.Trim().Split('-', '_')[0].ToLowerInvariant();

        return primary is "nb" or "nn"
            ? "no"
            : primary;
    }

    private static string ScriptOf(char character)
    {
        if (character <= 0x024F)
        {
            return "Latin";
        }

        foreach (var (start, end, script) in _scripts)
        {
            if (character >= start && character <= end)
            {
                return script;
            }
        }

        return "Other";
    }

    private static PdfLanguageDetection DetectLatin(string text, int letters, double share)
    {
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        var hits = new Dictionary<string, int>(StringComparer.Ordinal);
        var tokens = 0;

        foreach (var word in Tokens(text))
        {
            tokens++;

            if (!_wordWeights.TryGetValue(word, out var weight))
            {
                continue;
            }

            foreach (var (language, words) in _stopWords)
            {
                if (Array.IndexOf(words, word) >= 0)
                {
                    CollectionsMarshal.GetValueRefOrAddDefault(scores, language, out _) += weight;
                    CollectionsMarshal.GetValueRefOrAddDefault(hits, language, out _)++;
                }
            }
        }

        var hintCap = Math.Max(1, tokens * 0.25);
        var hints = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var character in text)
        {
            if (_letterHints.TryGetValue(char.ToLowerInvariant(character), out var languages))
            {
                foreach (var language in languages)
                {
                    CollectionsMarshal.GetValueRefOrAddDefault(hints, language, out _) += 0.5 / languages.Length;
                }
            }
            else if (character is >= 'Ạ' and <= 'ỹ')
            {
                // Vietnamese marks its tones on the vowels, with letters no other language uses.
                CollectionsMarshal.GetValueRefOrAddDefault(hints, "vi", out _) += 0.5;
            }
        }

        foreach (var (language, hint) in hints)
        {
            CollectionsMarshal.GetValueRefOrAddDefault(scores, language, out _) += Math.Min(hint, hintCap);
        }

        if (tokens < 3 || scores.Count == 0)
        {
            return new PdfLanguageDetection(Undetermined, NameOf(Undetermined), "Latin", 0, letters, null);
        }

        var ranked = scores.OrderByDescending(entry => entry.Value).ToList();
        var best = ranked[0];
        var second = ranked.Count > 1
            ? ranked[1].Value
            : 0;
        var margin = best.Value <= 0
            ? 0
            : (best.Value - second) / best.Value;
        var coverage = (double)hits.GetValueOrDefault(best.Key) / tokens;
        var confidence = (0.35 + (0.65 * margin)) *
            Math.Min(1, coverage / 0.25) *
            (0.5 + (0.5 * Math.Min(1, tokens / 40.0))) *
            Math.Clamp(share, 0, 1);

        return new PdfLanguageDetection(
            best.Key,
            NameOf(best.Key),
            "Latin",
            Math.Round(Math.Clamp(confidence, 0, 1), 2),
            letters,
            ranked.Count > 1 && margin < 0.25 ? ranked[1].Key : null);
    }

    private static IEnumerable<string> Tokens(string text)
    {
        var start = -1;

        for (var index = 0; index <= text.Length; index++)
        {
            var isLetter = index < text.Length && char.IsLetter(text[index]);

            if (isLetter && start < 0)
            {
                start = index;
            }
            else if (!isLetter && start >= 0)
            {
                yield return text[start..index].ToLowerInvariant();
                start = -1;
            }
        }
    }

    private static string CyrillicLanguage(string text, int cyrillic)
    {
        var ukrainian = 0;
        var belarusian = 0;
        var serbian = 0;
        var macedonian = 0;
        var hardSign = 0;
        var russianOnly = 0;

        foreach (var character in text)
        {
            switch (char.ToLowerInvariant(character))
            {
                case 'і' or 'ї' or 'є' or 'ґ':
                    ukrainian++;

                    break;
                case 'ў':
                    belarusian++;

                    break;
                case 'ђ' or 'ј' or 'љ' or 'њ' or 'ћ' or 'џ':
                    serbian++;

                    break;
                case 'ѓ' or 'ќ' or 'ѕ':
                    macedonian++;

                    break;
                case 'ъ':
                    hardSign++;

                    break;
                case 'ы' or 'э' or 'ё':
                    russianOnly++;

                    break;
            }
        }

        if (belarusian > 0)
        {
            return "be";
        }

        if (macedonian > 0)
        {
            return "mk";
        }

        if (serbian > 0)
        {
            return "sr";
        }

        if (ukrainian >= Math.Max(1, cyrillic * 0.005))
        {
            return "uk";
        }

        // Bulgarian writes the hard sign constantly and has none of the letters only Russian uses.
        return hardSign >= cyrillic * 0.01 && russianOnly == 0
            ? "bg"
            : "ru";
    }

    private static string ArabicLanguage(string text)
    {
        var persian = 0;
        var urdu = 0;

        foreach (var character in text)
        {
            switch (character)
            {
                case 'پ' or 'چ' or 'ژ' or 'گ' or 'ی' or 'ک':
                    persian++;

                    break;
                case 'ٹ' or 'ڈ' or 'ڑ' or 'ں' or 'ے':
                    urdu++;

                    break;
            }
        }

        if (urdu > 0)
        {
            return "ur";
        }

        return persian > 2
            ? "fa"
            : "ar";
    }

    private static Dictionary<string, double> BuildWordWeights()
    {
        var languagesPerWord = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var words in _stopWords.Values)
        {
            foreach (var word in words.Distinct(StringComparer.Ordinal))
            {
                CollectionsMarshal.GetValueRefOrAddDefault(languagesPerWord, word, out _)++;
            }
        }

        // A word only one language uses is strong evidence; one that five languages use says little.
        return languagesPerWord.ToDictionary(entry => entry.Key, entry => 1 / Math.Sqrt(entry.Value), StringComparer.Ordinal);
    }
}
