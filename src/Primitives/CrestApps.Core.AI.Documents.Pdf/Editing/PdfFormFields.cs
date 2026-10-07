using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Reads, fills, adds, changes and removes the interactive form fields of a PDF.
/// </summary>
/// <remarks>
/// Works on the field dictionaries directly rather than through PDFsharp's typed fields, which only cover
/// filling, so reading a field's choices and constraints, creating one, and removing one all see the same
/// model. Every value set is also given an appearance stream, so the filled form looks filled in every
/// viewer — and can be flattened — rather than only in viewers that regenerate appearances themselves.
/// </remarks>
internal static class PdfFormFields
{
    private const int ReadOnlyFlag = 1;
    private const int RequiredFlag = 1 << 1;
    private const int MultilineFlag = 1 << 12;
    private const int PasswordFlag = 1 << 13;
    private const int NoToggleToOffFlag = 1 << 14;
    private const int RadioFlag = 1 << 15;
    private const int PushButtonFlag = 1 << 16;
    private const int ComboFlag = 1 << 17;
    private const int EditFlag = 1 << 18;

    /// <summary>
    /// Reads every field of a document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The terminal fields, in form order.</returns>
    public static List<PdfFormFieldInfo> Read(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var result = new List<PdfFormFieldInfo>();
        var form = PdfLowLevel.GetDictionary(document.Internals.Catalog, "/AcroForm");
        var fields = PdfLowLevel.GetArray(form, "/Fields");

        if (fields is null)
        {
            return result;
        }

        foreach (var item in fields.Elements)
        {
            if (PdfLowLevel.Resolve(item) is PdfDictionary field)
            {
                Collect(document, field, null, result, 0);
            }
        }

        return result;
    }

    /// <summary>
    /// Finds a field by its full name, its own name, or its tooltip, ignoring case.
    /// </summary>
    /// <param name="fields">The fields.</param>
    /// <param name="name">The name.</param>
    /// <returns>The field, or <see langword="null"/>.</returns>
    public static PdfFormFieldInfo Find(List<PdfFormFieldInfo> fields, string name)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();

