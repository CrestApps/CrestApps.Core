using System.Text;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using P = DocumentFormat.OpenXml.Presentation;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Reads a presentation package into the format-neutral <see cref="PresentationModel"/>.
/// </summary>
internal sealed class OpenXmlPresentationReader
{
    private readonly PresentationDocument _document;
    private readonly PresentationReadOptions _options;
    private readonly OpenXmlThemeCache _themes;
    private readonly OpenXmlSlideDirectory _directory;

    private OpenXmlPresentationReader(PresentationDocument document, PresentationReadOptions options)
    {
        _document = document;
        _options = options ?? new PresentationReadOptions();
        _themes = new OpenXmlThemeCache(document.PresentationPart);
        _directory = new OpenXmlSlideDirectory(document.PresentationPart);
    }

    /// <summary>
    /// Reads a deck.
    /// </summary>
    /// <param name="document">The open package.</param>
    /// <param name="options">How much to read.</param>
    /// <returns>The model.</returns>
    public static PresentationModel Read(PresentationDocument document, PresentationReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new OpenXmlPresentationReader(document, options).ReadModel();
    }

    /// <summary>
    /// Reads one element on a slide, for the editor, which needs an element's resolved text and position.
    /// </summary>
    /// <param name="document">The open package.</param>
    /// <param name="slidePart">The slide the element is on.</param>
    /// <param name="element">The element's markup.</param>
    /// <returns>The resolved element, or <see langword="null"/> when it is not something the reader models.</returns>
    public static PresentationElement ReadElement(PresentationDocument document, SlidePart slidePart, OpenXmlElement element)
    {
        var reader = new OpenXmlPresentationReader(document, PresentationReadOptions.TextOnly);
        var context = OpenXmlSlideContext.ForSlide(slidePart, reader._themes);
        var number = reader._directory.Find(slidePart)?.Number;
        var z = 0;

        return reader.ReadElementCore(element, context, OpenXmlGroupTransform.Identity, null, number, includeImages: false, ref z);
    }

    private PresentationModel ReadModel()
    {
        var presentationPart = _document.PresentationPart
            ?? throw new InvalidOperationException("The package does not contain a presentation.");

        var presentation = presentationPart.Presentation;
        var size = presentation?.SlideSize;

        var model = new PresentationModel
        {
            SlideWidth = size?.Cx?.Value ?? PresentationUnits.WideSlideWidth,
            SlideHeight = size?.Cy?.Value ?? PresentationUnits.WideSlideHeight,
            Title = _document.PackageProperties.Title,
        };

        var masters = presentationPart.SlideMasterParts.ToList();
        var orderedMasters = new List<SlideMasterPart>();

        foreach (var masterId in presentation?.SlideMasterIdList?.Elements<P.SlideMasterId>() ?? [])
        {
            if (masterId.RelationshipId?.Value is { } relationshipId &&
                presentationPart.TryGetPartById(relationshipId, out var part) &&
                part is SlideMasterPart masterPart)
            {
                orderedMasters.Add(masterPart);
            }
        }

        orderedMasters.AddRange(masters.Where(master => !orderedMasters.Contains(master)));

        var firstTheme = _themes.Get(orderedMasters.Count > 0 ? orderedMasters[0] : null);
        model.Theme = new PresentationTheme
        {
            Name = firstTheme.Name,
            ColorSchemeName = firstTheme.ColorSchemeName,
            HeadingFont = firstTheme.MajorFont,
            BodyFont = firstTheme.MinorFont,
            Colors = new Dictionary<string, string>(firstTheme.Colors, StringComparer.OrdinalIgnoreCase),
        };

        var layoutUsage = new Dictionary<Uri, int>();

        foreach (var (_, slidePart) in _directory.Slides)
        {
            if (slidePart.SlideLayoutPart is { } layout)
            {
                layoutUsage[layout.Uri] = layoutUsage.GetValueOrDefault(layout.Uri) + 1;
            }
        }

        for (var index = 0; index < orderedMasters.Count; index++)
        {
            model.Masters.Add(ReadMaster(orderedMasters[index], index + 1, layoutUsage));
        }

        for (var index = 0; index < _directory.Slides.Count; index++)
        {
            var (slideId, slidePart) = _directory.Slides[index];
            var number = index + 1;
            var full = _options.Slides.Count == 0 || _options.Slides.Contains(number);

            model.Slides.Add(ReadSlide(slidePart, slideId, number, full));
        }

        ReadSections(presentation, model);

        return model;
    }

