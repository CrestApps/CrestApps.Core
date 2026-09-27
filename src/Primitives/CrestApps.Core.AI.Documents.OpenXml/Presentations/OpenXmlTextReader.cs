using System.Globalization;
using CrestApps.Core.AI.Documents.Presentations.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Reads DrawingML text into the presentation model, resolving every property a run or paragraph does not
/// set through the list styles it inherits.
/// </summary>
internal static class OpenXmlTextReader
{
    private static readonly string[] _bulletKinds = ["buNone", "buChar", "buAutoNum", "buBlip"];

    /// <summary>
    /// Reads a text body.
    /// </summary>
    /// <param name="textBody">The <c>p:txBody</c>, <c>a:txBody</c> or SmartArt text body.</param>
    /// <param name="context">The slide context colours and fonts are read in.</param>
    /// <param name="listStyles">The list styles the text inherits from, most specific first.</param>
    /// <param name="bodyProperties">The body properties the text inherits from, most specific first.</param>
    /// <param name="defaults">What the text looks like where nothing else says.</param>
    /// <param name="directory">The deck's slides, for links that jump to one.</param>
    /// <returns>The text body.</returns>
    public static PresentationTextBody Read(
        OpenXmlElement textBody,
        OpenXmlSlideContext context,
        List<OpenXmlElement> listStyles,
        List<OpenXmlElement> bodyProperties,
        OpenXmlTextDefaults defaults,
        OpenXmlSlideDirectory directory)
    {
        var body = new PresentationTextBody();

        ReadBodyProperties(body, bodyProperties);

        if (textBody is null)
        {
            return body;
        }

        foreach (var paragraphElement in OpenXmlMarkup.Children(textBody, "p"))
        {
            body.Paragraphs.Add(ReadParagraph(paragraphElement, context, listStyles, defaults, body.FontScale, directory));
        }

        return body;
    }

    private static void ReadBodyProperties(PresentationTextBody body, List<OpenXmlElement> chain)
    {
        body.InsetLeft = FirstLong(chain, "lIns") ?? body.InsetLeft;
        body.InsetTop = FirstLong(chain, "tIns") ?? body.InsetTop;
        body.InsetRight = FirstLong(chain, "rIns") ?? body.InsetRight;
        body.InsetBottom = FirstLong(chain, "bIns") ?? body.InsetBottom;
        body.Wrap = FirstAttribute(chain, "wrap") != "none";
        body.Columns = (int)Math.Clamp(FirstLong(chain, "numCol") ?? 1, 1, 16);
        body.VerticalAnchor = FirstAttribute(chain, "anchor") switch
        {
            "ctr" => "middle",
            "b" => "bottom",
            _ => "top",
        };
        body.Direction = FirstAttribute(chain, "vert") is "vert" or "vert270" or "eaVert" or "wordArtVert" or "mongolianVert"
            ? "vertical"
            : "horizontal";

        foreach (var properties in chain)
        {
            var normal = OpenXmlMarkup.Child(properties, "normAutofit");

            if (normal is not null)
            {
                body.AutoFit = "shrink";
                body.FontScale = Math.Clamp((OpenXmlMarkup.Long(normal, "fontScale") ?? 100_000) / 100_000d, 0.05, 1);
                body.LineSpacingReduction = Math.Clamp((OpenXmlMarkup.Long(normal, "lnSpcReduction") ?? 0) / 100_000d, 0, 0.9);

                return;
            }

            if (OpenXmlMarkup.Child(properties, "spAutoFit") is not null)
            {
                body.AutoFit = "resize";

                return;
            }

            if (OpenXmlMarkup.Child(properties, "noAutofit") is not null)
            {
                body.AutoFit = "none";

                return;
            }
        }
    }

