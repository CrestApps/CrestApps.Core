using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Changes the pages of an existing PDF: combines, cuts, reorders, turns and trims them, and draws
/// watermarks, stamps, page numbers and running text onto them.
/// </summary>
/// <remarks>
/// Operations that change which pages exist are made in place wherever PDFsharp allows it, so the document's
/// bookmarks, form, metadata and links survive; an operation that has to copy pages (a duplicate, a resize,
/// a split) first writes out the document as it stands, so it copies the pages with every earlier change
/// already on them.
/// </remarks>
internal sealed class PdfPageEditor : IDisposable
{
    private readonly string _title;
    private readonly string _dateText;
    private readonly string _fontFamily;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfPageEditor"/> class.
    /// </summary>
    /// <param name="bytes">The PDF to edit.</param>
    /// <param name="password">The owner password, when the file is protected.</param>
    /// <param name="title">The name the <c>{title}</c> and <c>{file}</c> tokens print.</param>
    /// <param name="dateText">The text the <c>{date}</c> token prints.</param>
    public PdfPageEditor(byte[] bytes, string password, string title, string dateText)
    {
        Document = PdfFiles.OpenForEditing(bytes, password);
        _title = title;
        _dateText = dateText;
        _fontFamily = PdfFontFamilies.Default;
    }

    /// <summary>
    /// Gets the document being edited. It is replaced when an operation has to rebuild it.
    /// </summary>
    public PdfDocument Document { get; private set; }

    /// <summary>
    /// Gets the current number of pages.
    /// </summary>
    public int PageCount => Document.PageCount;

    /// <summary>
    /// Adds the pages of other PDFs.
    /// </summary>
    /// <param name="sources">The PDFs, by name, with their bytes.</param>
    /// <param name="position"><c>end</c> (default), <c>start</c>, or a page number to insert after.</param>
    /// <returns>What was done.</returns>
    public string Merge(IReadOnlyList<(string Name, byte[] Bytes)> sources, string position)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var index = ResolveInsertIndex(position);
        var added = new List<string>();

        foreach (var (name, bytes) in sources)
        {
            using var source = PdfFiles.OpenForImport(bytes);
            var count = source.PageCount;

            for (var page = 0; page < count; page++)
            {
                Document.InsertPage(index + page, source.Pages[page]);
            }

            index += count;
            added.Add(string.Create(CultureInfo.InvariantCulture, $"\"{name}\" ({count} page(s))"));
        }