    private PresentationMaster ReadMaster(SlideMasterPart masterPart, int number, Dictionary<Uri, int> layoutUsage)
    {
        var context = OpenXmlSlideContext.ForMaster(masterPart, _themes);
        var root = masterPart.SlideMaster;
        var master = new PresentationMaster
        {
            Number = number,
            Name = OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(root, "cSld"), "name") is { Length: > 0 } name ? name : context.Theme.Name,
            Background = ReadBackground(OpenXmlMarkup.Child(root, "cSld"), context, masterPart) ?? PresentationFill.Solid(context.Colors.ResolveScheme("bg1") ?? "FFFFFF"),
            TitleStyle = ReadLevelStyle(context.MasterTextStyle("titleStyle"), context),
            BodyStyle = ReadLevelStyle(context.MasterTextStyle("bodyStyle"), context),
        };

        var tree = OpenXmlMarkup.Path(root, "cSld", "spTree");
        master.DecorationCount = tree?.ChildElements.Count(element => element.LocalName is "sp" or "pic" or "grpSp" or "cxnSp" or "graphicFrame" && OpenXmlPlaceholder.From(element) is null) ?? 0;

        foreach (var layoutId in root?.SlideLayoutIdList?.Elements<P.SlideLayoutId>() ?? [])
        {
            if (layoutId.RelationshipId?.Value is not { } relationshipId ||
                !masterPart.TryGetPartById(relationshipId, out var part) ||
                part is not SlideLayoutPart layoutPart)
            {
                continue;
            }

            master.Layouts.Add(ReadLayout(layoutPart, layoutUsage));
        }