    private static PresentationParagraph ReadParagraph(
        OpenXmlElement paragraphElement,
        OpenXmlSlideContext context,
        List<OpenXmlElement> listStyles,
        OpenXmlTextDefaults defaults,
        double fontScale,
        OpenXmlSlideDirectory directory)
    {
        var properties = OpenXmlMarkup.Child(paragraphElement, "pPr");
        var level = (int)Math.Clamp(OpenXmlMarkup.Long(properties, "lvl") ?? 0, 0, 8);
        var levelChain = OpenXmlSlideContext.LevelChain(listStyles, level);
        var chain = new List<OpenXmlElement>(levelChain.Count + 1);

        if (properties is not null)
        {
            chain.Add(properties);
        }

        chain.AddRange(levelChain);

        var runDefaults = levelChain.Select(element => OpenXmlMarkup.Child(element, "defRPr")).Where(element => element is not null).ToList();

        var paragraph = new PresentationParagraph
        {
            Level = level,
            Alignment = FirstAttribute(chain, "algn") switch
            {
                "ctr" => "center",
                "r" => "right",
                "just" or "dist" or "justLow" or "thaiDist" => "justify",
                _ => "left",
            },
            MarginLeft = FirstLong(chain, "marL") ?? 0,
            Indent = FirstLong(chain, "indent") ?? 0,
        };

        foreach (var child in paragraphElement.ChildElements)
        {
            switch (child.LocalName)
            {
                case "r":
                case "fld":
                    var run = ReadRun(child, context, runDefaults, defaults, fontScale, directory);

                    if (child.LocalName == "fld")
                    {
                        run.FieldType = OpenXmlMarkup.Attribute(child, "type");

                        if (run.FieldType == "slidenum" && defaults.SlideNumber is { } number)
                        {
                            run.Text = number.ToString(CultureInfo.InvariantCulture);
                        }
                    }

                    paragraph.Runs.Add(run);
                    break;

                case "br":
                    var lineBreak = ReadRun(child, context, runDefaults, defaults, fontScale, directory);
                    lineBreak.IsLineBreak = true;
                    lineBreak.Text = string.Empty;
                    paragraph.Runs.Add(lineBreak);
                    break;
            }
        }

        var endProperties = OpenXmlMarkup.Child(paragraphElement, "endParaRPr");
        var endSize = OpenXmlMarkup.Long(endProperties, "sz") ?? FirstLong(runDefaults, "sz");
        paragraph.EndSize = (endSize is null ? defaults.Size : endSize.Value / 100d) * fontScale;

        var referenceSize = paragraph.Runs.Count > 0 ? paragraph.Runs.Max(run => run.Size) : paragraph.EndSize;

        ReadSpacing(paragraph, chain, referenceSize);
        ReadBullet(paragraph, chain, context);

        return paragraph;
    }

    private static PresentationTextRun ReadRun(
        OpenXmlElement runElement,
        OpenXmlSlideContext context,
        List<OpenXmlElement> runDefaults,
        OpenXmlTextDefaults defaults,
        double fontScale,
        OpenXmlSlideDirectory directory)
    {
        var properties = OpenXmlMarkup.Child(runElement, "rPr");
        var chain = new List<OpenXmlElement>(runDefaults.Count + 1);

        if (properties is not null)
        {
            chain.Add(properties);
        }

        chain.AddRange(runDefaults);

        var size = FirstLong(chain, "sz");
        var underline = FirstAttribute(chain, "u");
        var strike = FirstAttribute(chain, "strike");

        var run = new PresentationTextRun
        {
            Text = OpenXmlMarkup.Child(runElement, "t")?.InnerText ?? string.Empty,
            Size = (size is null ? defaults.Size : size.Value / 100d) * fontScale,
            Bold = FirstBool(chain, "b") ?? defaults.Bold,
            Italic = FirstBool(chain, "i") ?? false,
            Underline = underline is not null && underline != "none",
            Strikethrough = strike is not null && strike != "noStrike",
            Capitalization = FirstAttribute(chain, "cap") ?? "none",
            Baseline = (FirstLong(chain, "baseline") ?? 0) / 1000d,
        };

        run.Font = ResolveFont(chain, context) ?? defaults.Font ?? context.Theme.MinorFont;
        run.Color = ResolveColor(chain, context) ?? defaults.Color ?? context.Colors.ResolveScheme("tx1") ?? "000000";

        foreach (var element in chain)
        {
            var highlight = OpenXmlMarkup.Child(element, "highlight");

            if (highlight is not null)
            {
                run.Highlight = context.Colors.ResolveChild(highlight)?.Hex;
                break;
            }
        }

        var link = OpenXmlMarkup.Child(properties, "hlinkClick");

        if (link is not null)
        {
            run.Link = ReadLink(link, context.OwnerPart, directory);

            // PowerPoint draws linked text in the theme's hyperlink colour and underlines it.
            if (!HasColor(properties))
            {
                run.Color = defaults.LinkColor ?? context.Colors.ResolveScheme("hlink") ?? run.Color;
            }

            run.Underline = true;
        }

        return run;
    }

