using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

internal sealed partial class PdfMigraDocBuilder
{
    private void AddCoverSection(Document document)
    {
        var cover = _definition.CoverPage;
        var section = document.AddSection();

        _page = ApplyPageSetup(section.PageSetup, null);

        var hasBackground = PdfColor.TryParse(cover.BackgroundColor, out var background);
        var textColor = PdfResolvedTheme.ReadColor(
            cover.TextColor,
            hasBackground ? background.IsLight ? _theme.Text : _white : _theme.Heading,
            "cover_page.text_color",
            _warnings);
        var alignment = ReadAlignment(cover.Align, ParagraphAlignment.Center);

        if (hasBackground)
        {
            // A frame the size of the page, laid behind the text, is how MigraDoc fills a whole page.
            var frame = section.AddTextFrame();
            frame.RelativeHorizontal = RelativeHorizontal.Page;
            frame.RelativeVertical = RelativeVertical.Page;
            frame.Left = ShapePosition.Left;
            frame.Top = ShapePosition.Top;
            frame.Width = Unit.FromPoint(_page.Width);
            frame.Height = Unit.FromPoint(_page.Height);
            frame.FillFormat.Color = background.ToMigraDoc();
            frame.LineFormat.Visible = false;
            frame.WrapFormat.Style = WrapStyle.Through;
        }
        else
        {
            // Unfilled, the cover still gets a band of the brand colour so it reads as a cover and not as a
            // page that happens to have a big title on it.
            var band = section.AddTextFrame();
            band.RelativeHorizontal = RelativeHorizontal.Page;
            band.RelativeVertical = RelativeVertical.Page;
            band.Left = ShapePosition.Left;
            band.Top = ShapePosition.Top;
            band.Width = Unit.FromPoint(_page.Width);
            band.Height = Unit.FromPoint(14);
            band.FillFormat.Color = _theme.Primary.ToMigraDoc();
            band.LineFormat.Visible = false;
            band.WrapFormat.Style = WrapStyle.Through;
        }

        var logo = cover.Logo ?? _theme.Logo;
        var top = section.AddParagraph();

        top.Format.SpaceBefore = Unit.FromPoint(Math.Max(40, _page.UsableHeight * 0.22));
        top.Format.Alignment = alignment;

        if (!string.IsNullOrWhiteSpace(logo) && TryGetImage(logo, out var logoData, out var logoWidth, out var logoHeight))
        {
            var image = top.AddImage(ToImageName(logoData));
            var height = Math.Min(Math.Max(_theme.LogoHeight * 2, 48), 110);
            var width = logoHeight > 0 ? height * logoWidth / logoHeight : height;

            if (width > _page.UsableWidth * 0.6)
            {
                height *= _page.UsableWidth * 0.6 / width;
                width = _page.UsableWidth * 0.6;
            }

            image.Height = Unit.FromPoint(height);
            image.Width = Unit.FromPoint(width);
            image.LockAspectRatio = false;
            top.Format.SpaceAfter = Unit.FromPoint(28);
        }
        else if (!string.IsNullOrWhiteSpace(logo))
        {
            _missingImageCount++;
        }

        var title = section.AddParagraph();
        title.Style = TitleStyle;
        title.Format.Font.Size = Math.Round(_theme.BaseFontSize * 3, 1);
        title.Format.Font.Color = textColor.ToMigraDoc();
        title.Format.Alignment = alignment;
        title.Format.SpaceAfter = Unit.FromPoint(10);

        // The cover title is not a heading of the body, so it does not become a bookmark of its own.
        title.Format.OutlineLevel = OutlineLevel.BodyText;

        var (titleText, subtitleText) = CoverText(cover);

        AddInline(title, titleText, null, null);

        if (!string.IsNullOrWhiteSpace(subtitleText))
        {
            var subtitle = section.AddParagraph();
            subtitle.Format.Font.Size = Math.Round(_theme.BaseFontSize * 1.45, 1);
            subtitle.Format.Font.Color = (hasBackground ? textColor : _theme.Muted).ToMigraDoc();
            subtitle.Format.Alignment = alignment;
            subtitle.Format.SpaceAfter = Unit.FromPoint(30);
            AddInline(subtitle, subtitleText, null, null);
        }

        var rule = section.AddParagraph();
        rule.Format.Alignment = alignment;
        rule.Format.SpaceBefore = Unit.FromPoint(6);
        rule.Format.SpaceAfter = Unit.FromPoint(18);
        rule.Format.LeftIndent = alignment == ParagraphAlignment.Center ? Unit.FromPoint(_page.UsableWidth * 0.35) : 0;
        rule.Format.RightIndent = Unit.FromPoint(alignment == ParagraphAlignment.Center ? _page.UsableWidth * 0.35 : _page.UsableWidth * 0.7);
        rule.Format.Borders.Bottom.Width = 2;
        rule.Format.Borders.Bottom.Color = (hasBackground ? textColor : _theme.Accent).ToMigraDoc();
        rule.Format.Font.Size = 1;

        foreach (var line in new[] { cover.Author ?? _definition.Author, cover.Date })
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var paragraph = section.AddParagraph();
            paragraph.Format.Font.Size = Math.Round(_theme.BaseFontSize * 1.15, 1);
            paragraph.Format.Font.Color = textColor.ToMigraDoc();
            paragraph.Format.Alignment = alignment;
            paragraph.Format.SpaceAfter = Unit.FromPoint(4);
            AddInline(paragraph, line, null, null);
        }