        return master;
    }

    private PresentationLayout ReadLayout(SlideLayoutPart layoutPart, Dictionary<Uri, int> layoutUsage)
    {
        var root = layoutPart.SlideLayout;
        var masterContext = layoutPart.SlideMasterPart is null ? null : OpenXmlSlideContext.ForMaster(layoutPart.SlideMasterPart, _themes);
        var layout = new PresentationLayout
        {
            Name = OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(root, "cSld"), "name") ?? "Layout",
            Type = OpenXmlMarkup.Attribute(root, "type") ?? "cust",
            SlideCount = layoutUsage.GetValueOrDefault(layoutPart.Uri),
        };

        foreach (var element in OpenXmlMarkup.Path(root, "cSld", "spTree")?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
        {
            if (OpenXmlPlaceholder.From(element) is not { } placeholder)
            {
                continue;
            }

            var transform = OpenXmlSlideContext.OwnTransform(element);

            if (transform is null && masterContext is not null)
            {
                // A layout placeholder without a position sits where the master's does.
                var masterShape = FindMasterPlaceholder(layoutPart.SlideMasterPart, placeholder);
                transform = OpenXmlSlideContext.OwnTransform(masterShape);
            }

            layout.Placeholders.Add(new PresentationPlaceholder
            {
                Type = placeholder.Type,
                Index = placeholder.Index,
                Name = OpenXmlMarkup.Attribute(NonVisualProperties(element), "name"),
                Bounds = ReadBounds(transform, OpenXmlGroupTransform.Identity).Bounds,
            });
        }

        return layout;
    }

    private static OpenXmlElement FindMasterPlaceholder(SlideMasterPart masterPart, OpenXmlPlaceholder placeholder)
    {
        foreach (var element in OpenXmlMarkup.Path(masterPart?.SlideMaster, "cSld", "spTree")?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
        {
            if (OpenXmlPlaceholder.From(element) is { } candidate && candidate.MasterType == placeholder.MasterType)
            {
                return element;
            }
        }

        return null;
    }

    private PresentationSlide ReadSlide(SlidePart slidePart, uint slideId, int number, bool full)
    {
        var context = OpenXmlSlideContext.ForSlide(slidePart, _themes);
        var root = slidePart.Slide;
        var layoutPart = slidePart.SlideLayoutPart;
        var masterPart = layoutPart?.SlideMasterPart;

        var slide = new PresentationSlide
        {
            Number = number,
            SlideId = slideId,
            Hidden = OpenXmlMarkup.Bool(root, "show") == false,
            LayoutName = OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(layoutPart?.SlideLayout, "cSld"), "name"),
            LayoutType = OpenXmlMarkup.Attribute(layoutPart?.SlideLayout, "type") ?? "cust",
            MasterName = OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(masterPart?.SlideMaster, "cSld"), "name") ?? context.Theme.Name,
            Notes = ReadNotes(slidePart),
            Transition = ReadTransition(root),
            HasAnimations = OpenXmlMarkup.Descendant(OpenXmlMarkup.Child(root, "timing"), "par") is not null,
        };

        slide.Background =
            ReadBackground(OpenXmlMarkup.Child(root, "cSld"), context, slidePart) ??
            ReadBackground(OpenXmlMarkup.Child(layoutPart?.SlideLayout, "cSld"), context, layoutPart) ??
            ReadBackground(OpenXmlMarkup.Child(masterPart?.SlideMaster, "cSld"), context, masterPart) ??
            PresentationFill.Solid(context.Colors.ResolveScheme("bg1") ?? "FFFFFF");

        var includeImages = full && _options.IncludeImageData;
        var z = 0;

        // Every slide is read for its elements so a partial read still knows each title, but pictures are
        // loaded only for the slides asked for.
        slide.Elements = ReadTree(OpenXmlMarkup.Path(root, "cSld", "spTree"), context, OpenXmlGroupTransform.Identity, null, number, includeImages, skipPlaceholders: false, ref z);

        if (full)
        {
            slide.InheritedElements = ReadInherited(slidePart, number, includeImages);
        }

        return slide;
    }

    private List<PresentationElement> ReadInherited(SlidePart slidePart, int number, bool includeImages)
    {
        var inherited = new List<PresentationElement>();
        var layoutPart = slidePart.SlideLayoutPart;

        if (layoutPart is null || OpenXmlMarkup.Bool(slidePart.Slide, "showMasterSp") == false)
        {
            return inherited;
        }

        var slideOverride = OpenXmlMarkup.Child(slidePart.Slide, "clrMapOvr");
        var z = 0;

        if (layoutPart.SlideMasterPart is { } masterPart && OpenXmlMarkup.Bool(layoutPart.SlideLayout, "showMasterSp") != false)
        {
            var masterContext = OpenXmlSlideContext.ForMaster(masterPart, _themes, OpenXmlMarkup.Child(layoutPart.SlideLayout, "clrMapOvr"), slideOverride);
            inherited.AddRange(ReadTree(OpenXmlMarkup.Path(masterPart.SlideMaster, "cSld", "spTree"), masterContext, OpenXmlGroupTransform.Identity, null, number, includeImages, skipPlaceholders: true, ref z));
        }

        var layoutContext = OpenXmlSlideContext.ForLayout(layoutPart, _themes, slideOverride);
        inherited.AddRange(ReadTree(OpenXmlMarkup.Path(layoutPart.SlideLayout, "cSld", "spTree"), layoutContext, OpenXmlGroupTransform.Identity, null, number, includeImages, skipPlaceholders: true, ref z));

        foreach (var element in inherited)
        {
            foreach (var descendant in element.DescendantsAndSelf())
            {
                descendant.IsInherited = true;
            }
        }

        return inherited;
    }

    private List<PresentationElement> ReadTree(
        OpenXmlElement tree,
        OpenXmlSlideContext context,
        OpenXmlGroupTransform transform,
        PresentationFill groupFill,
        int? slideNumber,
        bool includeImages,
        bool skipPlaceholders,
        ref int z)
    {
        var elements = new List<PresentationElement>();

        if (tree is null)
        {
            return elements;
        }

        foreach (var child in tree.ChildElements)
        {
            var candidate = child.LocalName == "AlternateContent" ? ChooseAlternate(child, out _) : child;

            if (candidate is null)
            {
                continue;
            }

            if (skipPlaceholders && OpenXmlPlaceholder.From(candidate) is not null)
            {
                continue;
            }

            var element = ReadElementCore(child, context, transform, groupFill, slideNumber, includeImages, ref z);

            if (element is not null)
            {
                elements.Add(element);
            }
        }

        return elements;
    }

    private PresentationElement ReadElementCore(
        OpenXmlElement markup,
        OpenXmlSlideContext context,
        OpenXmlGroupTransform transform,
        PresentationFill groupFill,
        int? slideNumber,
        bool includeImages,
        ref int z)
    {
        var alternateKind = (PresentationElementKind?)null;
        var element = markup;

        if (markup.LocalName == "AlternateContent")
        {
            element = ChooseAlternate(markup, out alternateKind);

            if (element is null)
            {
                return null;
            }
        }

        var options = includeImages
            ? _options
            : PresentationReadOptions.TextOnly;

        PresentationElement result = element.LocalName switch
        {
            "sp" => ReadShape(element, context, transform, groupFill, slideNumber, options),
            "pic" => ReadPicture(element, context, transform, options),
            "graphicFrame" => ReadGraphicFrame(element, context, transform, options),
            "cxnSp" => ReadConnector(element, context, transform),
            "grpSp" => ReadGroup(element, context, transform, groupFill, slideNumber, includeImages, ref z),
            "contentPart" => new PresentationElement { Kind = PresentationElementKind.Ink, Name = "Ink", UnsupportedDescription = "Ink drawing" },
            _ => null,
        };

        if (result is null)
        {
            return null;
        }

        if (alternateKind is { } kind)
        {
            result.Kind = kind;
            result.UnsupportedDescription ??= kind == PresentationElementKind.Model3D ? "3D model" : null;
        }

        result.ZOrder = ++z;

        return result;
    }

    private PresentationElement ReadShape(
        OpenXmlElement shape,
        OpenXmlSlideContext context,
        OpenXmlGroupTransform transform,
        PresentationFill groupFill,
        int? slideNumber,
        PresentationReadOptions options)
    {
        var placeholder = OpenXmlPlaceholder.From(shape);
        var isTextBox = OpenXmlMarkup.Bool(OpenXmlMarkup.Path(shape, "nvSpPr", "cNvSpPr"), "txBox") == true;
        var element = new PresentationElement
        {
            Kind = placeholder is not null
                ? PresentationElementKind.Placeholder
                : isTextBox ? PresentationElementKind.TextBox : PresentationElementKind.Shape,
            PlaceholderType = placeholder?.Type,
            PlaceholderIndex = placeholder?.Index,
            Geometry = "rect",
        };

        ReadNonVisual(shape, element, context);

        var (bounds, rotation, flipH, flipV) = ReadBounds(context.Transform(shape), transform);
        element.Bounds = bounds;
        element.Rotation = rotation;
        element.FlipHorizontal = flipH;
        element.FlipVertical = flipV;

        var properties = OpenXmlMarkup.Child(shape, "spPr");
        var style = OpenXmlMarkup.Child(shape, "style");
        OpenXmlElement layoutShape = null;
        OpenXmlElement masterShape = null;

        if (placeholder is { } value)
        {
            (layoutShape, masterShape) = context.FindInherited(value);
        }

        if (!OpenXmlGeometryReader.Read(properties, element) &&
            !OpenXmlGeometryReader.Read(OpenXmlMarkup.Child(layoutShape, "spPr"), element))
        {
            OpenXmlGeometryReader.Read(OpenXmlMarkup.Child(masterShape, "spPr"), element);
        }

        element.Fill =
            OpenXmlDrawingReader.ReadFill(OpenXmlDrawingReader.FindFill(properties), context, context.OwnerPart, options, groupFill: groupFill) ??
            OpenXmlDrawingReader.ReadFill(OpenXmlDrawingReader.FindFill(OpenXmlMarkup.Child(layoutShape, "spPr")), context, context.LayoutPart, options) ??
            OpenXmlDrawingReader.ReadFill(OpenXmlDrawingReader.FindFill(OpenXmlMarkup.Child(masterShape, "spPr")), context, context.MasterPart, options) ??
            OpenXmlDrawingReader.ReadStyleFill(OpenXmlMarkup.Child(style, "fillRef"), context, options) ??
            PresentationFill.None;

        var styleLine = OpenXmlDrawingReader.ReadStyleLine(OpenXmlMarkup.Child(style, "lnRef"), context);
        element.Line =
            OpenXmlDrawingReader.ReadLine(OpenXmlMarkup.Child(properties, "ln"), context, inherited: styleLine) ??
            OpenXmlDrawingReader.ReadLine(OpenXmlMarkup.Path(layoutShape, "spPr", "ln"), context) ??
            OpenXmlDrawingReader.ReadLine(OpenXmlMarkup.Path(masterShape, "spPr", "ln"), context) ??
            styleLine ??
            new PresentationLine { Color = null };

        element.HasShadow = OpenXmlMarkup.Path(properties, "effectLst", "outerShdw") is not null;

        var textBody = OpenXmlMarkup.Child(shape, "txBody");

        if (textBody is not null || placeholder is not null)
        {
            element.Text = ReadShapeText(textBody, placeholder, style, context, slideNumber);
        }

        return element;
    }

    private PresentationTextBody ReadShapeText(
        OpenXmlElement textBody,
        OpenXmlPlaceholder? placeholder,
        OpenXmlElement style,
        OpenXmlSlideContext context,
        int? slideNumber)
    {
        var defaults = new OpenXmlTextDefaults
        {
            SlideNumber = slideNumber,
            Font = placeholder?.IsTitle == true ? context.Theme.MajorFont : context.Theme.MinorFont,
        };

        var fontReference = OpenXmlMarkup.Child(style, "fontRef");

        if (fontReference is not null)
        {
            defaults.Color = context.Colors.ResolveChild(fontReference)?.Hex;
            defaults.Font = OpenXmlMarkup.Attribute(fontReference, "idx") == "major" ? context.Theme.MajorFont : context.Theme.MinorFont;
        }

        return OpenXmlTextReader.Read(
            textBody,
            context,
            context.ListStyles(textBody, placeholder),
            context.BodyProperties(textBody, placeholder),
            defaults,
            _directory);
    }

    private static PresentationElement ReadPicture(OpenXmlElement picture, OpenXmlSlideContext context, OpenXmlGroupTransform transform, PresentationReadOptions options)
    {
        var placeholder = OpenXmlPlaceholder.From(picture);
        var element = new PresentationElement
        {
            Kind = PresentationElementKind.Picture,
            PlaceholderType = placeholder?.Type,
            PlaceholderIndex = placeholder?.Index,
            Geometry = "rect",
        };

        ReadNonVisual(picture, element, context);

        var (bounds, rotation, flipH, flipV) = ReadBounds(context.Transform(picture), transform);
        element.Bounds = bounds;
        element.Rotation = rotation;
        element.FlipHorizontal = flipH;
        element.FlipVertical = flipV;

        var blipFill = OpenXmlMarkup.Child(picture, "blipFill");
        element.Image = OpenXmlDrawingReader.ReadImage(OpenXmlMarkup.Child(blipFill, "blip"), context.OwnerPart, options);
        OpenXmlDrawingReader.ReadCrop(OpenXmlMarkup.Child(blipFill, "srcRect"), element.Image);

        var properties = OpenXmlMarkup.Child(picture, "spPr");
        OpenXmlGeometryReader.Read(properties, element);
        element.Line = OpenXmlDrawingReader.ReadLine(OpenXmlMarkup.Child(properties, "ln"), context) ?? new PresentationLine { Color = null };
        element.HasShadow = OpenXmlMarkup.Path(properties, "effectLst", "outerShdw") is not null;

        var nonVisual = OpenXmlMarkup.Path(picture, "nvPicPr", "nvPr");

        if (OpenXmlMarkup.Child(nonVisual, "videoFile") is { } video)
        {
            element.Kind = PresentationElementKind.Video;
            element.MediaUrl = ExternalTarget(context.OwnerPart, OpenXmlMarkup.RelationshipAttribute(video, "link"));
        }
        else if (OpenXmlMarkup.Child(nonVisual, "audioFile") is { } audio)
        {
            element.Kind = PresentationElementKind.Audio;
            element.MediaUrl = ExternalTarget(context.OwnerPart, OpenXmlMarkup.RelationshipAttribute(audio, "link"));
        }
        else if (OpenXmlMarkup.Descendant(nonVisual, "media") is not null)
        {
            element.Kind = context.OwnerPart.DataPartReferenceRelationships.Any(relationship => relationship is VideoReferenceRelationship)
                ? PresentationElementKind.Video
                : PresentationElementKind.Audio;
        }

        return element;
    }

    private PresentationElement ReadGraphicFrame(
        OpenXmlElement frame,
        OpenXmlSlideContext context,
        OpenXmlGroupTransform transform,
        PresentationReadOptions options)
    {
        var placeholder = OpenXmlPlaceholder.From(frame);
        var element = new PresentationElement
        {
            Kind = PresentationElementKind.Unknown,
            PlaceholderType = placeholder?.Type,
            PlaceholderIndex = placeholder?.Index,
            Geometry = "rect",
        };

        ReadNonVisual(frame, element, context);

        var (bounds, rotation, flipH, flipV) = ReadBounds(context.Transform(frame), transform);
        element.Bounds = bounds;
        element.Rotation = rotation;
        element.FlipHorizontal = flipH;
        element.FlipVertical = flipV;

        var data = OpenXmlMarkup.Path(frame, "graphic", "graphicData");
        var uri = OpenXmlMarkup.Attribute(data, "uri");

        switch (uri)
        {
            case OpenXmlPresentationConstants.TableGraphicUri:
                element.Kind = PresentationElementKind.Table;
                element.Table = OpenXmlTableReader.Read(OpenXmlMarkup.Child(data, "tbl"), context, _directory, options);
                break;

            case OpenXmlPresentationConstants.ChartGraphicUri:
                element.Kind = PresentationElementKind.Chart;
                var chartId = OpenXmlMarkup.RelationshipAttribute(OpenXmlMarkup.Child(data, "chart"), "id");
                element.Chart = chartId is not null && context.OwnerPart.TryGetPartById(chartId, out var chartPart) && chartPart is ChartPart part
                    ? OpenXmlChartReader.Read(part, context)
                    : new PresentationChart { Kind = "other" };
                break;

            case OpenXmlPresentationConstants.DiagramGraphicUri:
                element.Kind = PresentationElementKind.Diagram;
                element.UnsupportedDescription = "SmartArt graphic";
                element.FallbackText = ReadDiagramText(data, context.OwnerPart);
                break;

            case OpenXmlPresentationConstants.OleGraphicUri:
                element.Kind = PresentationElementKind.EmbeddedObject;
                var ole = OpenXmlMarkup.Descendant(data, "oleObj");
                var program = OpenXmlMarkup.Attribute(ole, "progId");
                element.UnsupportedDescription = string.IsNullOrEmpty(program) ? "Embedded object" : "Embedded object (" + program + ")";
                var fallback = OpenXmlMarkup.Descendant(ole, "blip");

                if (fallback is not null)
                {
                    element.Image = OpenXmlDrawingReader.ReadImage(fallback, context.OwnerPart, options);
                }

                break;

            default:
                element.UnsupportedDescription = "Unsupported content";
                break;
        }

        return element;
    }

    private static PresentationElement ReadConnector(OpenXmlElement connector, OpenXmlSlideContext context, OpenXmlGroupTransform transform)
    {
        var element = new PresentationElement
        {
            Kind = PresentationElementKind.Connector,
            Geometry = "line",
        };

        ReadNonVisual(connector, element, context);

        var (bounds, rotation, flipH, flipV) = ReadBounds(OpenXmlSlideContext.OwnTransform(connector), transform);
        element.Bounds = bounds;
        element.Rotation = rotation;
        element.FlipHorizontal = flipH;
        element.FlipVertical = flipV;

        var properties = OpenXmlMarkup.Child(connector, "spPr");
        OpenXmlGeometryReader.Read(properties, element);

        var style = OpenXmlMarkup.Child(connector, "style");
        var styleLine = OpenXmlDrawingReader.ReadStyleLine(OpenXmlMarkup.Child(style, "lnRef"), context);
        element.Line = OpenXmlDrawingReader.ReadLine(OpenXmlMarkup.Child(properties, "ln"), context, inherited: styleLine) ?? styleLine ?? new PresentationLine { Color = "000000" };

        return element;
    }

    private PresentationElement ReadGroup(
        OpenXmlElement group,
        OpenXmlSlideContext context,
        OpenXmlGroupTransform transform,
        PresentationFill groupFill,
        int? slideNumber,
        bool includeImages,
        ref int z)
    {
        var element = new PresentationElement
        {
            Kind = PresentationElementKind.Group,
            Geometry = "rect",
        };

        ReadNonVisual(group, element, context);

        var groupTransform = OpenXmlMarkup.Path(group, "grpSpPr", "xfrm");
        var (bounds, rotation, flipH, flipV) = ReadBounds(groupTransform, transform);
        element.Bounds = bounds;
        element.Rotation = rotation;
        element.FlipHorizontal = flipH;
        element.FlipVertical = flipV;

        var options = includeImages ? _options : PresentationReadOptions.TextOnly;
        var ownFill = OpenXmlDrawingReader.ReadFill(OpenXmlDrawingReader.FindFill(OpenXmlMarkup.Child(group, "grpSpPr")), context, context.OwnerPart, options, groupFill: groupFill);

        element.Children = ReadTree(group, context, transform.Enter(groupTransform), ownFill ?? groupFill, slideNumber, includeImages, skipPlaceholders: false, ref z);

        return element;
    }

    private static void ReadNonVisual(OpenXmlElement element, PresentationElement result, OpenXmlSlideContext context)
    {
        var properties = NonVisualProperties(element);

        result.Id = (uint)Math.Clamp(OpenXmlMarkup.Long(properties, "id") ?? 0, 0, uint.MaxValue);
        result.Name = OpenXmlMarkup.Attribute(properties, "name");
        result.AltText = OpenXmlMarkup.Attribute(properties, "descr");
        result.Hidden = OpenXmlMarkup.Bool(properties, "hidden") == true;

        if (string.IsNullOrEmpty(result.AltText))
        {
            var title = OpenXmlMarkup.Attribute(properties, "title");
            result.AltText = string.IsNullOrEmpty(title) ? null : title;
        }

        foreach (var extension in OpenXmlMarkup.Children(OpenXmlMarkup.Child(properties, "extLst"), "ext"))
        {
            if (string.Equals(OpenXmlMarkup.Attribute(extension, "uri"), OpenXmlPresentationConstants.DecorativeExtensionUri, StringComparison.OrdinalIgnoreCase))
            {
                result.IsDecorative = OpenXmlMarkup.Bool(OpenXmlMarkup.Child(extension, "decorative"), "val") != false;
            }
        }

        var link = OpenXmlMarkup.Child(properties, "hlinkClick");

        if (link is not null)
        {
            result.Link = OpenXmlTextReader.ReadLink(link, context.OwnerPart, null);
        }
    }

    /// <summary>
    /// Returns the <c>cNvPr</c> element of a shape, picture, frame, connector or group.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The non-visual drawing properties, or <see langword="null"/>.</returns>
    public static OpenXmlElement NonVisualProperties(OpenXmlElement element)
    {
        if (element is null)
        {
            return null;
        }

        foreach (var child in element.ChildElements)
        {
            if (child.LocalName.StartsWith("nv", StringComparison.Ordinal) && child.LocalName.EndsWith("Pr", StringComparison.Ordinal))
            {
                return OpenXmlMarkup.Child(child, "cNvPr");
            }
        }

        return null;
    }

    private static (PresentationBounds Bounds, double Rotation, bool FlipH, bool FlipV) ReadBounds(OpenXmlElement transform, OpenXmlGroupTransform group)
    {
        if (transform is null)
        {
            return (default, 0, false, false);
        }

        var offset = OpenXmlMarkup.Child(transform, "off");
        var extent = OpenXmlMarkup.Child(transform, "ext");
        var bounds = group.Apply(
            OpenXmlMarkup.Long(offset, "x") ?? 0,
            OpenXmlMarkup.Long(offset, "y") ?? 0,
            OpenXmlMarkup.Long(extent, "cx") ?? 0,
            OpenXmlMarkup.Long(extent, "cy") ?? 0);

        var rotation = ((OpenXmlMarkup.Long(transform, "rot") ?? 0) / 60_000d) + group.Rotation;

        return (bounds, rotation % 360, OpenXmlMarkup.Bool(transform, "flipH") == true, OpenXmlMarkup.Bool(transform, "flipV") == true);
    }

    private static PresentationFill ReadBackground(OpenXmlElement commonSlideData, OpenXmlSlideContext context, OpenXmlPart owner)
    {
        var background = OpenXmlMarkup.Child(commonSlideData, "bg");

        if (background is null)
        {
            return null;
        }

        var properties = OpenXmlMarkup.Child(background, "bgPr");

        if (properties is not null)
        {
            return OpenXmlDrawingReader.ReadFill(OpenXmlDrawingReader.FindFill(properties), context, owner, new PresentationReadOptions());
        }

        var reference = OpenXmlMarkup.Child(background, "bgRef");

        return reference is null ? null : OpenXmlDrawingReader.ReadStyleFill(reference, context, new PresentationReadOptions());
    }

    private static PresentationTextRun ReadLevelStyle(OpenXmlElement style, OpenXmlSlideContext context)
    {
        var properties = OpenXmlMarkup.Path(style, "lvl1pPr", "defRPr");

        if (properties is null)
        {
            return null;
        }

        return new PresentationTextRun
        {
            Font = context.Theme.ResolveFont(OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(properties, "latin"), "typeface")),
            Size = (OpenXmlMarkup.Long(properties, "sz") ?? 1800) / 100d,
            Bold = OpenXmlMarkup.Bool(properties, "b") == true,
            Italic = OpenXmlMarkup.Bool(properties, "i") == true,
            Color = context.Colors.ResolveChild(OpenXmlMarkup.Child(properties, "solidFill"))?.Hex ?? context.Colors.ResolveScheme("tx1"),
        };
    }

    private static string ReadNotes(SlidePart slidePart)
    {
        var notes = slidePart.NotesSlidePart?.NotesSlide;

        if (notes is null)
        {
            return null;
        }

        foreach (var shape in OpenXmlMarkup.Path(notes, "cSld", "spTree")?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
        {
            if (OpenXmlPlaceholder.From(shape) is { Type: "body" } && OpenXmlMarkup.Child(shape, "txBody") is { } body)
            {
                var text = PlainText(body);

                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the plain text of a text body, one paragraph per line.
    /// </summary>
    /// <param name="textBody">The text body.</param>
    /// <returns>The text.</returns>
    public static string PlainText(OpenXmlElement textBody)
    {
        var builder = new StringBuilder();

        foreach (var paragraph in OpenXmlMarkup.Children(textBody, "p"))
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            foreach (var child in paragraph.ChildElements)
            {
                switch (child.LocalName)
                {
                    case "r":
                    case "fld":
                        builder.Append(OpenXmlMarkup.Child(child, "t")?.InnerText);
                        break;
                    case "br":
                        builder.Append('\n');
                        break;
                }
            }
        }

        return builder.ToString().Trim();
    }

    private static string ReadTransition(OpenXmlElement slide)
    {
        var transition = OpenXmlMarkup.Child(slide, "transition");

        if (transition is null)
        {
            var alternate = OpenXmlMarkup.Child(slide, "AlternateContent");
            transition = OpenXmlMarkup.Descendant(alternate, "transition");
        }

        if (transition is null)
        {
            return null;
        }

        var effect = transition.ChildElements.FirstOrDefault(child => child.LocalName != "sndAc" && child.LocalName != "extLst");

        return effect?.LocalName ?? "cut";
    }

    private static string ReadDiagramText(OpenXmlElement data, OpenXmlPart owner)
    {
        var relationships = OpenXmlMarkup.Child(data, "relIds");
        var dataId = OpenXmlMarkup.RelationshipAttribute(relationships, "dm");

        if (dataId is null || !owner.TryGetPartById(dataId, out var part))
        {
            return null;
        }

        var root = part.RootElement;

        if (root is null)
        {
            return null;
        }

        var words = new List<string>();

        foreach (var text in root.Descendants())
        {
            if (text.LocalName == "t" && !string.IsNullOrWhiteSpace(text.InnerText))
            {
                words.Add(text.InnerText.Trim());
            }
        }

        return words.Count == 0 ? null : string.Join("; ", words);
    }

    private static string ExternalTarget(OpenXmlPart owner, string relationshipId)
    {
        if (relationshipId is null || owner is null)
        {
            return null;
        }

        return owner.ExternalRelationships.FirstOrDefault(relationship => relationship.Id == relationshipId)?.Uri?.OriginalString
            ?? owner.DataPartReferenceRelationships.FirstOrDefault(relationship => relationship.Id == relationshipId)?.DataPart?.Uri?.OriginalString;
    }

    private static OpenXmlElement ChooseAlternate(OpenXmlElement alternate, out PresentationElementKind? kind)
    {
        kind = null;

        foreach (var choice in OpenXmlMarkup.Children(alternate, "Choice"))
        {
            var requires = OpenXmlMarkup.Attribute(choice, "Requires") ?? string.Empty;

            if (requires.Contains("am3d", StringComparison.OrdinalIgnoreCase))
            {
                kind = PresentationElementKind.Model3D;
                break;
            }

            var content = choice.ChildElements.FirstOrDefault(child => child.LocalName is "sp" or "pic" or "graphicFrame" or "grpSp" or "cxnSp");

            if (content is not null)
            {
                return content;
            }
        }

        var fallback = OpenXmlMarkup.Child(alternate, "Fallback");

        return fallback?.ChildElements.FirstOrDefault(child => child.LocalName is "sp" or "pic" or "graphicFrame" or "grpSp" or "cxnSp");
    }

    private void ReadSections(P.Presentation presentation, PresentationModel model)
    {
        foreach (var extension in presentation?.Descendants<P.PresentationExtension>() ?? [])
        {
            if (!string.Equals(extension.Uri?.Value, OpenXmlPresentationConstants.SectionListExtensionUri, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var list = OpenXmlMarkup.Child(extension, "sectionLst");

            foreach (var sectionElement in OpenXmlMarkup.Children(list, "section"))
            {
                var section = new PresentationSection
                {
                    Name = OpenXmlMarkup.Attribute(sectionElement, "name") ?? "Section",
                };

                foreach (var entry in OpenXmlMarkup.Children(OpenXmlMarkup.Child(sectionElement, "sldIdLst"), "sldId"))
                {
                    var id = OpenXmlMarkup.Long(entry, "id");

                    if (id is not null && _directory.FindById((uint)id.Value) is { } found)
                    {
                        section.SlideNumbers.Add(found.Number);
                        model.Slides[found.Number - 1].SectionName = section.Name;
                    }
                }

                model.Sections.Add(section);
            }
        }
    }
}