    /// <summary>
    /// Reads a link from an <c>a:hlinkClick</c> element.
    /// </summary>
    /// <param name="link">The link element.</param>
    /// <param name="owner">The part the link's relationship belongs to.</param>
    /// <param name="directory">The deck's slides.</param>
    /// <returns>The link.</returns>
    public static PresentationHyperlink ReadLink(OpenXmlElement link, OpenXmlPart owner, OpenXmlSlideDirectory directory)
    {
        var result = new PresentationHyperlink
        {
            Tooltip = OpenXmlMarkup.Attribute(link, "tooltip"),
        };

        var action = OpenXmlMarkup.Attribute(link, "action");

        if (action is not null && action.StartsWith(OpenXmlPresentationConstants.ShowJumpActionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            result.Action = action[OpenXmlPresentationConstants.ShowJumpActionPrefix.Length..] switch
            {
                "nextslide" => "next",
                "previousslide" => "previous",
                "firstslide" => "first",
                "lastslide" => "last",
                "endshow" => "end",
                var other => other,
            };

            return result;
        }

        var relationshipId = OpenXmlMarkup.RelationshipAttribute(link, "id");

        if (string.IsNullOrEmpty(relationshipId) || owner is null)
        {
            return result;
        }

        var hyperlink = owner.HyperlinkRelationships.FirstOrDefault(relationship => relationship.Id == relationshipId);

        if (hyperlink is not null)
        {
            result.Url = hyperlink.Uri?.OriginalString;

            return result;
        }

        if (owner.TryGetPartById(relationshipId, out var target) && target is SlidePart)
        {
            var found = directory?.Find(target);

            if (found is { } slide)
            {
                result.TargetSlideId = slide.SlideId;
                result.TargetSlideNumber = slide.Number;
            }
            else
            {
                result.IsBroken = true;
            }

            return result;
        }

        result.IsBroken = true;

        return result;
    }

    private static void ReadSpacing(PresentationParagraph paragraph, List<OpenXmlElement> chain, double referenceSize)
    {
        var lineSpacing = FirstChild(chain, "lnSpc");

        if (lineSpacing is not null)
        {
            var percent = OpenXmlMarkup.Long(OpenXmlMarkup.Child(lineSpacing, "spcPct"), "val");
            var points = OpenXmlMarkup.Long(OpenXmlMarkup.Child(lineSpacing, "spcPts"), "val");

            if (percent is not null)
            {
                paragraph.LineSpacing = Math.Clamp(percent.Value / 100_000d, 0.1, 10);
            }
            else if (points is not null)
            {
                paragraph.LineSpacingPoints = points.Value / 100d;
            }
        }

        paragraph.SpaceBefore = Spacing(FirstChild(chain, "spcBef"), referenceSize);
        paragraph.SpaceAfter = Spacing(FirstChild(chain, "spcAft"), referenceSize);
    }

    private static double Spacing(OpenXmlElement spacing, double referenceSize)
    {
        if (spacing is null)
        {
            return 0;
        }

        var points = OpenXmlMarkup.Long(OpenXmlMarkup.Child(spacing, "spcPts"), "val");

        if (points is not null)
        {
            return points.Value / 100d;
        }

        var percent = OpenXmlMarkup.Long(OpenXmlMarkup.Child(spacing, "spcPct"), "val");

        return percent is null ? 0 : percent.Value / 100_000d * referenceSize * 1.2;
    }

    private static void ReadBullet(PresentationParagraph paragraph, List<OpenXmlElement> chain, OpenXmlSlideContext context)
    {
        OpenXmlElement kind = null;

        foreach (var element in chain)
        {
            foreach (var child in element.ChildElements)
            {
                if (Array.IndexOf(_bulletKinds, child.LocalName) >= 0)
                {
                    kind = child;
                    break;
                }
            }

            if (kind is not null)
            {
                break;
            }
        }

        var bullet = paragraph.Bullet;

        switch (kind?.LocalName)
        {
            case "buChar":
                bullet.Kind = "char";
                bullet.Character = NormalizeBulletCharacter(OpenXmlMarkup.Attribute(kind, "char"), OpenXmlMarkup.Attribute(FirstChild(chain, "buFont"), "typeface"));
                break;

            case "buAutoNum":
                bullet.Kind = "number";
                bullet.NumberScheme = OpenXmlMarkup.Attribute(kind, "type") ?? "arabicPeriod";
                bullet.StartAt = (int)Math.Clamp(OpenXmlMarkup.Long(kind, "startAt") ?? 1, 1, 32767);
                break;

            case "buBlip":
                bullet.Kind = "char";
                bullet.Character = "▪";
                break;

            default:
                bullet.Kind = "none";
                return;
        }

        var color = FirstChild(chain, "buClr");

        if (color is not null)
        {
            bullet.Color = context.Colors.ResolveChild(color)?.Hex;
        }

        var sizePercent = OpenXmlMarkup.Long(FirstChild(chain, "buSzPct"), "val");

        if (sizePercent is not null)
        {
            bullet.SizeRatio = Math.Clamp(sizePercent.Value / 100_000d, 0.25, 4);
        }

        bullet.Font = OpenXmlMarkup.Attribute(FirstChild(chain, "buFont"), "typeface");
    }

    private static string NormalizeBulletCharacter(string character, string font)
    {
        if (string.IsNullOrEmpty(character))
        {
            return "•";
        }

        if (font is null || !(font.Contains("Wingdings", StringComparison.OrdinalIgnoreCase) || font.Contains("Symbol", StringComparison.OrdinalIgnoreCase)))
        {
            return character;
        }

        // A symbol font maps ordinary code points to pictures a browser does not have; these are the
        // bullets PowerPoint offers from them, drawn with the Unicode character that looks alike.
        return character switch
        {
            "§" => "▪",
            "Ø" => "➢",
            "ü" => "✔",
            "v" => "❖",
            "q" => "❑",
            "n" => "■",
            "l" => "●",
            "o" => "○",
            "·" => "•",
            _ => "•",
        };
    }

    private static string ResolveFont(List<OpenXmlElement> chain, OpenXmlSlideContext context)
    {
        foreach (var element in chain)
        {
            var typeface = OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(element, "latin"), "typeface");

            if (!string.IsNullOrWhiteSpace(typeface))
            {
                return context.Theme.ResolveFont(typeface);
            }
        }

        return null;
    }

