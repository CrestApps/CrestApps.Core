using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// The theme presets a new deck can start from.
/// </summary>
/// <remarks>
/// A model asked for "something modern" does better choosing a name than inventing twelve colours that have
/// to work together, and the fonts named here are ones PowerPoint ships on every desktop, so a deck never
/// opens with a substitute typeface.
/// </remarks>
public static class PresentationThemePresets
{
    /// <summary>
    /// The name of the preset a deck gets when none is asked for.
    /// </summary>
    public const string DefaultName = "office";

    /// <summary>
    /// Gets every preset, in the order they are offered.
    /// </summary>
    public static IReadOnlyList<PresentationThemePreset> All { get; } =
    [
        Create("office", "The familiar PowerPoint look: blue and orange accents on white, Calibri.", "Calibri Light", "Calibri", false,
            "000000", "FFFFFF", "44546A", "E7E6E6", "4472C4", "ED7D31", "A5A5A5", "FFC000", "5B9BD5", "70AD47", "0563C1", "954F72"),
        Create("modern", "Clean and contemporary: slate text, bright blue and violet accents, Segoe UI.", "Segoe UI Semibold", "Segoe UI", false,
            "1F2937", "FFFFFF", "111827", "F3F4F6", "2563EB", "7C3AED", "0EA5E9", "10B981", "F59E0B", "EF4444", "2563EB", "7C3AED"),
        Create("corporate", "Conservative and trustworthy: navy and steel blue, Calibri.", "Calibri", "Calibri", false,
            "1B1B1B", "FFFFFF", "1F3864", "E8EEF7", "1F4E79", "2E75B6", "C55A11", "7F7F7F", "548235", "BF9000", "2E75B6", "7030A0"),
        Create("dark", "Light text on a deep navy background with luminous accents, Segoe UI.", "Segoe UI Semibold", "Segoe UI", true,
            "0F172A", "F8FAFC", "1E293B", "CBD5E1", "38BDF8", "A78BFA", "34D399", "FBBF24", "F472B6", "FB923C", "38BDF8", "A78BFA"),
        Create("vibrant", "Bold and energetic: magenta, purple and electric blue, Century Gothic.", "Century Gothic", "Century Gothic", false,
            "222222", "FFFFFF", "3A0CA3", "F1F0FF", "F72585", "7209B7", "4361EE", "4CC9F0", "FF9E00", "3A0CA3", "4361EE", "7209B7"),
        Create("minimal", "Monochrome with a single red accent, Arial.", "Arial", "Arial", false,
            "262626", "FFFFFF", "404040", "F2F2F2", "404040", "C00000", "7F7F7F", "A6A6A6", "595959", "D9D9D9", "C00000", "595959"),
        Create("nature", "Earthy greens and gold, Georgia headings.", "Georgia", "Calibri", false,
            "1E2A1E", "FFFFFF", "2D4A2D", "EEF3E8", "4E7D3A", "8DAA4B", "C8A951", "5B8C85", "A0522D", "6B8E23", "4E7D3A", "5B8C85"),
        Create("warm", "Terracotta, amber and brick on cream, Cambria headings.", "Cambria", "Calibri", false,
            "3B2F2F", "FFFFFF", "5C3D2E", "FBF3EC", "C0504D", "E36C09", "F2A541", "9C5B3E", "D99694", "7F6000", "C0504D", "9C5B3E"),
        Create("ocean", "Calm blues and teals, Segoe UI Light headings.", "Segoe UI Light", "Segoe UI", false,
            "0B2545", "FFFFFF", "13315C", "EEF4ED", "1B98E0", "247BA0", "00A6A6", "70A9A1", "F4A259", "5B8E7D", "1B98E0", "247BA0"),
    ];

    /// <summary>
    /// Finds a preset by name.
    /// </summary>
    /// <param name="name">The preset's name.</param>
    /// <param name="preset">The preset, when one has that name.</param>
    /// <returns><see langword="true"/> when a preset has the name.</returns>
    public static bool TryGet(string name, out PresentationThemePreset preset)
    {
        preset = null;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                preset = candidate;

                return true;
            }
        }

        return false;
    }

    private static PresentationThemePreset Create(
        string name,
        string description,
        string headingFont,
        string bodyFont,
        bool dark,
        params string[] colors)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < PresentationTheme.ColorSlots.Count; index++)
        {
            map[PresentationTheme.ColorSlots[index]] = colors[index];
        }

        return new PresentationThemePreset
        {
            Name = name,
            Description = description,
            Colors = map,
            HeadingFont = headingFont,
            BodyFont = bodyFont,
            DarkBackground = dark,
        };
    }
}
