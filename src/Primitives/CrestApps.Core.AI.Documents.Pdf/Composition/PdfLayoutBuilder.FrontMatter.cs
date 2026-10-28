using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

internal sealed partial class PdfLayoutBuilder
{
    private void AddCoverSection(PdfLayoutDocument document)
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
        var alignment = ReadAlignment(cover.Align, PdfLayoutAlignment.Center);

        if (hasBackground)
        {
            // A frame the size of the page, laid behind the text, fills the whole page.
            var frame = section.AddFrame();
            frame.LeftFromPage = true;
            frame.TopFromPage = true;
            frame.Width = _page.Width;
            frame.Height = _page.Height;
            frame.Fill = background;
        }
        else
        {
            // Unfilled, the cover still gets a band of the brand colour so it reads as a cover and not as a
            // page that happens to have a big title on it.
            var band = section.AddFrame();
            band.LeftFromPage = true;
            band.TopFromPage = true;
            band.Width = _page.Width;
            band.Height = 14;
            band.Fill = _theme.Primary;
        }

        var logo = cover.Logo ?? _theme.Logo;
        var top = section.AddParagraph();

        top.Format.SpaceBefore = Math.Max(40, _page.UsableHeight * 0.22);
        top.Format.Alignment = alignment;

        if (!string.IsNullOrWhiteSpace(logo) && TryGetImage(logo, out var picture))
        {
            var height = Math.Min(Math.Max(_theme.LogoHeight * 2, 48), 110);
            var width = picture.Height > 0 ? height * picture.Width / picture.Height : height;

            if (width > _page.UsableWidth * 0.6)
            {
                height *= _page.UsableWidth * 0.6 / width;
                width = _page.UsableWidth * 0.6;
            }

            top.AddImage(picture, width, height);
            top.Format.SpaceAfter = 28;
        }
        else if (!string.IsNullOrWhiteSpace(logo))
        {
            _missingImageCount++;
        }

        var title = section.AddParagraph();
        title.Style = TitleStyle;
        title.Format.Font.Size = Math.Round(_theme.BaseFontSize * 3, 1);
        title.Format.Font.Color = textColor;
        title.Format.Alignment = alignment;
        title.Format.SpaceAfter = 10;

        // The cover title is not a heading of the body, so it does not become a bookmark of its own.
        title.Format.OutlineLevel = 0;

        var (titleText, subtitleText) = CoverText(cover);

        AddInline(title, titleText, null, null);

        if (!string.IsNullOrWhiteSpace(subtitleText))
        {
            var subtitle = section.AddParagraph();
            subtitle.Format.Font.Size = Math.Round(_theme.BaseFontSize * 1.45, 1);
            subtitle.Format.Font.Color = hasBackground ? textColor : _theme.Muted;
            subtitle.Format.Alignment = alignment;
            subtitle.Format.SpaceAfter = 30;
            AddInline(subtitle, subtitleText, null, null);
        }

        var rule = section.AddParagraph();
        rule.Format.Alignment = alignment;
        rule.Format.SpaceBefore = 6;
        rule.Format.SpaceAfter = 18;
        rule.Format.LeftIndent = alignment == PdfLayoutAlignment.Center ? _page.UsableWidth * 0.35 : 0;
        rule.Format.RightIndent = alignment == PdfLayoutAlignment.Center ? _page.UsableWidth * 0.35 : _page.UsableWidth * 0.7;
        rule.Format.Borders.Bottom.Width = 2;
        rule.Format.Borders.Bottom.Color = hasBackground ? textColor : _theme.Accent;
        rule.Format.Font.Size = 1;

        foreach (var line in new[] { cover.Author ?? _definition.Author, cover.Date })
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var paragraph = section.AddParagraph();
            paragraph.Format.Font.Size = Math.Round(_theme.BaseFontSize * 1.15, 1);
            paragraph.Format.Font.Color = textColor;
            paragraph.Format.Alignment = alignment;
            paragraph.Format.SpaceAfter = 4;
            AddInline(paragraph, line, null, null);
        }

        if (!string.IsNullOrWhiteSpace(cover.Note))
        {
            var frame = section.AddFrame();
            frame.TopFromPage = true;
            frame.Left = 0;
            frame.Top = _page.Height - _page.Bottom - 40;
            frame.Width = _page.UsableWidth;
            frame.Height = 40;

            var note = frame.AddParagraph();
            note.Format.Font.Size = Math.Max(6, _theme.BaseFontSize - 2);
            note.Format.Font.Color = hasBackground ? textColor : _theme.Muted;
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

    private void AddTableOfContentsSection(PdfLayoutDocument document)
    {
        var contents = _definition.TableOfContents;
        var section = document.AddSection();

        _page = ApplyPageSetup(section.PageSetup, null);

        // The contents page is numbered like every page after the cover, unless only the body is numbered.
        if (!_numberBodyOnly)
        {
            AddRunningHeads(section, !_runningHeadsStarted);
            _runningHeadsStarted = true;
        }

        var title = section.AddParagraph(string.IsNullOrWhiteSpace(contents.Title) ? "Contents" : contents.Title);
        title.Style = TocTitleStyle;

        if (_tocEntries.Count == 0)
        {
            var empty = section.AddParagraph("This document has no headings yet.");
            empty.Style = CaptionStyle;
            empty.Format.Alignment = PdfLayoutAlignment.Left;

            return;
        }

        var format = PageNumberFormat();

        foreach (var entry in _tocEntries)
        {
            var paragraph = section.AddParagraph();
            paragraph.Style = TocEntryStylePrefix + Math.Clamp(entry.Level, 1, 4).ToString(CultureInfo.InvariantCulture);
            paragraph.Format.TabStops = [new PdfLayoutTabStop(_page.UsableWidth, PdfLayoutAlignment.Right, DotLeader: true)];

            var link = paragraph.AddHyperlink(entry.Bookmark, isWeb: false);
            link.AddText(entry.Text);
            link.AddTab();
            link.AddPageRefField(entry.Bookmark).Format = format;
        }
    }
}
