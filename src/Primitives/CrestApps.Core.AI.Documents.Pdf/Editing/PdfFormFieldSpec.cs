namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// A form field to add, or the properties of one to change.
/// </summary>
internal sealed class PdfFormFieldSpec
{
    /// <summary>
    /// Gets or sets the action: <c>add</c>, <c>update</c> or <c>remove</c>.
    /// </summary>
    public string Action { get; set; }

    /// <summary>
    /// Gets or sets the field type of a new field: <c>text</c>, <c>multiline</c>, <c>password</c>,
    /// <c>checkbox</c>, <c>radio</c>, <c>dropdown</c>, <c>listbox</c> or <c>signature</c>.
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the field's name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the name an update renames the field to.
    /// </summary>
    public string NewName { get; set; }

    /// <summary>
    /// Gets or sets the one-based page a new field goes on.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets the distance of a new field from the left edge, in points.
    /// </summary>
    public double? X { get; set; }

    /// <summary>
    /// Gets or sets the distance of a new field from the top edge, in points.
    /// </summary>
    public double? Y { get; set; }

    /// <summary>
    /// Gets or sets the width, in points.
    /// </summary>
    public double? Width { get; set; }

    /// <summary>
    /// Gets or sets the height, in points.
    /// </summary>
    public double? Height { get; set; }

    /// <summary>
    /// Gets or sets the choices of a dropdown, list box or radio group.
    /// </summary>
    public List<string> Options { get; set; }

    /// <summary>
    /// Gets or sets the initial value.
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// Gets or sets the value a reset restores.
    /// </summary>
    public string DefaultValue { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the field must be filled.
    /// </summary>
    public bool? Required { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the field cannot be changed.
    /// </summary>
    public bool? ReadOnly { get; set; }

    /// <summary>
    /// Gets or sets the tooltip, read by assistive technology as the field's label.
    /// </summary>
    public string Tooltip { get; set; }

    /// <summary>
    /// Gets or sets the most characters a text field takes.
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Gets or sets the font size; 0 or unset sizes the text to the field.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a text field takes several lines.
    /// </summary>
    public bool? Multiline { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a dropdown also accepts typed text.
    /// </summary>
    public bool? Editable { get; set; }
}