    private static string ResolveColor(List<OpenXmlElement> chain, OpenXmlSlideContext context)
    {
        foreach (var element in chain)
        {
            foreach (var child in element.ChildElements)
            {
                switch (child.LocalName)
                {
                    case "solidFill":
                        return context.Colors.ResolveChild(child)?.Hex;

                    case "gradFill":
                        var firstStop = OpenXmlMarkup.Path(child, "gsLst", "gs");

                        return context.Colors.ResolveChild(firstStop)?.Hex;
                }
            }
        }

        return null;
    }

    private static bool HasColor(OpenXmlElement properties)
    {
        return OpenXmlMarkup.Child(properties, "solidFill") is not null || OpenXmlMarkup.Child(properties, "gradFill") is not null;
    }

    private static OpenXmlElement FirstChild(List<OpenXmlElement> chain, string localName)
    {
        foreach (var element in chain)
        {
            var child = OpenXmlMarkup.Child(element, localName);

            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static string FirstAttribute(List<OpenXmlElement> chain, string name)
    {
        foreach (var element in chain)
        {
            var value = OpenXmlMarkup.Attribute(element, name);

            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static long? FirstLong(List<OpenXmlElement> chain, string name)
    {
        foreach (var element in chain)
        {
            var value = OpenXmlMarkup.Long(element, name);

            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static bool? FirstBool(List<OpenXmlElement> chain, string name)
    {
        foreach (var element in chain)
        {
            var value = OpenXmlMarkup.Bool(element, name);

            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }
}