        return "Merged " + string.Join(", ", added) + (string.IsNullOrWhiteSpace(position) || position.Equals("end", StringComparison.OrdinalIgnoreCase) ? " at the end" : $" at position {position}");
    }

    /// <summary>
    /// Keeps only the given pages.
    /// </summary>
    /// <param name="pages">The pages to keep, one-based.</param>
    /// <returns>What was done.</returns>
    public string Extract(List<int> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var keep = pages.ToHashSet();
        var removed = Enumerable.Range(1, PageCount).Where(page => !keep.Contains(page)).ToList();

        RemovePages(removed);

        return $"Kept pages {PdfPageRange.Describe(pages)}";
    }

    /// <summary>
    /// Removes pages.
    /// </summary>
    /// <param name="pages">The pages, one-based.</param>
    /// <returns>What was done.</returns>
    public string Delete(List<int> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (pages.Count >= PageCount)
        {
            throw new PdfToolException("A PDF must keep at least one page; this would delete all of them.");
        }

        RemovePages(pages);

        return $"Deleted pages {PdfPageRange.Describe(pages)}";
    }

    /// <summary>
    /// Puts the pages in a new order.
    /// </summary>
    /// <param name="order">The pages in their new order, one-based. Pages not listed follow in their current order.</param>
    /// <returns>What was done.</returns>
    public string Reorder(List<int> order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.Distinct().Count() != order.Count)
        {
            throw new PdfToolException("A reorder lists each page once; use the duplicate operation to repeat a page.");
        }

        var pages = Enumerable.Range(0, PageCount).Select(index => Document.Pages[index]).ToList();
        var sequence = order.Select(number => pages[number - 1]).ToList();
        var unlisted = pages.Where(page => !sequence.Contains(page)).ToList();

        sequence.AddRange(unlisted);

        for (var target = 0; target < sequence.Count; target++)
        {
            var current = IndexOf(sequence[target]);

            if (current != target)
            {
                Document.Pages.MovePage(current, target);
            }
        }

        return unlisted.Count == 0
            ? $"Reordered the pages to {string.Join(", ", order)}"
            : $"Reordered the pages to {string.Join(", ", order)}, followed by the {unlisted.Count} page(s) not listed";
    }

    /// <summary>
    /// Reverses the page order.
    /// </summary>
    /// <returns>What was done.</returns>
    public string Reverse()
    {
        Reorder([.. Enumerable.Range(1, PageCount).Reverse()]);

        return "Reversed the page order";
    }

    /// <summary>
    /// Turns pages.
    /// </summary>
    /// <param name="pages">The pages, one-based.</param>
    /// <param name="degrees">The clockwise rotation, a multiple of 90.</param>
    /// <returns>What was done.</returns>
    public string Rotate(List<int> pages, int degrees)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (degrees % 90 != 0)
        {
            throw new PdfToolException("Pages can only be turned by multiples of 90 degrees.");
        }

        foreach (var number in pages)
        {
            var page = Document.Pages[number - 1];
            page.Rotate = (((page.Rotate + degrees) % 360) + 360) % 360;
        }

        return $"Rotated pages {PdfPageRange.Describe(pages)} by {degrees}°";
    }

    /// <summary>
    /// Inserts copies of pages right after each one.
    /// </summary>
    /// <param name="pages">The pages, one-based.</param>
    /// <param name="copies">How many copies of each.</param>
    /// <returns>What was done.</returns>
    public string Duplicate(List<int> pages, int copies)
    {
        ArgumentNullException.ThrowIfNull(pages);

        copies = Math.Clamp(copies, 1, 50);

        var snapshot = Checkpoint();

        using var source = PdfFiles.OpenForImport(snapshot);

        // Inserted from the last page back, so the page numbers still to process do not shift.
        foreach (var number in pages.OrderDescending())
        {
            for (var copy = 0; copy < copies; copy++)
            {
                Document.InsertPage(number, source.Pages[number - 1]);
            }
        }

        return $"Duplicated pages {PdfPageRange.Describe(pages)} ({copies} cop{(copies == 1 ? "y" : "ies")} each)";
    }

    /// <summary>
    /// Inserts blank pages.
    /// </summary>
    /// <param name="after">The page they go after; 0 puts them first.</param>
    /// <param name="count">How many.</param>
    /// <param name="size">The paper size, or <see langword="null"/> to match the neighbouring page.</param>
    /// <param name="orientation">The orientation, or <see langword="null"/>.</param>
    /// <returns>What was done.</returns>
    public string InsertBlank(int after, int count, string size, string orientation)
    {
        if (after < 0 || after > PageCount)
        {
            throw new PdfToolException($"'after' must be between 0 and {PageCount}.");
        }

        count = Math.Clamp(count, 1, 100);

        var neighbour = Document.Pages[Math.Clamp(after - 1, 0, PageCount - 1)];
        var (width, height) = ResolveSize(size, orientation, neighbour.Width.Point, neighbour.Height.Point);

        for (var index = 0; index < count; index++)
        {
            var page = Document.InsertPage(after + index);
            page.Width = XUnit.FromPoint(width);
            page.Height = XUnit.FromPoint(height);
        }

        return $"Inserted {count} blank page(s) after page {after}";
    }

    /// <summary>
    /// Trims the visible area of pages.
    /// </summary>
    /// <param name="pages">The pages, one-based.</param>
    /// <param name="margins">How much to trim from each edge, in millimetres.</param>
    /// <returns>What was done.</returns>
    public string Crop(List<int> pages, PdfPageSetupDefinition margins)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (margins is null)
        {
            throw new PdfToolException("A crop needs 'margins' with margin_top_mm, margin_bottom_mm, margin_left_mm and/or margin_right_mm.");
        }

        foreach (var number in pages)
        {
            var page = Document.Pages[number - 1];
            var box = page.EffectiveCropBoxReadOnly;
            var left = box.X1 + PdfPageSizes.FromMillimetres(margins.MarginLeftMm ?? 0);
            var right = box.X2 - PdfPageSizes.FromMillimetres(margins.MarginRightMm ?? 0);
            var bottom = box.Y1 + PdfPageSizes.FromMillimetres(margins.MarginBottomMm ?? 0);
            var top = box.Y2 - PdfPageSizes.FromMillimetres(margins.MarginTopMm ?? 0);

            if (right - left < 36 || top - bottom < 36)
            {
                throw new PdfToolException($"Those margins leave less than half an inch of page {number}.");
            }

            page.CropBox = new PdfRectangle(new XPoint(left, bottom), new XPoint(right, top));
        }

        return $"Cropped pages {PdfPageRange.Describe(pages)}";
    }

    /// <summary>
    /// Fits every page onto a new paper size, scaling its content to fit and keeping its proportions.
    /// </summary>
    /// <param name="size">The paper size.</param>
    /// <param name="orientation">The orientation, or <see langword="null"/> to follow each page.</param>
    /// <returns>What was done.</returns>
    public string Resize(string size, string orientation)
    {
        if (string.IsNullOrWhiteSpace(size))
        {
            throw new PdfToolException("A resize needs 'size', such as A4 or Letter.");
        }

        var snapshot = Checkpoint();
        var resized = new PdfDocument();

        resized.Info.Title = Document.Info.Title;
        resized.Info.Author = Document.Info.Author;
        resized.Info.Subject = Document.Info.Subject;
        resized.Info.Keywords = Document.Info.Keywords;

        using (var stream = new MemoryStream(snapshot, writable: false))
        {
            using var form = XPdfForm.FromStream(stream);

            for (var index = 0; index < form.PageCount; index++)
            {
                form.PageIndex = index;

                var sourceWidth = form.PointWidth;
                var sourceHeight = form.PointHeight;
                var (width, height) = ResolveSize(size, orientation ?? (sourceWidth > sourceHeight ? "landscape" : "portrait"), sourceWidth, sourceHeight);
                var page = resized.AddPage();

                page.Width = XUnit.FromPoint(width);
                page.Height = XUnit.FromPoint(height);

                using var graphics = XGraphics.FromPdfPage(page);

                var scale = Math.Min(width / sourceWidth, height / sourceHeight);
                var drawnWidth = sourceWidth * scale;
                var drawnHeight = sourceHeight * scale;

                graphics.DrawImage(form, (width - drawnWidth) / 2, (height - drawnHeight) / 2, drawnWidth, drawnHeight);
            }
        }

        Document.Dispose();
        Document = resized;

        return $"Resized every page to {size}{(string.IsNullOrWhiteSpace(orientation) ? string.Empty : " " + orientation)}, scaling the content to fit (links and form fields on the pages are not carried over)";
    }

    /// <summary>
    /// Cuts the document into parts.
    /// </summary>
    /// <param name="parts">The pages of each part, one-based.</param>
    /// <returns>Each part's file.</returns>
    public List<byte[]> Split(List<List<int>> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var snapshot = Checkpoint();
        var files = new List<byte[]>(parts.Count);

        using var source = PdfFiles.OpenForImport(snapshot);

        foreach (var part in parts)
        {
            using var output = new PdfDocument();

            output.Info.Title = source.Info.Title;
            output.Info.Author = source.Info.Author;

            foreach (var number in part)
            {
                output.AddPage(source.Pages[number - 1]);
            }

            files.Add(PdfFiles.Save(output));
        }

        return files;
    }

    /// <summary>
    /// Draws a watermark on pages.
    /// </summary>
    /// <param name="pages">The pages, one-based.</param>
    /// <param name="watermark">The watermark.</param>
    /// <param name="image">The watermark picture, for an image watermark.</param>
    /// <returns>What was done.</returns>
    public string Watermark(List<int> pages, PdfWatermarkDefinition watermark, PdfImageData image)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(watermark);

        foreach (var number in pages)
        {
            PdfPageDecorator.DrawWatermark(Document.Pages[number - 1], watermark, image, _fontFamily);
        }

        return $"Added the watermark \"{watermark.Text ?? watermark.Image}\" to pages {PdfPageRange.Describe(pages)}";
    }

    /// <summary>
    /// Stamps text at a position on pages.
    /// </summary>
    /// <param name="pages">The pages, one-based.</param>
    /// <param name="text">The text, which may carry tokens.</param>
    /// <param name="position">The position.</param>
    /// <param name="style">How the text is drawn.</param>
    /// <returns>What was done.</returns>
    public string Stamp(List<int> pages, string text, string position, PdfTextStampStyle style)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new PdfToolException("A stamp needs 'text'.");
        }

        foreach (var number in pages)
        {
            PdfPageDecorator.DrawText(Document.Pages[number - 1], ReplaceTokens(text, number, number.ToString(CultureInfo.InvariantCulture)), position, style);
        }

        return $"Stamped \"{text}\" at {position ?? "top-right"} on pages {PdfPageRange.Describe(pages)}";
    }

    /// <summary>
    /// Numbers pages.
    /// </summary>
    /// <param name="pages">The pages that carry a number, one-based.</param>
    /// <param name="template">The text the number is printed in.</param>
    /// <param name="position">The position.</param>
    /// <param name="startAt">The number the first numbered page carries.</param>
    /// <param name="format">The numbering style.</param>
    /// <param name="style">How the text is drawn.</param>
    /// <returns>What was done.</returns>
    public string PageNumbers(List<int> pages, string template, string position, int startAt, string format, PdfTextStampStyle style)
    {
        ArgumentNullException.ThrowIfNull(pages);

        template = string.IsNullOrWhiteSpace(template) ? "Page {page} of {pages}" : template;

        var total = pages.Count;

        for (var index = 0; index < pages.Count; index++)
        {
            var label = FormatNumber(startAt + index, format);
            var text = ReplaceTokens(template, pages[index], label, FormatNumber(startAt + total - 1, format));

            PdfPageDecorator.DrawText(Document.Pages[pages[index] - 1], text, position ?? "bottom-center", style);
        }

        return $"Numbered pages {PdfPageRange.Describe(pages)} (\"{template}\" at {position ?? "bottom-center"}, starting at {FormatNumber(startAt, format)})";
    }

    /// <summary>
    /// Draws running text at the top and bottom of pages.
    /// </summary>
    /// <param name="pages">The pages, one-based.</param>
    /// <param name="header">The text at the top, in three slots.</param>
    /// <param name="footer">The text at the bottom, in three slots.</param>
    /// <param name="style">How the text is drawn.</param>
    /// <returns>What was done.</returns>
    public string HeaderFooter(List<int> pages, PdfHeaderFooterDefinition header, PdfHeaderFooterDefinition footer, PdfTextStampStyle style)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (header is null && footer is null)
        {
            throw new PdfToolException("A header_footer needs 'header' and/or 'footer' with left, center or right text.");
        }

        foreach (var number in pages)
        {
            var page = Document.Pages[number - 1];
            var label = number.ToString(CultureInfo.InvariantCulture);

            DrawSlots(page, header, "top", number, label, style);
            DrawSlots(page, footer, "bottom", number, label, style);
        }

        return $"Added {(header is null ? string.Empty : "a header")}{(header is not null && footer is not null ? " and " : string.Empty)}{(footer is null ? string.Empty : "a footer")} to pages {PdfPageRange.Describe(pages)}";
    }

    /// <summary>
    /// Writes the document out.
    /// </summary>
    /// <returns>The file.</returns>
    public byte[] Save()
    {
        PruneOutlines();

        return PdfFiles.Save(Document);
    }

    /// <summary>
    /// Releases the document.
    /// </summary>
    public void Dispose()
    {
        Document?.Dispose();
    }

    private void DrawSlots(PdfPage page, PdfHeaderFooterDefinition slots, string edge, int number, string label, PdfTextStampStyle style)
    {
        if (slots is null)
        {
            return;
        }

        var slotStyle = new PdfTextStampStyle
        {
            FontFamily = style.FontFamily,
            FontSize = slots.FontSize ?? style.FontSize,
            Bold = style.Bold,
            Color = PdfColor.Parse(slots.Color, style.Color),
            Opacity = style.Opacity,
            Margin = style.Margin,
        };

        PdfPageDecorator.DrawText(page, ReplaceTokens(slots.Left, number, label), edge + "-left", slotStyle);
        PdfPageDecorator.DrawText(page, ReplaceTokens(slots.Center, number, label), edge + "-center", slotStyle);
        PdfPageDecorator.DrawText(page, ReplaceTokens(slots.Right, number, label), edge + "-right", slotStyle);
    }

    private string ReplaceTokens(string text, int pageNumber, string label, string totalLabel = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return new StringBuilder(text)
            .Replace("{page}", label)
            .Replace("{pages}", totalLabel ?? PageCount.ToString(CultureInfo.InvariantCulture))
            .Replace("{title}", _title ?? string.Empty)
            .Replace("{file}", _title ?? string.Empty)
            .Replace("{date}", _dateText ?? string.Empty)
            .Replace("{index}", pageNumber.ToString(CultureInfo.InvariantCulture))
            .ToString();
    }

    /// <summary>
    /// Writes the numbering style a page number is printed in.
    /// </summary>
    /// <param name="number">The number.</param>
    /// <param name="format">The style: <c>1</c>, <c>i</c>, <c>I</c>, <c>a</c> or <c>A</c>.</param>
    /// <returns>The number as printed.</returns>
    public static string FormatNumber(int number, string format)
    {
        return format?.Trim() switch
        {
            "i" or "roman" => ToRoman(number).ToLowerInvariant(),
            "I" or "ROMAN" => ToRoman(number),
            "a" or "alphabetic" => ToLetters(number).ToLowerInvariant(),
            "A" or "ALPHABETIC" => ToLetters(number),
            _ => number.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static string ToRoman(int number)
    {
        if (number <= 0 || number > 3999)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        (int Value, string Symbol)[] numerals =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
            (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
        ];

        var builder = new StringBuilder();

        foreach (var (value, symbol) in numerals)
        {
            while (number >= value)
            {
                builder.Append(symbol);
                number -= value;
            }
        }

        return builder.ToString();
    }

    private static string ToLetters(int number)
    {
        if (number <= 0)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        var builder = new StringBuilder();

        while (number > 0)
        {
            number--;
            builder.Insert(0, (char)('A' + (number % 26)));
            number /= 26;
        }

        return builder.ToString();
    }

    private int ResolveInsertIndex(string position)
    {
        if (string.IsNullOrWhiteSpace(position) || position.Trim().Equals("end", StringComparison.OrdinalIgnoreCase))
        {
            return PageCount;
        }

        if (position.Trim().Equals("start", StringComparison.OrdinalIgnoreCase) || position.Trim().Equals("beginning", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (int.TryParse(position.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var after) && after >= 0 && after <= PageCount)
        {
            return after;
        }

        throw new PdfToolException($"'position' must be \"start\", \"end\", or a page number from 0 to {PageCount} to insert after.");
    }

    private void RemovePages(IEnumerable<int> pages)
    {
        foreach (var number in pages.Distinct().OrderDescending())
        {
            Document.Pages.RemoveAt(number - 1);
        }
    }

    private int IndexOf(PdfPage page)
    {
        for (var index = 0; index < Document.PageCount; index++)
        {
            if (ReferenceEquals(Document.Pages[index], page))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Writes the document out and reopens it, so an operation that copies pages copies them as they stand.
    /// </summary>
    /// <returns>The document as it stands.</returns>
    private byte[] Checkpoint()
    {
        var bytes = Save();

        Document.Dispose();
        Document = PdfFiles.OpenForEditing(bytes);

        return bytes;
    }

    /// <summary>
    /// Drops bookmarks whose page was removed; a bookmark to a page that is gone points nowhere.
    /// </summary>
    private void PruneOutlines()
    {
        var pages = new HashSet<PdfPage>(Enumerable.Range(0, Document.PageCount).Select(index => Document.Pages[index]));

        void Prune(PdfOutlineCollection outlines)
        {
            for (var index = outlines.Count - 1; index >= 0; index--)
            {
                var outline = outlines[index];

                if (outline.DestinationPage is not null && !pages.Contains(outline.DestinationPage))
                {
                    outlines.RemoveAt(index);

                    continue;
                }

                if (outline.HasChildren)
                {
                    Prune(outline.Outlines);
                }
            }
        }

        try
        {
            Prune(Document.Outlines);
        }
        catch (Exception)
        {
            // An outline PDFsharp cannot walk is left as it is rather than failing the edit.
        }
    }

    private static (double Width, double Height) ResolveSize(string size, string orientation, double fallbackWidth, double fallbackHeight)
    {
        double width = fallbackWidth;
        double height = fallbackHeight;

        if (!string.IsNullOrWhiteSpace(size) && !PdfPageSizes.TryGet(size, out width, out height))
        {
            throw new PdfToolException($"\"{size}\" is not a paper size. Known sizes: {string.Join(", ", PdfPageSizes.Names)}.");
        }

        var value = orientation?.Trim().ToLowerInvariant();

        if ((value == "landscape" && width < height) || (value == "portrait" && width > height))
        {
            (width, height) = (height, width);
        }

        return (width, height);
    }
}
