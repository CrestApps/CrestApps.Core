using System.Globalization;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Rewrites text in place — paragraph by paragraph, by find and replace, or in the speaker notes — keeping
/// the formatting around it.
/// </summary>
/// <remarks>
/// The write-back half of rewriting, shortening, expanding and translating: the model reads the text with
/// <c>extract_presentation_content</c>, rewrites it, and hands it back here paragraph by paragraph, so bullets,
/// fonts and colours survive.
/// </remarks>
internal sealed class UpdateSlideTextTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.UpdateSlideText;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateSlideTextTool"/> class.
    /// </summary>
    public UpdateSlideTextTool()
        : base(
            TheName,
            "Rewrites text while keeping its formatting. 'paragraphs' replaces individual paragraphs, each given by slide, element #id and paragraph number exactly as extract_presentation_content kind 'text' lists them: use it to rewrite, shorten, expand, simplify or translate slides. 'replace' finds and replaces words across the deck (or chosen slides), including across formatting changes. 'notes' rewrites speaker notes. Several can be combined in one call.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "paragraphs": { "type": "array", "description": "Paragraph rewrites.", "items": { "type": "object", "properties": {
                  "slide": { "type": "integer" },
                  "element": { "type": ["integer", "string"], "description": "The element's #id or name." },
                  "paragraph": { "type": "integer", "description": "The paragraph number, counting from 1. Omit to replace all the element's text." },
                  "text": { "type": "string" }
                }, "required": ["slide", "element", "text"] } },
                "replace": { "type": "array", "description": "Find-and-replace operations.", "items": { "type": "object", "properties": {
                  "find": { "type": "string" }, "replace_with": { "type": "string" },
                  "match_case": { "type": "boolean" }, "whole_word": { "type": "boolean" }
                }, "required": ["find", "replace_with"] } },
                "slides": { "type": ["integer", "string", "array"], "description": "Limit find-and-replace to these slides." },
                "include_notes": { "type": "boolean", "description": "Find-and-replace in speaker notes too." },
                "notes": { "type": "array", "description": "Speaker notes to write.", "items": { "type": "object", "properties": {
                  "slide": { "type": "integer" }, "notes": { "type": "string" }
                }, "required": ["slide", "notes"] } }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Updates the text.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var edits = new List<PresentationEdit>();

        foreach (var item in call.Arguments.Array("paragraphs", "paragraph_edits", "edits").OfType<JsonObject>())
        {
            var slide = RequireSlide(item, model.Slides.Count);
            var element = PresentationArguments.ReadString(PresentationArguments.Find(item, "element", "element_id", "id"))
                ?? throw new PresentationArgumentException($"A paragraph rewrite on slide {slide} is missing its 'element'.");
            var text = PresentationArguments.ReadString(PresentationArguments.Find(item, "text", "new_text")) ?? string.Empty;
            var paragraph = PresentationArguments.ReadInt(PresentationArguments.Find(item, "paragraph", "paragraph_number"));

            edits.Add(new UpdateElementEdit
            {
                Slide = slide,
                Element = element,
                ParagraphNumber = paragraph,

                // One paragraph keeps its level, and so its bullet and indent; only its words change.
                Paragraphs = paragraph is { } number
                    ? [PresentationArguments.ParseLine(text.Trim(), ExistingLevel(model.Slides[slide - 1], element, number))]
                    : PresentationArguments.ReadParagraphs(JsonValue.Create(text)),
            });
        }

        var slides = call.Arguments.Slides(model.Slides.Count, "slides", "slide");
        var includeNotes = call.Arguments.Bool("include_notes") == true;

        foreach (var item in call.Arguments.Array("replace", "replacements", "find_replace").OfType<JsonObject>())
        {
            var find = PresentationArguments.ReadString(PresentationArguments.Find(item, "find", "search", "old"));

            if (string.IsNullOrEmpty(find))
            {
                throw new PresentationArgumentException("Each find-and-replace needs the text to 'find'.");
            }

            edits.Add(new ReplaceTextEdit
            {
                Slides = slides,
                Find = find,
                Replace = PresentationArguments.ReadString(PresentationArguments.Find(item, "replace_with", "replace", "replacement", "new")) ?? string.Empty,
                MatchCase = PresentationArguments.ReadBool(PresentationArguments.Find(item, "match_case")) == true,
                WholeWord = PresentationArguments.ReadBool(PresentationArguments.Find(item, "whole_word")) == true,
                IncludeNotes = includeNotes,
            });
        }

        foreach (var item in call.Arguments.Array("notes", "speaker_notes").OfType<JsonObject>())
        {
            edits.Add(new UpdateSlideEdit
            {
                Slide = RequireSlide(item, model.Slides.Count),
                Notes = PresentationArguments.ReadString(PresentationArguments.Find(item, "notes", "text")) ?? string.Empty,
            });
        }

        if (edits.Count == 0)
        {
            throw new PresentationArgumentException("Give 'paragraphs' to rewrite, 'replace' operations, or 'notes' to write.");
        }

        var result = await call.Session.ApplyAsync(deck, edits, "rewrote text", cancellationToken);

        return Changed(deck, result);
    }

    private static int ExistingLevel(PresentationSlide slide, string reference, int paragraph)
    {
        var trimmed = reference.Trim().TrimStart('#');
        var element = uint.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? slide.AllElements().FirstOrDefault(candidate => candidate.Id == id)
            : slide.AllElements().FirstOrDefault(candidate => string.Equals(candidate.Name, trimmed, StringComparison.OrdinalIgnoreCase));

        return element?.Text is { } text && paragraph >= 1 && paragraph <= text.Paragraphs.Count
            ? text.Paragraphs[paragraph - 1].Level
            : 0;
    }

    private static int RequireSlide(JsonObject item, int slideCount)
    {
        var slide = PresentationArguments.ReadInt(PresentationArguments.Find(item, "slide", "slide_number"))
            ?? throw new PresentationArgumentException("Each rewrite needs its 'slide'.");

        if (slide < 1 || slide > slideCount)
        {
            throw new PresentationArgumentException($"There is no slide {slide}: the deck has {slideCount} slide(s).");
        }

        return slide;
    }
}