        return fields.Find(field => string.Equals(field.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? fields.Find(field => string.Equals(field.Name.Split('.')[^1], trimmed, StringComparison.OrdinalIgnoreCase))
            ?? fields.Find(field => string.Equals(field.Tooltip, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? fields.Find(field => string.Equals(Normalize(field.Name), Normalize(trimmed), StringComparison.Ordinal));
    }

    /// <summary>
    /// Sets a field's value.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">The value as text: for a checkbox <c>true</c>/<c>false</c>, for a choice one of its options.</param>
    /// <returns>The value as set.</returns>
    public static string SetValue(PdfDocument document, PdfFormFieldInfo field, string value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(field);

        value ??= string.Empty;

        switch (field.Type)
        {
            case "text":
                {
                    if (field.MaxLength is > 0 && value.Length > field.MaxLength)
                    {
                        throw new PdfToolException($"\"{field.Name}\" takes at most {field.MaxLength} characters; the value has {value.Length}.");
                    }

                    field.Dictionary.Elements["/V"] = CreateString(value);
                    DrawTextAppearance(document, field, value);

                    return value;
                }

            case "checkbox":
                {
                    var on = IsOn(value, field.Options);
                    var onName = field.Options.Count > 0 ? field.Options[0] : "Yes";
                    var state = on ? onName : "Off";

                    field.Dictionary.Elements.SetName("/V", "/" + state);

                    foreach (var widget in field.Widgets)
                    {
                        EnsureCheckAppearance(document, widget, onName);
                        widget.Elements.SetName("/AS", "/" + state);
                    }

                    return on ? "checked" : "unchecked";
                }

            case "radio":
                {
                    var option = MatchOption(field, value)
                        ?? throw new PdfToolException($"\"{value}\" is not one of the choices of \"{field.Name}\": {string.Join(", ", field.Options)}.");

                    field.Dictionary.Elements.SetName("/V", "/" + option);

                    foreach (var widget in field.Widgets)
                    {
                        var states = AppearanceStates(widget);
                        widget.Elements.SetName("/AS", states.Contains(option, StringComparer.Ordinal) ? "/" + option : "/Off");
                    }

                    return option;
                }

            case "dropdown":
            case "listbox":
                {
                    var flags = Flags(field.Dictionary);
                    var option = MatchOption(field, value);

                    if (option is null)
                    {
                        if (field.Type == "listbox" || (flags & EditFlag) == 0 || field.Options.Count > 0 && (flags & EditFlag) == 0)
                        {
                            throw new PdfToolException($"\"{value}\" is not one of the choices of \"{field.Name}\": {string.Join(", ", field.Options)}.");
                        }

                        option = value;
                    }

                    field.Dictionary.Elements["/V"] = CreateString(option);

                    var index = field.Options.FindIndex(candidate => string.Equals(candidate, option, StringComparison.Ordinal));

                    if (index >= 0)
                    {
                        var selected = new PdfArray(document);
                        selected.Elements.Add(new PdfInteger(index));
                        field.Dictionary.Elements["/I"] = selected;
                    }

                    DrawTextAppearance(document, field, DisplayValue(field, option));

                    return option;
                }

            case "signature":
                throw new PdfToolException($"\"{field.Name}\" is a signature field; it is signed with sign_pdf, not filled.");

            default:
                throw new PdfToolException($"\"{field.Name}\" is a button and holds no value.");
        }
    }

    /// <summary>
    /// Adds a field.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="spec">What to add.</param>
    /// <returns>The field's name.</returns>
    public static string Add(PdfDocument document, PdfFormFieldSpec spec)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(spec);

        if (string.IsNullOrWhiteSpace(spec.Name))
        {
            throw new PdfToolException("A new field needs a 'name'.");
        }

        if (Find(Read(document), spec.Name) is { } existing && string.Equals(existing.Name, spec.Name.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new PdfToolException($"A field named \"{existing.Name}\" already exists.");
        }

        var page = ResolvePage(document, spec.Page);
        var form = EnsureAcroForm(document);
        var type = (spec.Type ?? "text").Trim().ToLowerInvariant();
        var width = spec.Width ?? (type is "checkbox" or "radio" ? 12 : 180);
        var height = spec.Height ?? (type switch
        {
            "checkbox" or "radio" => 12,
            "multiline" => 60,
            "listbox" => 60,
            "signature" => 40,
            _ => 20,
        });

        var rectangle = ToRectangle(page, spec.X ?? 72, spec.Y ?? 72, width, height);
        var flags = (spec.Required == true ? RequiredFlag : 0) | (spec.ReadOnly == true ? ReadOnlyFlag : 0);
        var name = spec.Name.Trim();

        switch (type)
        {
            case "text":
            case "multiline":
            case "password":
                {
                    var field = CreateWidgetField(document, page, form, name, rectangle, "/Tx", flags |
                        (type == "multiline" || spec.Multiline == true ? MultilineFlag : 0) |
                        (type == "password" ? PasswordFlag : 0));

                    field.Elements.SetString("/DA", "/Helv " + PdfLowLevel.Format(spec.FontSize ?? 0) + " Tf 0 g");

                    if (spec.MaxLength is > 0)
                    {
                        field.Elements.SetInteger("/MaxLen", spec.MaxLength.Value);
                    }

                    Finish(field, spec);

                    if (!string.IsNullOrEmpty(spec.Value))
                    {
                        SetValue(document, Find(Read(document), name), spec.Value);
                    }

                    break;
                }

            case "checkbox":
                {
                    var field = CreateWidgetField(document, page, form, name, rectangle, "/Btn", flags);
                    var on = !string.IsNullOrEmpty(spec.Value) && IsOn(spec.Value, []);

                    EnsureCheckAppearance(document, field, "Yes");
                    field.Elements.SetName("/V", on ? "/Yes" : "/Off");
                    field.Elements.SetName("/AS", on ? "/Yes" : "/Off");
                    Finish(field, spec);

                    break;
                }

            case "radio":
                AddRadioGroup(document, page, form, name, rectangle, flags, spec);

                break;

            case "dropdown":
            case "combo":
            case "listbox":
                {
                    if (spec.Options is not { Count: > 0 })
                    {
                        throw new PdfToolException($"A {type} needs 'options'.");
                    }

                    var field = CreateWidgetField(document, page, form, name, rectangle, "/Ch", flags | (type == "listbox" ? 0 : ComboFlag) | (spec.Editable == true ? EditFlag : 0));
                    var options = new PdfArray(document);

                    foreach (var option in spec.Options)
                    {
                        options.Elements.Add(CreateString(option));
                    }

                    field.Elements["/Opt"] = options;
                    field.Elements.SetString("/DA", "/Helv " + PdfLowLevel.Format(spec.FontSize ?? 0) + " Tf 0 g");
                    Finish(field, spec);

                    SetValue(document, Find(Read(document), name), string.IsNullOrEmpty(spec.Value) ? spec.Options[0] : spec.Value);

                    break;
                }

            case "signature":
                {
                    var field = CreateWidgetField(document, page, form, name, rectangle, "/Sig", flags);

                    field.Elements["/AP"] = AppearanceDictionary(document, PdfLowLevel.CreateForm(
                        document,
                        rectangle.Width,
                        rectangle.Height,
                        "q 0.6 G 0.75 w 0 0.5 m " + PdfLowLevel.Format(rectangle.Width) + " 0.5 l S Q"));
                    form.Elements.SetInteger("/SigFlags", 3);
                    Finish(field, spec);

                    break;
                }

            default:
                throw new PdfToolException($"'{spec.Type}' is not a field type. Use text, multiline, password, checkbox, radio, dropdown, listbox or signature.");
        }

        return name;
    }

    /// <summary>
    /// Changes a field's properties.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="field">The field.</param>
    /// <param name="spec">The properties to change; unset ones are kept.</param>
    /// <returns>What changed.</returns>
    public static List<string> Update(PdfDocument document, PdfFormFieldInfo field, PdfFormFieldSpec spec)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(spec);

        var changes = new List<string>();
        var dictionary = field.Dictionary;

        if (!string.IsNullOrWhiteSpace(spec.NewName))
        {
            dictionary.Elements["/T"] = CreateString(spec.NewName.Trim());
            changes.Add($"renamed to \"{spec.NewName.Trim()}\"");
        }

        if (spec.Tooltip is not null)
        {
            dictionary.Elements["/TU"] = CreateString(spec.Tooltip);
            changes.Add("tooltip");
        }

        var flags = Flags(dictionary);

        if (spec.Required is { } required)
        {
            flags = required ? flags | RequiredFlag : flags & ~RequiredFlag;
            changes.Add(required ? "required" : "optional");
        }

        if (spec.ReadOnly is { } readOnly)
        {
            flags = readOnly ? flags | ReadOnlyFlag : flags & ~ReadOnlyFlag;
            changes.Add(readOnly ? "read-only" : "editable");
        }

        dictionary.Elements.SetInteger("/Ff", flags);

        if (spec.MaxLength is > 0 && field.Type == "text")
        {
            dictionary.Elements.SetInteger("/MaxLen", spec.MaxLength.Value);
            changes.Add($"max length {spec.MaxLength}");
        }

        if (spec.Options is { Count: > 0 } && field.Type is "dropdown" or "listbox")
        {
            var options = new PdfArray(document);

            foreach (var option in spec.Options)
            {
                options.Elements.Add(CreateString(option));
            }

            dictionary.Elements["/Opt"] = options;
            changes.Add("options");
        }

        if (spec.DefaultValue is not null)
        {
            dictionary.Elements["/DV"] = CreateString(spec.DefaultValue);
            changes.Add("default value");
        }

        return changes;
    }

    /// <summary>
    /// Removes a field and its widgets.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="field">The field.</param>
    public static void Remove(PdfDocument document, PdfFormFieldInfo field)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(field);

        for (var index = 0; index < document.PageCount; index++)
        {
            var annotations = PdfLowLevel.GetAnnotations(document.Pages[index], create: false);

            if (annotations is null)
            {
                continue;
            }

            foreach (var widget in field.Widgets.Append(field.Dictionary))
            {
                var position = PdfLowLevel.IndexOf(annotations, widget);

                if (position >= 0)
                {
                    annotations.Elements.RemoveAt(position);
                }
            }
        }

        var parent = PdfLowLevel.GetDictionary(field.Dictionary, "/Parent");
        var container = parent is null
            ? PdfLowLevel.GetArray(PdfLowLevel.GetDictionary(document.Internals.Catalog, "/AcroForm"), "/Fields")
            : PdfLowLevel.GetArray(parent, "/Kids");
        var at = PdfLowLevel.IndexOf(container, field.Dictionary);

        if (at >= 0)
        {
            container.Elements.RemoveAt(at);
        }
    }

    /// <summary>
    /// Draws the appearance a text or choice field shows its value with.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">The text to show.</param>
    public static void DrawTextAppearance(PdfDocument document, PdfFormFieldInfo field, string value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(field);

        var multiline = field.Multiline || field.Type == "listbox";
        var fontSize = ReadFontSize(field.Dictionary);
        var password = (Flags(field.Dictionary) & PasswordFlag) != 0;
        var shown = password ? new string('*', value?.Length ?? 0) : value ?? string.Empty;

        foreach (var widget in field.Widgets)
        {
            var rectangle = PdfLowLevel.GetRectangle(widget);

            if (rectangle is null)
            {
                continue;
            }

            var width = rectangle.Width;
            var height = rectangle.Height;
            var size = fontSize > 0 ? fontSize : Math.Clamp(multiline ? 10 : height * 0.62, 5, 12);
            var content = new StringBuilder("/Tx BMC q 1 1 ")
                .Append(PdfLowLevel.Format(width - 2)).Append(' ').Append(PdfLowLevel.Format(height - 2)).Append(" re W n BT /Helv ")
                .Append(PdfLowLevel.Format(size)).Append(" Tf 0 g ");

            if (multiline)
            {
                var lines = WrapLines(shown, width - 4, size);
                var y = height - 2 - size;

                content.Append("2 ").Append(PdfLowLevel.Format(y)).Append(" Td ").Append(PdfLowLevel.Format(size * 1.15)).Append(" TL ");

                foreach (var line in lines)
                {
                    content.Append(PdfLowLevel.Literal(line)).Append(" Tj T* ");
                }
            }
            else
            {
                content.Append("2 ").Append(PdfLowLevel.Format((height - (size * 0.72)) / 2)).Append(" Td ").Append(PdfLowLevel.Literal(shown)).Append(" Tj ");
            }

            content.Append("ET Q EMC");

            widget.Elements["/AP"] = AppearanceDictionary(document, PdfLowLevel.CreateForm(document, width, height, content.ToString(), PdfLowLevel.HelveticaResources(document)));
        }

        var form = EnsureAcroForm(document);
        form.Elements.SetBoolean("/NeedAppearances", true);
    }

    /// <summary>
    /// Gets a document's interactive form dictionary, creating it when the document has none.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The form dictionary.</returns>
    public static PdfDictionary EnsureAcroForm(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var form = PdfLowLevel.GetDictionary(document.Internals.Catalog, "/AcroForm");

        if (form is null)
        {
            form = new PdfDictionary(document);
            document.Internals.AddObject(form);
            document.Internals.Catalog.Elements["/AcroForm"] = form.Reference;
        }

        if (PdfLowLevel.GetArray(form, "/Fields") is null)
        {
            form.Elements["/Fields"] = new PdfArray(document);
        }

        var resources = PdfLowLevel.GetDictionary(form, "/DR");

        if (resources is null)
        {
            resources = new PdfDictionary(document);
            form.Elements["/DR"] = resources;
        }

        var fonts = PdfLowLevel.GetDictionary(resources, "/Font");

        if (fonts is null)
        {
            fonts = new PdfDictionary(document);
            resources.Elements["/Font"] = fonts;
        }

        if (!fonts.Elements.ContainsKey("/Helv"))
        {
            fonts.Elements["/Helv"] = PdfLowLevel.Helvetica(document).Reference;
        }

        if (!form.Elements.ContainsKey("/DA"))
        {
            form.Elements.SetString("/DA", "/Helv 0 Tf 0 g");
        }

        return form;
    }

    private static void Collect(PdfDocument document, PdfDictionary field, string parentName, List<PdfFormFieldInfo> result, int depth)
    {
        if (depth > 32)
        {
            return;
        }

        var partial = PdfLowLevel.Text(field.Elements["/T"]);
        var name = string.IsNullOrEmpty(parentName)
            ? partial
            : string.IsNullOrEmpty(partial) ? parentName : parentName + "." + partial;

        var kids = PdfLowLevel.GetArray(field, "/Kids");
        var childFields = new List<PdfDictionary>();
        var widgets = new List<PdfDictionary>();

        foreach (var item in PdfLowLevel.Items(kids))
        {
            if (PdfLowLevel.Resolve(item) is not PdfDictionary kid)
            {
                continue;
            }

            // A kid with its own name is a field; one without is a widget drawing this field.
            if (kid.Elements.ContainsKey("/T"))
            {
                childFields.Add(kid);
            }
            else
            {
                widgets.Add(kid);
            }
        }

        if (childFields.Count > 0)
        {
            foreach (var child in childFields)
            {
                Collect(document, child, name, result, depth + 1);
            }

            return;
        }

        if (widgets.Count == 0 && field.Elements.ContainsKey("/Rect"))
        {
            widgets.Add(field);
        }

        var info = new PdfFormFieldInfo
        {
            Name = name ?? "(unnamed)",
            Dictionary = field,
            Widgets = widgets,
            Tooltip = PdfLowLevel.Text(field.Elements["/TU"]),
        };

        var type = PdfLowLevel.Text(PdfLowLevel.GetInherited(field, "/FT"));
        var flags = Flags(field);

        info.Required = (flags & RequiredFlag) != 0;
        info.ReadOnly = (flags & ReadOnlyFlag) != 0;
        info.Multiline = (flags & MultilineFlag) != 0;

        switch (type)
        {
            case "Tx":
                info.Type = "text";
                info.Value = PdfLowLevel.Text(PdfLowLevel.GetInherited(field, "/V"));
                info.MaxLength = PdfLowLevel.GetInherited(field, "/MaxLen") is { } maxLength ? (int)PdfLowLevel.Number(maxLength) : null;

                break;

            case "Btn":
                if ((flags & PushButtonFlag) != 0)
                {
                    info.Type = "button";
                }
                else if ((flags & RadioFlag) != 0)
                {
                    info.Type = "radio";
                    info.Options = [.. widgets.SelectMany(AppearanceStates).Distinct(StringComparer.Ordinal)];
                    info.Value = NameOrNull(PdfLowLevel.GetInherited(field, "/V"));
                }
                else
                {
                    info.Type = "checkbox";
                    info.Options = [.. widgets.SelectMany(AppearanceStates).Distinct(StringComparer.Ordinal).Take(1)];

                    var state = NameOrNull(PdfLowLevel.GetInherited(field, "/V")) ?? NameOrNull(widgets.Count > 0 ? widgets[0].Elements["/AS"] : null);
                    info.Value = state is null || state == "Off" ? "unchecked" : "checked";
                }

                break;

            case "Ch":
                info.Type = (flags & ComboFlag) != 0 ? "dropdown" : "listbox";
                info.Options = ReadOptions(PdfLowLevel.GetInherited(field, "/Opt") as PdfArray);

                var value = PdfLowLevel.GetInherited(field, "/V");
                info.Value = value is PdfArray selected
                    ? string.Join(", ", selected.Elements.Select(PdfLowLevel.Text))
                    : PdfLowLevel.Text(value);

                break;

            case "Sig":
                info.Type = "signature";
                info.Value = PdfLowLevel.GetInherited(field, "/V") is PdfDictionary ? "signed" : "unsigned";

                break;

            default:
                info.Type = "unknown";

                break;
        }

        info.DefaultValue = PdfLowLevel.Text(PdfLowLevel.GetInherited(field, "/DV"));

        if (widgets.Count > 0)
        {
            info.Rectangle = PdfLowLevel.GetRectangle(widgets[0]);
            info.Page = PdfLowLevel.FindPage(document, widgets[0]);
        }

        result.Add(info);
    }

    private static List<string> ReadOptions(PdfArray options)
    {
        var result = new List<string>();

        foreach (var item in PdfLowLevel.Items(options))
        {
            var resolved = PdfLowLevel.Resolve(item);

            // A choice is either its text, or a pair of the value exported and the text shown.
            result.Add(resolved is PdfArray pair && pair.Elements.Count > 0
                ? PdfLowLevel.Text(pair.Elements[0])
                : PdfLowLevel.Text(resolved));
        }

        return result;
    }

    private static string DisplayValue(PdfFormFieldInfo field, string option)
    {
        var options = PdfLowLevel.GetInherited(field.Dictionary, "/Opt") as PdfArray;

        foreach (var item in PdfLowLevel.Items(options))
        {
            if (PdfLowLevel.Resolve(item) is PdfArray pair && pair.Elements.Count > 1 &&
                string.Equals(PdfLowLevel.Text(pair.Elements[0]), option, StringComparison.Ordinal))
            {
                return PdfLowLevel.Text(pair.Elements[1]);
            }
        }

        return option;
    }

    private static List<string> AppearanceStates(PdfDictionary widget)
    {
        var normal = PdfLowLevel.GetDictionary(PdfLowLevel.GetDictionary(widget, "/AP"), "/N");

        if (normal is null || normal.Stream is not null)
        {
            return [];
        }

        return [.. normal.Elements.Keys.Select(key => key.TrimStart('/')).Where(key => !string.Equals(key, "Off", StringComparison.Ordinal))];
    }

    private static string MatchOption(PdfFormFieldInfo field, string value)
    {
        var trimmed = value.Trim();

        foreach (var option in field.Options)
        {
            if (string.Equals(option, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return option;
            }
        }

        if (field.Type is "dropdown" or "listbox")
        {
            var options = PdfLowLevel.GetInherited(field.Dictionary, "/Opt") as PdfArray;

            foreach (var item in PdfLowLevel.Items(options))
            {
                if (PdfLowLevel.Resolve(item) is PdfArray pair && pair.Elements.Count > 1 &&
                    string.Equals(PdfLowLevel.Text(pair.Elements[1]), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return PdfLowLevel.Text(pair.Elements[0]);
                }
            }
        }

        // A one-based position is accepted too, for a model that numbers the choices it was shown.
        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var position) && position >= 1 && position <= field.Options.Count
            ? field.Options[position - 1]
            : null;
    }

    private static bool IsOn(string value, List<string> onNames)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        return trimmed.ToLowerInvariant() is "true" or "yes" or "on" or "1" or "checked" or "x" or "y" ||
            onNames.Any(name => string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureCheckAppearance(PdfDocument document, PdfDictionary widget, string onName)
    {
        if (AppearanceStates(widget).Count > 0)
        {
            return;
        }

        var rectangle = PdfLowLevel.GetRectangle(widget);

        if (rectangle is null)
        {
            return;
        }

        var width = rectangle.Width;
        var height = rectangle.Height;
        var box = "q 0.4 G 0.75 w 0.5 0.5 " + PdfLowLevel.Format(width - 1) + " " + PdfLowLevel.Format(height - 1) + " re S Q";
        var check = box + " q 0 G " + PdfLowLevel.Format(Math.Max(1, Math.Min(width, height) * 0.12)) + " w 1 J 1 j " +
            PdfLowLevel.Format(width * 0.2) + " " + PdfLowLevel.Format(height * 0.5) + " m " +
            PdfLowLevel.Format(width * 0.42) + " " + PdfLowLevel.Format(height * 0.24) + " l " +
            PdfLowLevel.Format(width * 0.82) + " " + PdfLowLevel.Format(height * 0.78) + " l S Q";

        var states = new PdfDictionary(document);
        states.Elements["/" + onName] = PdfLowLevel.CreateForm(document, width, height, check).Reference;
        states.Elements["/Off"] = PdfLowLevel.CreateForm(document, width, height, box).Reference;

        var appearance = new PdfDictionary(document);
        appearance.Elements["/N"] = states;
        widget.Elements["/AP"] = appearance;
    }

    private static void AddRadioGroup(PdfDocument document, PdfPage page, PdfDictionary form, string name, PdfRectangle first, int flags, PdfFormFieldSpec spec)
    {
        if (spec.Options is not { Count: > 0 })
        {
            throw new PdfToolException("A radio group needs 'options', one per button.");
        }

        var parent = new PdfDictionary(document);
        document.Internals.AddObject(parent);

        parent.Elements.SetName("/FT", "/Btn");
        parent.Elements["/T"] = CreateString(name);
        parent.Elements.SetInteger("/Ff", flags | RadioFlag | NoToggleToOffFlag);
        parent.Elements.SetName("/V", "/Off");

        var kids = new PdfArray(document);
        parent.Elements["/Kids"] = kids;

        var size = first.Width;
        var labels = new StringBuilder();

        for (var index = 0; index < spec.Options.Count; index++)
        {
            var option = SafeName(spec.Options[index]);

            // Buttons are stacked down the page, each with its label printed beside it.
            var bottom = first.Y1 - (index * size * 1.8);
            var rectangle = new PdfRectangle(new PdfSharp.Drawing.XPoint(first.X1, bottom), new PdfSharp.Drawing.XPoint(first.X1 + size, bottom + size));
            var widget = new PdfDictionary(document);
            document.Internals.AddObject(widget);

            widget.Elements.SetName("/Type", "/Annot");
            widget.Elements.SetName("/Subtype", "/Widget");
            widget.Elements["/Rect"] = PdfLowLevel.Numbers(document, rectangle.X1, rectangle.Y1, rectangle.X2, rectangle.Y2);
            widget.Elements["/P"] = page.Reference;
            widget.Elements.SetInteger("/F", 4);
            widget.Elements["/Parent"] = parent.Reference;
            widget.Elements.SetName("/AS", "/Off");

            var radius = size / 2;
            var circle = Circle(radius, radius, radius - 0.75);
            var dot = Circle(radius, radius, radius * 0.45);
            var states = new PdfDictionary(document);

            states.Elements["/" + option] = PdfLowLevel.CreateForm(document, size, size, "q 0.4 G 0.75 w " + circle + " S Q q 0 g " + dot + " f Q").Reference;
            states.Elements["/Off"] = PdfLowLevel.CreateForm(document, size, size, "q 0.4 G 0.75 w " + circle + " S Q").Reference;

            var appearance = new PdfDictionary(document);
            appearance.Elements["/N"] = states;
            widget.Elements["/AP"] = appearance;

            kids.Elements.Add(widget.Reference);
            PdfLowLevel.GetAnnotations(page, create: true).Elements.Add(widget.Reference);

            labels.Append("BT /Helv 10 Tf 0 g ").Append(PdfLowLevel.Format(rectangle.X2 + 4)).Append(' ').Append(PdfLowLevel.Format(rectangle.Y1 + 1.5)).Append(" Td ")
                .Append(PdfLowLevel.Literal(spec.Options[index])).Append(" Tj ET ");
        }

        Finish(parent, spec);
        PdfLowLevel.GetArray(form, "/Fields").Elements.Add(parent.Reference);

        // The labels are printed into the page so each button says what it means.
        var fonts = PdfLowLevel.GetDictionary(page.Resources, "/Font");

        if (fonts is null)
        {
            fonts = new PdfDictionary(document);
            page.Resources.Elements["/Font"] = fonts;
        }

        if (!fonts.Elements.ContainsKey("/Helv"))
        {
            fonts.Elements["/Helv"] = PdfLowLevel.Helvetica(document).Reference;
        }

        PdfLowLevel.AppendIsolated(page, labels.ToString());

        if (!string.IsNullOrEmpty(spec.Value))
        {
            SetValue(document, Find(Read(document), name), spec.Value);
        }
    }

    private static string Circle(double cx, double cy, double r)
    {
        // Four Bézier arcs approximate a circle; 0.5523 is the standard control-point distance.
        var k = r * 0.5523;

        return string.Join(' ',
            PdfLowLevel.Format(cx + r), PdfLowLevel.Format(cy), "m",
            PdfLowLevel.Format(cx + r), PdfLowLevel.Format(cy + k), PdfLowLevel.Format(cx + k), PdfLowLevel.Format(cy + r), PdfLowLevel.Format(cx), PdfLowLevel.Format(cy + r), "c",
            PdfLowLevel.Format(cx - k), PdfLowLevel.Format(cy + r), PdfLowLevel.Format(cx - r), PdfLowLevel.Format(cy + k), PdfLowLevel.Format(cx - r), PdfLowLevel.Format(cy), "c",
            PdfLowLevel.Format(cx - r), PdfLowLevel.Format(cy - k), PdfLowLevel.Format(cx - k), PdfLowLevel.Format(cy - r), PdfLowLevel.Format(cx), PdfLowLevel.Format(cy - r), "c",
            PdfLowLevel.Format(cx + k), PdfLowLevel.Format(cy - r), PdfLowLevel.Format(cx + r), PdfLowLevel.Format(cy - k), PdfLowLevel.Format(cx + r), PdfLowLevel.Format(cy), "c");
    }

    private static PdfDictionary CreateWidgetField(PdfDocument document, PdfPage page, PdfDictionary form, string name, PdfRectangle rectangle, string fieldType, int flags)
    {
        var field = new PdfDictionary(document);
        document.Internals.AddObject(field);

        field.Elements.SetName("/FT", fieldType);
        field.Elements["/T"] = CreateString(name);
        field.Elements.SetInteger("/Ff", flags);
        field.Elements.SetName("/Type", "/Annot");
        field.Elements.SetName("/Subtype", "/Widget");
        field.Elements["/Rect"] = PdfLowLevel.Numbers(document, rectangle.X1, rectangle.Y1, rectangle.X2, rectangle.Y2);
        field.Elements["/P"] = page.Reference;
        field.Elements.SetInteger("/F", 4);

        var characteristics = new PdfDictionary(document);
        characteristics.Elements["/BC"] = PdfLowLevel.Numbers(document, 0.6, 0.6, 0.6);
        characteristics.Elements["/BG"] = PdfLowLevel.Numbers(document, 1, 1, 1);
        field.Elements["/MK"] = characteristics;

        PdfLowLevel.GetArray(form, "/Fields").Elements.Add(field.Reference);
        PdfLowLevel.GetAnnotations(page, create: true).Elements.Add(field.Reference);

        return field;
    }

    private static void Finish(PdfDictionary field, PdfFormFieldSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(spec.Tooltip))
        {
            field.Elements["/TU"] = CreateString(spec.Tooltip);
        }

        if (spec.DefaultValue is not null)
        {
            field.Elements["/DV"] = CreateString(spec.DefaultValue);
        }
    }

    private static PdfDictionary AppearanceDictionary(PdfDocument document, PdfDictionary normal)
    {
        var appearance = new PdfDictionary(document);
        appearance.Elements["/N"] = normal.Reference;

        return appearance;
    }

    private static PdfPage ResolvePage(PdfDocument document, int? page)
    {
        var number = page ?? 1;

        if (number < 1 || number > document.PageCount)
        {
            throw new PdfToolException($"Page {number} does not exist; the document has {document.PageCount} page(s).");
        }

        return document.Pages[number - 1];
    }

    /// <summary>
    /// Converts a box given from the top-left of a page's visible area into user space.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="x">The distance from the left edge, in points.</param>
    /// <param name="y">The distance from the top edge, in points.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The rectangle in user space.</returns>
    public static PdfRectangle ToRectangle(PdfPage page, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(page);

        var visible = page.EffectiveCropBoxReadOnly;
        var left = visible.X1 + x;
        var top = visible.Y2 - y;

        return new PdfRectangle(new PdfSharp.Drawing.XPoint(left, top - height), new PdfSharp.Drawing.XPoint(left + width, top));
    }

    private static int Flags(PdfDictionary field)
    {
        return (int)PdfLowLevel.Number(PdfLowLevel.GetInherited(field, "/Ff"));
    }

    private static double ReadFontSize(PdfDictionary field)
    {
        var appearance = PdfLowLevel.Text(PdfLowLevel.GetInherited(field, "/DA"));

        if (string.IsNullOrWhiteSpace(appearance))
        {
            return 0;
        }

        var parts = appearance.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tf = Array.IndexOf(parts, "Tf");

        return tf >= 1 && double.TryParse(parts[tf - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
            ? size
            : 0;
    }

    private static List<string> WrapLines(string text, double width, double size)
    {
        var lines = new List<string>();
        var average = size * 0.5;
        var perLine = Math.Max(1, (int)(width / average));

        foreach (var paragraph in (text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = new StringBuilder();

            foreach (var word in paragraph.Split(' '))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > perLine)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }

                if (line.Length > 0)
                {
                    line.Append(' ');
                }

                line.Append(word);
            }

            lines.Add(line.ToString());
        }

        return lines;
    }

    private static PdfString CreateString(string value)
    {
        // Text outside ASCII is stored as Unicode so a name like "Zoë" survives a round trip.
        return (value ?? string.Empty).All(character => character < 128)
            ? new PdfString(value ?? string.Empty)
            : new PdfString(value, PdfStringEncoding.Unicode);
    }

    private static string NameOrNull(PdfItem item)
    {
        return PdfLowLevel.Resolve(item) switch
        {
            PdfName name => name.Value.TrimStart('/'),
            PdfString text => text.Value,
            _ => null,
        };
    }

    private static string SafeName(string value)
    {
        var builder = new StringBuilder();

        foreach (var character in value ?? string.Empty)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_');
        }

        return builder.Length == 0 ? "Choice" : builder.ToString();
    }

    private static string Normalize(string name)
    {
        return new string([.. (name ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
    }
}
