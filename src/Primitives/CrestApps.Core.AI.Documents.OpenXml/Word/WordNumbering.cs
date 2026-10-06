using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.OpenXml.Word;

/// <summary>
/// Writes the numbering definitions lists are built on, so a bulleted or numbered list is a real list that
/// Word renumbers, indents and reads aloud as one, not paragraphs that start with a typed marker.
/// </summary>
internal static class WordNumbering
{
    private const string BulletDefinitionName = "CrestApps Bullets";
    private const string NumberDefinitionName = "CrestApps Numbers";

    private static readonly string[] _bulletMarkers = ["•", "◦", "▪"];

    /// <summary>
    /// Starts a new list. Every numbered list restarts at its own first number.
    /// </summary>
    /// <param name="mainPart">The main document part.</param>
    /// <param name="numbered">Whether the list is numbered rather than bulleted.</param>
    /// <param name="start">The first number of a numbered list.</param>
    /// <returns>The numbering instance the list's paragraphs refer to.</returns>
    public static int CreateList(MainDocumentPart mainPart, bool numbered, int start = 1)
    {
        ArgumentNullException.ThrowIfNull(mainPart);

        var numbering = GetOrCreate(mainPart);
        var abstractNumberId = EnsureDefinition(numbering, numbered);
        var numberId = numbering.Elements<NumberingInstance>()
            .Select(instance => instance.NumberID?.Value ?? 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        var instance = new NumberingInstance(new AbstractNumId { Val = abstractNumberId })
        {
            NumberID = numberId,
        };

        if (numbered)
        {
            instance.Append(new LevelOverride(new StartOverrideNumberingValue { Val = Math.Max(0, start) })
            {
                LevelIndex = 0,
            });
        }

        var cleanup = numbering.GetFirstChild<NumberingIdMacAtCleanup>();

        if (cleanup is null)
        {
            numbering.Append(instance);
        }
        else
        {
            cleanup.InsertBeforeSelf(instance);
        }

        return numberId;
    }

    /// <summary>
    /// Returns the numbering definitions of a document, creating the part when the document has none.
    /// </summary>
    /// <param name="mainPart">The main document part.</param>
    /// <returns>The numbering definitions.</returns>
    public static Numbering GetOrCreate(MainDocumentPart mainPart)
    {
        ArgumentNullException.ThrowIfNull(mainPart);

        var part = mainPart.NumberingDefinitionsPart ?? mainPart.AddNewPart<NumberingDefinitionsPart>();

        part.Numbering ??= new Numbering();

        return part.Numbering;
    }

    private static int EnsureDefinition(Numbering numbering, bool numbered)
    {
        var name = numbered ? NumberDefinitionName : BulletDefinitionName;
        var existing = numbering.Elements<AbstractNum>()
            .FirstOrDefault(definition => string.Equals(definition.AbstractNumDefinitionName?.Val?.Value, name, StringComparison.Ordinal));

        if (existing?.AbstractNumberId?.Value is { } existingId)
        {
            return existingId;
        }

        var abstractNumberId = numbering.Elements<AbstractNum>()
            .Select(definition => definition.AbstractNumberId?.Value ?? -1)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        var definition = new AbstractNum
        {
            AbstractNumberId = abstractNumberId,
            MultiLevelType = new MultiLevelType { Val = MultiLevelValues.HybridMultilevel },
            AbstractNumDefinitionName = new AbstractNumDefinitionName { Val = name },
        };

        for (var level = 0; level < 9; level++)
        {
            definition.Append(numbered ? CreateNumberLevel(level) : CreateBulletLevel(level));
        }

        // Every abstract definition has to come before the first numbering instance.
        var lastDefinition = numbering.Elements<AbstractNum>().LastOrDefault();

        if (lastDefinition is not null)
        {
            lastDefinition.InsertAfterSelf(definition);
        }
        else if (numbering.GetFirstChild<NumberingInstance>() is { } firstInstance)
        {
            firstInstance.InsertBeforeSelf(definition);
        }
        else
        {
            numbering.Append(definition);
        }

        return abstractNumberId;
    }

    private static Level CreateBulletLevel(int level)
    {
        return new Level(
            new StartNumberingValue { Val = 1 },
            new NumberingFormat { Val = NumberFormatValues.Bullet },
            new LevelText { Val = _bulletMarkers[level % _bulletMarkers.Length] },
            new LevelJustification { Val = LevelJustificationValues.Left },
            new PreviousParagraphProperties(new Indentation
            {
                Left = WordUnits.Invariant(360L * (level + 1) + 360),
                Hanging = "360",
            }),
            new NumberingSymbolRunProperties(new RunFonts
            {
                Ascii = "Calibri",
                HighAnsi = "Calibri",
                Hint = FontTypeHintValues.Default,
            }))
        {
            LevelIndex = level,
        };
    }

    private static Level CreateNumberLevel(int level)
    {
        var format = (level % 3) switch
        {
            0 => NumberFormatValues.Decimal,
            1 => NumberFormatValues.LowerLetter,
            _ => NumberFormatValues.LowerRoman,
        };

        return new Level(
            new StartNumberingValue { Val = 1 },
            new NumberingFormat { Val = format },
            new LevelText { Val = "%" + WordUnits.Invariant(level + 1) + "." },
            new LevelJustification { Val = LevelJustificationValues.Left },
            new PreviousParagraphProperties(new Indentation
            {
                Left = WordUnits.Invariant(360L * (level + 1) + 360),
                Hanging = "360",
            }))
        {
            LevelIndex = level,
        };
    }
}
