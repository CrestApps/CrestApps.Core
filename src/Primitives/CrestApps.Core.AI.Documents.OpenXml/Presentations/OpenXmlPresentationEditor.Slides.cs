using System.Globalization;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using P = DocumentFormat.OpenXml.Presentation;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Adds, copies, removes, moves and changes slides.
/// </summary>
internal sealed partial class OpenXmlPresentationEditor
{
    private static readonly Dictionary<string, string> _layoutKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["title"] = "title",
        ["title_slide"] = "title",
        ["cover"] = "title",
        ["title_and_content"] = "obj",
        ["content"] = "obj",
        ["obj"] = "obj",
        ["bullets"] = "obj",
        ["section"] = "secHead",
        ["section_header"] = "secHead",
        ["sechead"] = "secHead",
        ["divider"] = "secHead",
        ["two_content"] = "twoObj",
        ["two_column"] = "twoObj",
        ["two_columns"] = "twoObj",
        ["twoobj"] = "twoObj",
        ["comparison"] = "twoTxTwoObj",
        ["compare"] = "twoTxTwoObj",
        ["twotxtwoobj"] = "twoTxTwoObj",
        ["title_only"] = "titleOnly",
        ["titleonly"] = "titleOnly",
        ["blank"] = "blank",
        ["empty"] = "blank",
        ["content_with_caption"] = "objTx",
        ["objtx"] = "objTx",
        ["picture_with_caption"] = "picTx",
        ["picture"] = "picTx",
        ["pictx"] = "picTx",
    };

    private void AddSlide(AddSlideEdit edit)
    {
        var directory = Directory();
        var position = Math.Clamp(edit.Position ?? directory.Slides.Count + 1, 1, directory.Slides.Count + 1);
        var neighbour = directory.Slides.Count == 0 ? null : directory.Slides[Math.Clamp(position - 2, 0, directory.Slides.Count - 1)].Part;
        // The first slide of a deck is its title slide, unless it was given body text to hold.
        var titleSlide = directory.Slides.Count == 0 && position == 1 && edit.Body is not { Count: > 0 } && edit.SecondBody is not { Count: > 0 };
        var layoutPart = FindLayout(edit.Layout, titleSlide, neighbour);

        var slidePart = CreateSlide(layoutPart, position);
        var layoutType = OpenXmlMarkup.Attribute(layoutPart.SlideLayout, "type") ?? "cust";

        FillPlaceholders(slidePart, layoutType, edit.Title, edit.Subtitle, edit.Body, edit.SecondBody, edit.FirstHeading, edit.SecondHeading);
        CopyFooters(slidePart, layoutType);

        if (edit.Background is { IsEmpty: false } background)
        {
            WriteBackground(slidePart, background);
        }

        if (edit.Hidden)
        {
            slidePart.Slide.Show = false;
        }

        if (!string.IsNullOrWhiteSpace(edit.Notes))
        {
            SetNotes(slidePart, edit.Notes);
        }

        var slideId = SlideIdOf(slidePart);
        _result.CreatedSlideIds.Add(slideId);
        MarkChanged(slidePart);

        if (edit.Elements is { Count: > 0 })
        {
            InsertElementsOn(slidePart, position, edit.Elements);
        }

        var layoutName = OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(layoutPart.SlideLayout, "cSld"), "name");
        _result.Changes.Add($"Added slide {position.ToString(CultureInfo.InvariantCulture)} (\"{edit.Title ?? "untitled"}\", layout \"{layoutName}\").");
    }

    private SlidePart CreateSlide(SlideLayoutPart layoutPart, int position)
    {
        var slidePart = _presentationPart.AddNewPart<SlidePart>(OpenXmlSchemaOrder.NextRelationshipId(_presentationPart));

        slidePart.Slide = new P.Slide(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(
                        new A.TransformGroup(
                            new A.Offset { X = 0, Y = 0 },
                            new A.Extents { Cx = 0, Cy = 0 },
                            new A.ChildOffset { X = 0, Y = 0 },
                            new A.ChildExtents { Cx = 0, Cy = 0 })))),
            new P.ColorMapOverride(new A.MasterColorMapping()));

        slidePart.AddPart(layoutPart, "rId1");
        InsertSlideId(slidePart, position);

        return slidePart;
    }

    private void InsertSlideId(SlidePart slidePart, int position)
    {
        var presentation = _presentationPart.Presentation;
        var list = presentation.SlideIdList;

        if (list is null)
        {
            list = new P.SlideIdList();
            presentation.SlideIdList = list;
        }

        var ids = list.Elements<P.SlideId>().ToList();
        var next = Math.Max(256u, ids.Count == 0 ? 256u : ids.Max(id => id.Id?.Value ?? 255u) + 1);
        var slideId = new P.SlideId { Id = next, RelationshipId = _presentationPart.GetIdOfPart(slidePart) };

        if (position - 1 < ids.Count)
        {
            list.InsertBefore(slideId, ids[position - 1]);
        }
        else
        {
            list.AppendChild(slideId);
        }
    }

    /// <summary>
    /// Finds a layout by kind or name.
    /// </summary>
    private SlideLayoutPart FindLayout(string requested, bool isFirstSlide, SlidePart neighbour)
    {
        var masters = OrderedMasters();
        var primary = neighbour?.SlideLayoutPart?.SlideMasterPart ?? (masters.Count > 0 ? masters[0] : null);
        var layouts = masters
            .OrderBy(master => ReferenceEquals(master, primary) ? 0 : 1)
            .SelectMany(master => master.SlideLayoutParts)
            .ToList();

        if (layouts.Count == 0)
        {
            throw new PresentationEditException("The deck has no slide layouts to build a slide on.");
        }

        var name = string.IsNullOrWhiteSpace(requested)
            ? isFirstSlide ? "title" : "title_and_content"
            : requested.Trim();

        var key = name.Replace(' ', '_').Replace('-', '_');

        if (_layoutKinds.TryGetValue(key, out var type))
        {
            var byType = layouts.FirstOrDefault(layout => OpenXmlMarkup.Attribute(layout.SlideLayout, "type") == type);

            if (byType is not null)
            {
                return byType;
            }
        }

        var byName = layouts.FirstOrDefault(layout => string.Equals(LayoutName(layout), name, StringComparison.OrdinalIgnoreCase))
            ?? layouts.FirstOrDefault(layout => LayoutName(layout).Contains(name, StringComparison.OrdinalIgnoreCase))
            ?? layouts.FirstOrDefault(layout => string.Equals(OpenXmlMarkup.Attribute(layout.SlideLayout, "type"), name, StringComparison.OrdinalIgnoreCase));

        if (byName is not null)
        {
            return byName;
        }

        // A deck built from a custom template may name its layouts anything; a slide asked for by kind still
        // needs a layout of that shape, so fall back on the placeholders it has.
        if (type is not null)
        {
            var byShape = layouts.FirstOrDefault(layout => MatchesShape(layout, type));

            if (byShape is not null)
            {
                return byShape;
            }
        }

        if (string.IsNullOrWhiteSpace(requested))
        {
            return layouts[0];
        }

        throw new PresentationEditException($"The deck has no layout \"{requested}\". Its layouts are: {string.Join(", ", layouts.Select(layout => $"\"{LayoutName(layout)}\" ({OpenXmlMarkup.Attribute(layout.SlideLayout, "type") ?? "custom"})"))}.");
    }

    private static bool MatchesShape(SlideLayoutPart layout, string type)
    {
        var placeholders = OpenXmlMarkup.Path(layout.SlideLayout, "cSld", "spTree")?.ChildElements
            .Select(OpenXmlPlaceholder.From)
            .Where(placeholder => placeholder is not null && !placeholder.Value.IsFooter)
            .Select(placeholder => placeholder.Value)
            .ToList() ?? [];

        var titles = placeholders.Count(placeholder => placeholder.IsTitle);
        var contents = placeholders.Count(placeholder => placeholder.IsContent);

        return type switch
        {
            "title" => placeholders.Any(placeholder => placeholder.Type == "ctrTitle") || (titles == 1 && placeholders.Any(placeholder => placeholder.Type == "subTitle")),
            "obj" => titles == 1 && contents == 1,
            "twoObj" => titles == 1 && contents == 2,
            "twoTxTwoObj" => titles == 1 && contents == 4,
            "titleOnly" => titles == 1 && contents == 0,
            "blank" => placeholders.Count == 0,
            _ => false,
        };
    }

    private static string LayoutName(SlideLayoutPart layout)
    {
        return OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(layout.SlideLayout, "cSld"), "name") ?? string.Empty;
    }

    private List<SlideMasterPart> OrderedMasters()
    {
        var masters = new List<SlideMasterPart>();

        foreach (var masterId in _presentationPart.Presentation.SlideMasterIdList?.Elements<P.SlideMasterId>() ?? [])
        {
            if (masterId.RelationshipId?.Value is { } relationshipId &&
                _presentationPart.TryGetPartById(relationshipId, out var part) &&
                part is SlideMasterPart master)
            {
                masters.Add(master);
            }
        }

        masters.AddRange(_presentationPart.SlideMasterParts.Where(master => !masters.Contains(master)));

        return masters;
    }

    /// <summary>
    /// Puts the given title, subtitle and body text into the slide's placeholders, creating each placeholder
    /// from the layout only when it has something to hold.
    /// </summary>
    private void FillPlaceholders(
        SlidePart slidePart,
        string layoutType,
        string title,
        string subtitle,
        IList<PresentationParagraphSpec> body,
        IList<PresentationParagraphSpec> secondBody,
        string firstHeading,
        string secondHeading)
    {
        var context = OpenXmlSlideContext.ForSlide(slidePart, _themes);
        var layoutPlaceholders = context.LayoutPlaceholders()
            .Where(entry => !entry.Placeholder.IsFooter && entry.Shape.LocalName == "sp")
            .ToList();

        var titlePlaceholder = layoutPlaceholders.FirstOrDefault(entry => entry.Placeholder.IsTitle);
        var subtitlePlaceholder = layoutPlaceholders.FirstOrDefault(entry => entry.Placeholder.Type == "subTitle");
        var content = layoutPlaceholders
            .Where(entry => entry.Placeholder.IsContent && entry.Placeholder.Type != "pic")
            .Select(entry => (entry.Placeholder, entry.Shape, Bounds: TransformBounds(context.Transform(entry.Shape) ?? OpenXmlSlideContext.OwnTransform(context.FindInherited(entry.Placeholder).Master))))
            .ToList();

        (OpenXmlPlaceholder Placeholder, OpenXmlElement Shape)? firstHeadingPlaceholder = null;
        (OpenXmlPlaceholder Placeholder, OpenXmlElement Shape)? secondHeadingPlaceholder = null;
        var bodies = new List<(OpenXmlPlaceholder Placeholder, OpenXmlElement Shape)>();

        if ((content.Count >= 4 && layoutType == "twoTxTwoObj") || content.Count == 4)
        {
            // Two columns, each a short heading above its content.
            var columns = content.OrderBy(entry => entry.Bounds.X).ToList();
            var left = columns.Take(2).OrderBy(entry => entry.Bounds.Y).ToList();
            var right = columns.Skip(2).Take(2).OrderBy(entry => entry.Bounds.Y).ToList();

            firstHeadingPlaceholder = (left[0].Placeholder, left[0].Shape);
            secondHeadingPlaceholder = (right[0].Placeholder, right[0].Shape);
            bodies.Add((left[1].Placeholder, left[1].Shape));
            bodies.Add((right[1].Placeholder, right[1].Shape));
        }
        else if (layoutType is "objTx" or "picTx" && content.Count >= 1)
        {
            // A caption layout: the large placeholder takes the content and the narrow one the caption.
            var ordered = content.OrderByDescending(entry => entry.Bounds.Area()).ToList();

            if (layoutType == "picTx")
            {
                bodies.Add((ordered[^1].Placeholder, ordered[^1].Shape));
            }
            else
            {
                bodies.AddRange(ordered.Select(entry => (entry.Placeholder, entry.Shape)));
            }
        }
        else
        {
            bodies.AddRange(content.OrderBy(entry => entry.Bounds.X).ThenBy(entry => entry.Bounds.Y).Select(entry => (entry.Placeholder, entry.Shape)));
        }

        var style = HouseStyle;

        if (!string.IsNullOrEmpty(title) && titlePlaceholder.Shape is not null)
        {
            AddPlaceholderText(slidePart, titlePlaceholder.Shape, [PresentationParagraphSpec.FromText(title)], style.Title);
        }
        else if (!string.IsNullOrEmpty(title))
        {
            _result.Warnings.Add($"The layout of the new slide has no title placeholder, so the title \"{title}\" was not placed. Choose a layout with a title, or add a text box.");
        }

        if (!string.IsNullOrEmpty(subtitle))
        {
            if (subtitlePlaceholder.Shape is not null)
            {
                AddPlaceholderText(slidePart, subtitlePlaceholder.Shape, SplitLines(subtitle), style.Subtitle);
            }
            else if (bodies.Count > 0 && body is null)
            {
                AddPlaceholderText(slidePart, bodies[0].Shape, SplitLines(subtitle), style.Subtitle);
                bodies.RemoveAt(0);
            }
        }

        if (!string.IsNullOrEmpty(firstHeading) && firstHeadingPlaceholder is { } firstHeadingValue)
        {
            AddPlaceholderText(slidePart, firstHeadingValue.Shape, [PresentationParagraphSpec.FromText(firstHeading)], style.Body);
        }

        if (!string.IsNullOrEmpty(secondHeading) && secondHeadingPlaceholder is { } secondHeadingValue)
        {
            AddPlaceholderText(slidePart, secondHeadingValue.Shape, [PresentationParagraphSpec.FromText(secondHeading)], style.Body);
        }

        if (body is { Count: > 0 })
        {
            if (bodies.Count > 0)
            {
                AddPlaceholderText(slidePart, bodies[0].Shape, body, style.Body);
            }
            else
            {
                AddTextBox(slidePart, body, ContentArea(slidePart));
            }
        }

        if (secondBody is { Count: > 0 })
        {
            if (bodies.Count > 1)
            {
                AddPlaceholderText(slidePart, bodies[1].Shape, secondBody, style.Body);
            }
            else
            {
                var area = ContentArea(slidePart);
                AddTextBox(slidePart, secondBody, Placement("right_half", area));
            }
        }
    }

    private static List<PresentationParagraphSpec> SplitLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => PresentationParagraphSpec.FromText(line))
            .ToList();
    }

    /// <summary>
    /// Adds a placeholder to a slide from its layout's placeholder and writes text into it.
    /// </summary>
    private P.Shape AddPlaceholderText(SlidePart slidePart, OpenXmlElement layoutShape, IList<PresentationParagraphSpec> paragraphs, PresentationTextStyle style)
    {
        var shape = CreatePlaceholderShape(slidePart, layoutShape);
        var textBody = shape.TextBody;

        WriteParagraphs(slidePart, textBody, paragraphs, style, explicitBullets: false);
        TrackText(slidePart, shape);

        return shape;
    }

    private static P.Shape CreatePlaceholderShape(SlidePart slidePart, OpenXmlElement layoutShape)
    {
        var placeholder = OpenXmlPlaceholder.From(layoutShape) ?? new OpenXmlPlaceholder("body", null);
        var placeholderElement = new P.PlaceholderShape();

        if (placeholder.Type != "obj")
        {
            placeholderElement.Type = new P.PlaceholderValues(placeholder.Type);
        }

        if (placeholder.Index is { } index)
        {
            placeholderElement.Index = index;
        }

        var size = OpenXmlMarkup.Attribute(OpenXmlPlaceholder.FindElement(layoutShape), "sz");

        if (size is not null)
        {
            placeholderElement.Size = new P.PlaceholderSizeValues(size);
        }

        var id = NextShapeId(slidePart);
        var name = ElementName(layoutShape);

        var shape = new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = string.IsNullOrEmpty(name) ? "Placeholder " + id.ToString(CultureInfo.InvariantCulture) : name },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties(placeholderElement)),
            new P.ShapeProperties(),
            new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph(new A.EndParagraphRunProperties { Language = "en-US" })));

        ShapeTree(slidePart).AppendChild(shape);

        return shape;
    }

    private P.Shape AddTextBox(SlidePart slidePart, IList<PresentationParagraphSpec> paragraphs, PresentationBounds bounds)
    {
        var spec = new PresentationElementSpec
        {
            Kind = PresentationElementSpecKind.Text,
            Paragraphs = paragraphs,
            Bounds = new PresentationBoundsSpec
            {
                X = new PresentationLength(bounds.X, PresentationLengthUnit.Emus),
                Y = new PresentationLength(bounds.Y, PresentationLengthUnit.Emus),
                Width = new PresentationLength(bounds.Width, PresentationLengthUnit.Emus),
            },
        };

        return (P.Shape)InsertElement(slidePart, spec, bounds);
    }

    /// <summary>
    /// Gives a new slide the footer, date and slide number most of the deck's other slides show.
    /// </summary>
    private void CopyFooters(SlidePart slidePart, string layoutType)
    {
        if (layoutType == "title")
        {
            return;
        }

        var others = Directory().Slides
            .Where(entry => !ReferenceEquals(entry.Part, slidePart) && OpenXmlMarkup.Attribute(entry.Part.SlideLayoutPart?.SlideLayout, "type") != "title")
            .Select(entry => entry.Part)
            .ToList();

        if (others.Count == 0)
        {
            return;
        }

        foreach (var type in new[] { "ftr", "sldNum", "dt" })
        {
            var holders = others
                .Select(other => Elements(ShapeTree(other)).FirstOrDefault(element => OpenXmlPlaceholder.From(element)?.Type == type))
                .Where(element => element is not null)
                .ToList();

            if (holders.Count * 2 <= others.Count)
            {
                continue;
            }

            AddFooterPlaceholder(slidePart, type, type == "ftr" ? TextOf(holders[0]) : null, type == "dt" ? TextOf(holders[0]) : null);
        }
    }

    private void DuplicateSlide(DuplicateSlideEdit edit)
    {
        var source = GetSlide(edit.Slide);
        var directory = Directory();
        var position = Math.Clamp(edit.Position ?? edit.Slide + 1, 1, directory.Slides.Count + 1);
        var copy = _presentationPart.AddNewPart<SlidePart>(OpenXmlSchemaOrder.NextRelationshipId(_presentationPart));

        // Copied from the markup in memory rather than the part's stream: a slide made or changed earlier in
        // this batch has not been written to its stream yet.
        copy.Slide = (P.Slide)source.Slide.CloneNode(true);

        CopyRelationships(source, copy);

        if (source.NotesSlidePart?.NotesSlide is { } notes)
        {
            SetNotes(copy, OpenXmlPresentationReader.PlainText(Elements(OpenXmlMarkup.Path(notes, "cSld", "spTree"))
                .FirstOrDefault(element => OpenXmlPlaceholder.From(element)?.Type == "body")?.GetFirstChild<P.TextBody>()));
        }

        InsertSlideId(copy, position);
        _result.CreatedSlideIds.Add(SlideIdOf(copy));
        MarkChanged(copy);
        _result.Changes.Add($"Copied slide {edit.Slide.ToString(CultureInfo.InvariantCulture)} to position {position.ToString(CultureInfo.InvariantCulture)}.");
    }

    /// <summary>
    /// Gives a copied slide the relationships its markup refers to: the same layout and pictures, and its
    /// own copies of charts so editing one does not change the other.
    /// </summary>
    private static void CopyRelationships(SlidePart source, SlidePart target)
    {
        foreach (var pair in source.Parts)
        {
            switch (pair.OpenXmlPart)
            {
                case NotesSlidePart:
                    continue;
                case ChartPart chart:
                    ClonePart(chart, target, pair.RelationshipId);
                    break;
                case DiagramDataPart or DiagramLayoutDefinitionPart or DiagramStylePart or DiagramColorsPart or DiagramPersistLayoutPart or EmbeddedObjectPart or EmbeddedPackagePart:
                    CloneLeafPart(pair.OpenXmlPart, target, pair.RelationshipId);
                    break;
                default:
                    target.AddPart(pair.OpenXmlPart, pair.RelationshipId);
                    break;
            }
        }

        foreach (var relationship in source.HyperlinkRelationships)
        {
            target.AddHyperlinkRelationship(relationship.Uri, relationship.IsExternal, relationship.Id);
        }

        foreach (var relationship in source.ExternalRelationships)
        {
            target.AddExternalRelationship(relationship.RelationshipType, relationship.Uri, relationship.Id);
        }

        foreach (var relationship in source.DataPartReferenceRelationships)
        {
            switch (relationship)
            {
                case VideoReferenceRelationship video:
                    target.AddVideoReferenceRelationship((MediaDataPart)video.DataPart, video.Id);
                    break;
                case AudioReferenceRelationship audio:
                    target.AddAudioReferenceRelationship((MediaDataPart)audio.DataPart, audio.Id);
                    break;
                case MediaReferenceRelationship media:
                    target.AddMediaReferenceRelationship((MediaDataPart)media.DataPart, media.Id);
                    break;
            }
        }
    }

    /// <summary>
    /// Copies a chart part, with its embedded workbook and styles, under a new owner.
    /// </summary>
    private static ChartPart ClonePart(ChartPart source, OpenXmlPartContainer owner, string relationshipId)
    {
        var copy = owner.AddNewPart<ChartPart>(relationshipId);

        // As with a slide, the chart may exist only in memory so far.
        copy.ChartSpace = (C.ChartSpace)source.ChartSpace.CloneNode(true);

        foreach (var pair in source.Parts)
        {
            var child = pair.OpenXmlPart;
            var childCopy = child switch
            {
                EmbeddedPackagePart package => (OpenXmlPart)copy.AddNewPart<EmbeddedPackagePart>(package.ContentType, pair.RelationshipId),
                ChartStylePart => copy.AddNewPart<ChartStylePart>(pair.RelationshipId),
                ChartColorStylePart => copy.AddNewPart<ChartColorStylePart>(pair.RelationshipId),
                ImagePart image => copy.AddImagePart(image.ContentType, pair.RelationshipId),
                ChartDrawingPart => copy.AddNewPart<ChartDrawingPart>(pair.RelationshipId),
                ThemeOverridePart => copy.AddNewPart<ThemeOverridePart>(pair.RelationshipId),
                _ => null,
            };

            if (childCopy is null)
            {
                continue;
            }

            using var childStream = child.GetStream(FileMode.Open, FileAccess.Read);
            childCopy.FeedData(childStream);
        }

        foreach (var relationship in source.ExternalRelationships)
        {
            copy.AddExternalRelationship(relationship.RelationshipType, relationship.Uri, relationship.Id);
        }

        return copy;
    }

    /// <summary>
    /// Copies a SmartArt or embedded-object part under a new owner, so the two slides do not share one
    /// drawing that editing either would change for both.
    /// </summary>
    private static void CloneLeafPart(OpenXmlPart source, SlidePart owner, string relationshipId)
    {
        OpenXmlPart copy = source switch
        {
            DiagramDataPart => owner.AddNewPart<DiagramDataPart>(relationshipId),
            DiagramLayoutDefinitionPart => owner.AddNewPart<DiagramLayoutDefinitionPart>(relationshipId),
            DiagramStylePart => owner.AddNewPart<DiagramStylePart>(relationshipId),
            DiagramColorsPart => owner.AddNewPart<DiagramColorsPart>(relationshipId),
            DiagramPersistLayoutPart => owner.AddNewPart<DiagramPersistLayoutPart>(relationshipId),
            EmbeddedObjectPart => owner.AddNewPart<EmbeddedObjectPart>(source.ContentType, relationshipId),
            EmbeddedPackagePart => owner.AddNewPart<EmbeddedPackagePart>(source.ContentType, relationshipId),
            _ => null,
        };

        if (copy is null)
        {
            owner.AddPart(source, relationshipId);

            return;
        }

        using (var stream = source.GetStream(FileMode.Open, FileAccess.Read))
        {
            copy.FeedData(stream);
        }

        foreach (var child in source.Parts)
        {
            copy.AddPart(child.OpenXmlPart, child.RelationshipId);
        }
    }

    private void DeleteSlides(DeleteSlidesEdit edit)
    {
        if (edit.Slides is not { Count: > 0 })
        {
            throw new PresentationEditException("Name at least one slide to delete.");
        }

        var directory = Directory();
        var targets = edit.Slides.Distinct().OrderByDescending(number => number).ToList();

        foreach (var number in targets)
        {
            if (number < 1 || number > directory.Slides.Count)
            {
                throw new PresentationEditException($"There is no slide {number.ToString(CultureInfo.InvariantCulture)}: the deck has {directory.Slides.Count.ToString(CultureInfo.InvariantCulture)} slide(s).");
            }
        }

        var removedIds = new HashSet<uint>();

        foreach (var number in targets)
        {
            var (slideId, part) = directory.Slides[number - 1];
            var idElement = _presentationPart.Presentation.SlideIdList.Elements<P.SlideId>().First(element => element.Id?.Value == slideId);

            idElement.Remove();
            removedIds.Add(slideId);

            // Links on other slides that jumped here would otherwise point at a part that no longer exists.
            foreach (var (_, other) in directory.Slides)
            {
                if (ReferenceEquals(other, part))
                {
                    continue;
                }

                var relationship = other.Parts.FirstOrDefault(pair => ReferenceEquals(pair.OpenXmlPart, part));

                if (relationship.OpenXmlPart is not null)
                {
                    RemoveLinksTo(other, relationship.RelationshipId);
                    other.DeletePart(relationship.RelationshipId);
                }
            }

            _presentationPart.DeletePart(part);
        }

        RemoveFromSections(removedIds);

        _result.Changes.Add(targets.Count == 1
            ? $"Deleted slide {targets[0].ToString(CultureInfo.InvariantCulture)}."
            : $"Deleted slides {string.Join(", ", targets.OrderBy(number => number).Select(number => number.ToString(CultureInfo.InvariantCulture)))}.");
    }

    private static void RemoveLinksTo(SlidePart slidePart, string relationshipId)
    {
        foreach (var link in slidePart.Slide.Descendants<A.HyperlinkOnClick>().ToList())
        {
            if (link.Id?.Value == relationshipId)
            {
                link.Remove();
            }
        }
    }

    private void MoveSlide(MoveSlideEdit edit)
    {
        var slidePart = GetSlide(edit.Slide);
        var list = _presentationPart.Presentation.SlideIdList;
        var ids = list.Elements<P.SlideId>().ToList();
        var position = Math.Clamp(edit.Position, 1, ids.Count);
        var moving = ids[edit.Slide - 1];

        if (position == edit.Slide)
        {
            _result.Changes.Add($"Slide {edit.Slide.ToString(CultureInfo.InvariantCulture)} is already at position {position.ToString(CultureInfo.InvariantCulture)}.");

            return;
        }

        moving.Remove();

        var remaining = list.Elements<P.SlideId>().ToList();

        if (position - 1 < remaining.Count)
        {
            list.InsertBefore(moving, remaining[position - 1]);
        }
        else
        {
            list.AppendChild(moving);
        }

        MarkChanged(slidePart);
        _result.Changes.Add($"Moved slide {edit.Slide.ToString(CultureInfo.InvariantCulture)} to position {position.ToString(CultureInfo.InvariantCulture)}.");
    }

    private void UpdateSlide(UpdateSlideEdit edit)
    {
        var slidePart = GetSlide(edit.Slide);
        var changes = new List<string>();
        var style = HouseStyle;

        if (edit.Title is not null)
        {
            SetRoleText(slidePart, "title", edit.Title.Length == 0 ? [] : [PresentationParagraphSpec.FromText(edit.Title)], style.Title, edit.Slide);
            changes.Add(edit.Title.Length == 0 ? "cleared the title" : "set the title");
        }

        if (edit.Subtitle is not null)
        {
            SetRoleText(slidePart, "subtitle", edit.Subtitle.Length == 0 ? [] : SplitLines(edit.Subtitle), style.Subtitle, edit.Slide);
            changes.Add("set the subtitle");
        }

        if (edit.Body is not null)
        {
            SetRoleText(slidePart, "body", edit.Body, style.Body, edit.Slide);
            changes.Add("replaced the body text");
        }

        if (edit.SecondBody is not null)
        {
            SetRoleText(slidePart, "body2", edit.SecondBody, style.Body, edit.Slide);
            changes.Add("replaced the second column");
        }

        if (edit.Notes is not null)
        {
            SetNotes(slidePart, edit.Notes);
            changes.Add(edit.Notes.Length == 0 ? "cleared the speaker notes" : "set the speaker notes");
        }

        if (edit.Hidden is { } hidden)
        {
            if (hidden)
            {
                slidePart.Slide.Show = false;
            }
            else
            {
                slidePart.Slide.Show = null;
            }

            changes.Add(hidden ? "hid the slide" : "unhid the slide");
        }

        if (edit.Transition is not null)
        {
            SetTransition(slidePart, edit.Transition);
            changes.Add($"set the transition to {edit.Transition}");
        }

        MarkChanged(slidePart);
        _result.Changes.Add(changes.Count == 0
            ? $"Slide {edit.Slide.ToString(CultureInfo.InvariantCulture)} was left unchanged; nothing to update was given."
            : $"On slide {edit.Slide.ToString(CultureInfo.InvariantCulture)}: {string.Join(", ", changes)}.");
    }

    /// <summary>
    /// Writes text into the placeholder with a role, adding the placeholder from the layout when the slide
    /// does not have it yet.
    /// </summary>
    private void SetRoleText(SlidePart slidePart, string role, IList<PresentationParagraphSpec> paragraphs, PresentationTextStyle style, int slideNumber)
    {
        var shape = TryFindElement(slidePart, role);

        if (shape is null)
        {
            if (paragraphs.Count == 0)
            {
                return;
            }

            var context = OpenXmlSlideContext.ForSlide(slidePart, _themes);
            var candidates = context.LayoutPlaceholders().Where(entry => entry.Shape.LocalName == "sp").ToList();
            var present = Elements(ShapeTree(slidePart)).Select(OpenXmlPlaceholder.From).Where(placeholder => placeholder is not null).Select(placeholder => placeholder.Value).ToList();
            var layoutShape = role switch
            {
                "title" => candidates.FirstOrDefault(entry => entry.Placeholder.IsTitle).Shape,
                "subtitle" => candidates.FirstOrDefault(entry => entry.Placeholder.Type == "subTitle").Shape,
                "body2" => candidates.Where(entry => entry.Placeholder.IsContent && entry.Placeholder.Type != "pic").Skip(1).FirstOrDefault(entry => !present.Contains(entry.Placeholder)).Shape,
                _ => candidates.FirstOrDefault(entry => entry.Placeholder.IsContent && entry.Placeholder.Type != "pic" && !present.Contains(entry.Placeholder)).Shape,
            };

            if (layoutShape is null)
            {
                if (role == "title")
                {
                    throw new PresentationEditException($"Slide {slideNumber.ToString(CultureInfo.InvariantCulture)} is built on a layout with no title. Apply a layout with a title first, or add a text box with insert_slide_element.");
                }

                AddTextBox(slidePart, paragraphs, role == "body2" ? Placement("right_half", ContentArea(slidePart)) : ContentArea(slidePart));

                return;
            }

            AddPlaceholderText(slidePart, layoutShape, paragraphs, style);

            return;
        }

        var textBody = shape.GetFirstChild<P.TextBody>();

        if (textBody is null)
        {
            textBody = new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph());
            shape.AppendChild(textBody);
        }

        if (paragraphs.Count == 0)
        {
            foreach (var paragraph in textBody.Elements<A.Paragraph>().ToList())
            {
                paragraph.Remove();
            }

            textBody.AppendChild(new A.Paragraph(new A.EndParagraphRunProperties { Language = "en-US" }));
            TrackText(slidePart, shape);

            return;
        }

        WriteParagraphs(slidePart, textBody, paragraphs, style, explicitBullets: OpenXmlPlaceholder.From(shape) is null);
        TrackText(slidePart, shape);
    }

    private static void SetTransition(SlidePart slidePart, string transition)
    {
        var slide = slidePart.Slide;

        foreach (var existing in slide.ChildElements.Where(child => child.LocalName == "transition" || (child.LocalName == "AlternateContent" && OpenXmlMarkup.Descendant(child, "transition") is not null)).ToList())
        {
            existing.Remove();
        }

        var name = transition.Trim().ToLowerInvariant();

        if (name is "none" or "")
        {
            return;
        }

        OpenXmlElement effect = name switch
        {
            "fade" => new P.FadeTransition(),
            "push" => new P.PushTransition(),
            "wipe" => new P.WipeTransition(),
            "split" => new P.SplitTransition(),
            "cover" => new P.CoverTransition(),
            "cut" => new P.CutTransition(),
            "zoom" => new P.ZoomTransition(),
            _ => throw new PresentationEditException($"\"{transition}\" is not a transition. Use none, fade, push, wipe, split, cover, cut or zoom."),
        };

        OpenXmlSchemaOrder.Set(slide, new P.Transition(effect) { Speed = P.TransitionSpeedValues.Medium }, OpenXmlSchemaOrder.Slide);
    }

    private void ApplyLayout(ApplyLayoutEdit edit)
    {
        if (edit.Slides is not { Count: > 0 })
        {
            throw new PresentationEditException("Name at least one slide to change the layout of.");
        }

        foreach (var number in edit.Slides.Distinct())
        {
            var slidePart = GetSlide(number);
            var layoutPart = FindLayout(edit.Layout, false, slidePart);
            var current = slidePart.SlideLayoutPart;

            if (ReferenceEquals(current, layoutPart))
            {
                continue;
            }

            var relationshipId = slidePart.GetIdOfPart(current);
            slidePart.DeletePart(relationshipId);
            slidePart.AddPart(layoutPart, relationshipId);

            RemapPlaceholders(slidePart, number);
            MarkChanged(slidePart);
        }

        _result.Changes.Add($"Applied the \"{edit.Layout}\" layout to slide(s) {string.Join(", ", edit.Slides.Distinct().Select(number => number.ToString(CultureInfo.InvariantCulture)))}.");
    }

    /// <summary>
    /// Moves a slide's placeholder content into the placeholders of its new layout.
    /// </summary>
    /// <remarks>
    /// A placeholder finds its position through its index and type, so content whose placeholder the new
    /// layout lacks would float to wherever the master puts a body. Each placeholder is matched to one of the
    /// new layout's by role — title to title, body to body in order — and takes its position from there; one
    /// with no match is turned into an ordinary text box where it stood.
    /// </remarks>
    private void RemapPlaceholders(SlidePart slidePart, int slideNumber)
    {
        var context = OpenXmlSlideContext.ForSlide(slidePart, _themes);
        var targets = context.LayoutPlaceholders().Where(entry => !entry.Placeholder.IsFooter).ToList();
        var used = new HashSet<OpenXmlElement>();

        foreach (var shape in Elements(ShapeTree(slidePart)).Where(element => element.Parent is P.ShapeTree).ToList())
        {
            if (OpenXmlPlaceholder.From(shape) is not { } placeholder || placeholder.IsFooter)
            {
                continue;
            }

            var match = targets.FirstOrDefault(entry => !used.Contains(entry.Shape) && entry.Placeholder.IsTitle == placeholder.IsTitle &&
                (placeholder.IsTitle || entry.Placeholder.Type == placeholder.Type || (entry.Placeholder.IsContent && placeholder.IsContent) ||
                (entry.Placeholder.Type == "subTitle" && placeholder.IsContent) || (entry.Placeholder.IsContent && placeholder.Type == "subTitle")));

            var element = OpenXmlPlaceholder.FindElement(shape);

            if (match.Shape is not null)
            {
                used.Add(match.Shape);

                // The position comes from the new layout, so any position the slide carried is dropped.
                OpenXmlMarkup.Path(shape, "spPr", "xfrm")?.Remove();
                OpenXmlSchemaOrder.SetAttribute(element, "type", match.Placeholder.Type == "obj" ? null : match.Placeholder.Type);
                OpenXmlSchemaOrder.SetAttribute(element, "idx", match.Placeholder.Index?.ToString(CultureInfo.InvariantCulture));
                TrackText(slidePart, shape);

                continue;
            }

            if (!string.IsNullOrWhiteSpace(TextOf(shape)))
            {
                var bounds = Resolve(slidePart, shape).Bounds;
                element.Remove();

                if (OpenXmlMarkup.Path(shape, "spPr") is OpenXmlCompositeElement properties)
                {
                    OpenXmlSchemaOrder.Set(properties, Transform(bounds), OpenXmlSchemaOrder.ShapeProperties);
                }

                _result.Warnings.Add($"The new layout of slide {slideNumber.ToString(CultureInfo.InvariantCulture)} has no place for \"{ElementName(shape)}\", so it was kept as a text box where it was.");
            }
            else
            {
                shape.Remove();
            }
        }
    }

    private void SetBackground(SetBackgroundEdit edit)
    {
        if (edit.Background is null || edit.Background.IsEmpty)
        {
            throw new PresentationEditException("Give a background colour, gradient or picture, or ask for the background to be reset.");
        }

        if (edit.ApplyToMaster)
        {
            foreach (var master in OrderedMasters())
            {
                WriteBackground(master, edit.Background);
            }

            // Slides and layouts that set their own background would hide the master's.
            foreach (var (_, slidePart) in Directory().Slides)
            {
                if (slidePart.Slide.CommonSlideData?.Background is { } own)
                {
                    own.Remove();
                    MarkChanged(slidePart);
                }
            }

            foreach (var layout in OrderedMasters().SelectMany(master => master.SlideLayoutParts))
            {
                layout.SlideLayout.CommonSlideData?.Background?.Remove();
            }

            foreach (var (_, slidePart) in Directory().Slides)
            {
                MarkChanged(slidePart);
            }

            _result.Changes.Add("Set the background of every slide on the slide master.");

            return;
        }

        var targets = edit.Slides is { Count: > 0 }
            ? edit.Slides.Distinct().ToList()
            : Enumerable.Range(1, Directory().Slides.Count).ToList();

        foreach (var number in targets)
        {
            var slidePart = GetSlide(number);
            WriteBackground(slidePart, edit.Background);
            MarkChanged(slidePart);
        }

        _result.Changes.Add(edit.Background.Reset
            ? $"Reset the background of {targets.Count.ToString(CultureInfo.InvariantCulture)} slide(s) to the layout's."
            : $"Set the background of {targets.Count.ToString(CultureInfo.InvariantCulture)} slide(s).");
    }

    /// <summary>
    /// Writes a background onto a slide, layout or master.
    /// </summary>
    private static void WriteBackground(OpenXmlPart part, PresentationBackgroundSpec spec)
    {
        var commonSlideData = part switch
        {
            SlidePart slide => slide.Slide.CommonSlideData,
            SlideLayoutPart layout => layout.SlideLayout.CommonSlideData,
            SlideMasterPart master => master.SlideMaster.CommonSlideData,
            _ => null,
        };

        if (commonSlideData is null)
        {
            return;
        }

        commonSlideData.Background?.Remove();

        if (spec.Reset)
        {
            if (part is SlideMasterPart)
            {
                commonSlideData.PrependChild(new P.Background(new P.BackgroundStyleReference(new A.SchemeColor { Val = A.SchemeColorValues.Background1 }) { Index = 1001U }));
            }

            return;
        }

        OpenXmlElement fill;

        if (spec.Image is { Data.Length: > 0 } image)
        {
            var relationshipId = AddImage(part, image);
            var blip = new A.Blip { Embed = relationshipId };

            if (spec.ImageTransparency is { } transparency && transparency > 0)
            {
                blip.Append(new A.AlphaModulationFixed { Amount = (int)Math.Round((1 - (Math.Clamp(transparency, 0, 100) / 100)) * 100_000) });
            }

            fill = new A.BlipFill(blip, new A.Stretch(new A.FillRectangle())) { RotateWithShape = true, Dpi = 0U };
        }
        else if (spec.GradientColors is { Count: > 0 })
        {
            fill = OpenXmlDrawingWriter.Gradient(spec.GradientColors, spec.GradientAngle, 1, "background");
        }
        else
        {
            fill = OpenXmlDrawingWriter.Fill(OpenXmlDrawingWriter.ParseColor(spec.Color, "background"));
        }

        commonSlideData.PrependChild(new P.Background(new P.BackgroundProperties(fill, new A.EffectList())));
    }

    /// <summary>
    /// Stores a picture as a part of a slide, layout or master and returns the relationship to it.
    /// </summary>
    private static string AddImage(OpenXmlPartContainer owner, PresentationImageData image)
    {
        var contentType = image.ContentType;

        if (!PresentationImageInfo.TryRead(image.Data, out var sniffed, out _, out _))
        {
            throw new PresentationEditException($"The picture \"{image.FileName ?? "image"}\" is not a PNG, JPEG, GIF or BMP image, so it cannot be placed on a slide.");
        }

        contentType = sniffed ?? contentType;

        var relationshipId = OpenXmlSchemaOrder.NextRelationshipId(owner);
        var imagePart = owner switch
        {
            SlidePart slide => slide.AddImagePart(contentType, relationshipId),
            SlideLayoutPart layout => layout.AddImagePart(contentType, relationshipId),
            SlideMasterPart master => master.AddImagePart(contentType, relationshipId),
            _ => throw new PresentationEditException("A picture can only be placed on a slide, layout or master."),
        };

        using var stream = new MemoryStream(image.Data, writable: false);
        imagePart.FeedData(stream);

        return relationshipId;
    }

    /// <summary>
    /// Writes a slide's speaker notes, creating its notes page, and the deck's notes master, when needed.
    /// </summary>
    private void SetNotes(SlidePart slidePart, string notes)
    {
        var notesPart = slidePart.NotesSlidePart;

        if (string.IsNullOrEmpty(notes) && notesPart is null)
        {
            return;
        }

        if (notesPart is null)
        {
            var notesMaster = EnsureNotesMaster();
            notesPart = slidePart.AddNewPart<NotesSlidePart>(OpenXmlSchemaOrder.NextRelationshipId(slidePart));

            PresentationPackageFactory.Feed(notesPart,
                "<p:notes " + PresentationTemplateXml.Namespaces + "><p:cSld><p:spTree>" +
                "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>" +
                "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Slide Image Placeholder 1\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldImg\"/></p:nvPr></p:nvSpPr><p:spPr/></p:sp>" +
                "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Notes Placeholder 2\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"body\" idx=\"1\"/></p:nvPr></p:nvSpPr><p:spPr/>" +
                "<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang=\"en-US\"/></a:p></p:txBody></p:sp>" +
                "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:notes>");

            notesPart.AddPart(notesMaster, "rId1");
            notesPart.AddPart(slidePart, "rId2");
        }

        var body = notesPart.NotesSlide.CommonSlideData.ShapeTree.Elements<P.Shape>()
            .FirstOrDefault(shape => OpenXmlPlaceholder.From(shape) is { Type: "body" })?.TextBody;

        if (body is null)
        {
            return;
        }

        foreach (var paragraph in body.Elements<A.Paragraph>().ToList())
        {
            paragraph.Remove();
        }

        foreach (var line in (notes ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            body.AppendChild(line.Length == 0
                ? new A.Paragraph(new A.EndParagraphRunProperties { Language = "en-US" })
                : new A.Paragraph(new A.Run(new A.RunProperties { Language = "en-US", Dirty = false }, new A.Text(line))));
        }

        notesPart.NotesSlide.Save();
    }

    private NotesMasterPart EnsureNotesMaster()
    {
        if (_presentationPart.NotesMasterPart is { } existing)
        {
            return existing;
        }

        var relationshipId = OpenXmlSchemaOrder.NextRelationshipId(_presentationPart);
        var notesMaster = _presentationPart.AddNewPart<NotesMasterPart>(relationshipId);
        PresentationPackageFactory.Feed(notesMaster, PresentationTemplateXml.NotesMaster());

        var theme = notesMaster.AddNewPart<ThemePart>("rId1");
        var colors = new Dictionary<string, string>(PresentationThemePresets.All[0].Colors, StringComparer.OrdinalIgnoreCase);
        PresentationPackageFactory.Feed(theme, PresentationTemplateXml.Theme("Notes Theme", colors, "Calibri Light", "Calibri"));

        _presentationPart.Presentation.NotesMasterIdList = new P.NotesMasterIdList(new P.NotesMasterId { Id = relationshipId });

        return notesMaster;
    }
}
