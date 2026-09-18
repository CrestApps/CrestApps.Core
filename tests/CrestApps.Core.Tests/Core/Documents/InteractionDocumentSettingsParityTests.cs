using System.Reflection;
using CrestApps.Core.AI.Documents.Models;

namespace CrestApps.Core.Tests.Core.Documents;

/// <summary>
/// Checks that every stored interaction-document setting has somewhere to land on the options type.
/// </summary>
/// <remarks>
/// <para>
/// Two settings were added to <see cref="InteractionDocumentSettings"/> and read back through an
/// <c>IOptionsMonitor&lt;InteractionDocumentSettings&gt;</c>, but nothing configures that type. The host
/// maps the stored settings onto <see cref="InteractionDocumentOptions"/>, and that is the type the
/// container actually populates, so the service kept reading a freshly constructed default.
/// </para>
/// <para>
/// The symptom is quiet: the settings page saves, reloads and shows the new value, while uploads keep
/// obeying the compiled-in default. A document over the configured ceiling was accepted and indexed,
/// and the refusal message quoted 50000 rather than whatever the site had been set to.
/// </para>
/// </remarks>
public sealed class InteractionDocumentSettingsParityTests
{
    [Fact]
    public void EverySetting_HasAMatchingOption()
    {
        var options = typeof(InteractionDocumentOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(property => property.Name, property => property.PropertyType);

        var missing = typeof(InteractionDocumentSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(setting => !options.TryGetValue(setting.Name, out var optionType) || optionType != setting.PropertyType)
            .Select(setting => $"{setting.PropertyType.Name} {setting.Name}")
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{nameof(InteractionDocumentOptions)} is missing a matching property for: {string.Join(", ", missing)}. " +
            "A setting with no option is never read, because the host maps the stored settings onto the options type.");
    }

    [Fact]
    public void TheTwoUploadCeilings_CarryTheSameDefaults()
    {
        var settings = new InteractionDocumentSettings();
        var options = new InteractionDocumentOptions();

        // A mismatch here means an unconfigured host silently enforces a different ceiling than the
        // settings page presents as its default.
        Assert.Equal(settings.MaxIndexableCharacters, options.MaxIndexableCharacters);
        Assert.Equal(settings.DescribeFiguresInUploads, options.DescribeFiguresInUploads);
    }
}
