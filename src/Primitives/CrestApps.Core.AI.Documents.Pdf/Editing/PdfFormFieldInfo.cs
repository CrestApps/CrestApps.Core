using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// What a form field is: its name, type, value, choices and constraints, and where it sits.
/// </summary>
internal sealed class PdfFormFieldInfo
{
    /// <summary>
    /// Gets or sets the fully qualified name, with parent names joined by dots.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the type: <c>text</c>, <c>checkbox</c>, <c>radio</c>, <c>dropdown</c>, <c>listbox</c>,
    /// <c>signature</c> or <c>button</c>.
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the current value as text.
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// Gets or sets the default value as text.
    /// </summary>
    public string DefaultValue { get; set; }

    /// <summary>
    /// Gets or sets the choices of a dropdown, list box or radio group, or a checkbox's on-value.
    /// </summary>
    public List<string> Options { get; set; } = [];

    /// <summary>
    /// Gets or sets the tooltip, which assistive technology reads as the field's label.
    /// </summary>
    public string Tooltip { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the field must be filled.
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the field cannot be changed.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a text field takes several lines.
    /// </summary>
    public bool Multiline { get; set; }

    /// <summary>
    /// Gets or sets the most characters a text field takes.
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Gets or sets the one-based page the field's first widget is on.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets the first widget's rectangle in user space.
    /// </summary>
    public PdfRectangle Rectangle { get; set; }

    /// <summary>
    /// Gets or sets the field dictionary.
    /// </summary>
    public PdfDictionary Dictionary { get; set; }

    /// <summary>
    /// Gets or sets the widget annotations that draw the field.
    /// </summary>
    public List<PdfDictionary> Widgets { get; set; } = [];
}