        if (!string.IsNullOrWhiteSpace(cover.Note))
        {
            var frame = section.AddTextFrame();
            frame.RelativeHorizontal = RelativeHorizontal.Margin;
            frame.RelativeVertical = RelativeVertical.Page;
            frame.Left = ShapePosition.Left;
            frame.Top = Unit.FromPoint(_page.Height - _page.Bottom - 40);
            frame.Width = Unit.FromPoint(_page.UsableWidth);
            frame.Height = Unit.FromPoint(40);
            frame.WrapFormat.Style = WrapStyle.Through;

            var note = frame.AddParagraph();
            note.Format.Font.Size = Math.Max(6, _theme.BaseFontSize - 2);
            note.Format.Font.Color = (hasBackground ? textColor : _theme.Muted).ToMigraDoc();
            note.Format.Alignment = alignment;
            AddInline(note, cover.Note, null, null);
        }
    }

    private (string Title, string Subtitle) CoverText(PdfCoverPageDefinition cover)
    {
        var title = !string.IsNullOrWhiteSpace(cover.Title) ? cover.Title : _definition.Title;

        if (!string.IsNullOrWhiteSpace(title))
        {
            return (title, cover.Subtitle);
        }

        _warnings.Add("The cover page has no title, so its subtitle or the first heading stands in for one. Give the document a title with format_pdf (title or cover_page.title).");

        // Without a title, the subtitle reads as the cover's title, or else the first heading does.
        return !string.IsNullOrWhiteSpace(cover.Subtitle)
            ? (cover.Subtitle, null)
            : (_tocEntries.Count > 0 ? _tocEntries[0].Text : null, null);
    }

    private void AddTableOfContentsSection(Document document)
    {
        var contents = _definition.TableOfContents;
        var section = document.AddSection();

        _page = ApplyPageSetup(section.PageSetup, null);

        var title = section.AddParagraph(string.IsNullOrWhiteSpace(contents.Title) ? "Contents" : contents.Title);
        title.Style = TocTitleStyle;

        if (_tocEntries.Count == 0)
        {
            var empty = section.AddParagraph("This document has no headings yet.");
            empty.Style = CaptionStyle;
            empty.Format.Alignment = ParagraphAlignment.Left;

            return;
        }

        var format = PageNumberFormat();

        foreach (var entry in _tocEntries)
        {
            var paragraph = section.AddParagraph();
            paragraph.Style = TocEntryStylePrefix + Math.Clamp(entry.Level, 1, 4).ToString(System.Globalization.CultureInfo.InvariantCulture);
            paragraph.Format.TabStops.ClearAll();
            paragraph.Format.TabStops.AddTabStop(Unit.FromPoint(_page.UsableWidth), TabAlignment.Right, TabLeader.Dots);

            var link = paragraph.AddHyperlink(entry.Bookmark);
            link.AddText(entry.Text);
            link.AddTab();

            var field = link.AddPageRefField(entry.Bookmark);

            if (!string.IsNullOrEmpty(format))
            {
                field.Format = format;
            }
        }
    }
}
