namespace CrestApps.Core.AI.Models;

public static class ChatDocumentsOptionsExtensions
{
    public static string GetAllowedFileExtensionsAcceptValue(this ChatDocumentsOptions options)
    {
        return BuildAcceptValue(options?.AllowedFileExtensions);
    }

    public static string GetAllowedFileExtensionsDisplayValue(this ChatDocumentsOptions options)
    {
        return BuildDisplayValue(options?.AllowedFileExtensions);
    }

    public static string GetEmbeddableFileExtensionsAcceptValue(this ChatDocumentsOptions options)
    {
        return BuildAcceptValue(options?.EmbeddableFileExtensions);
    }

    public static string GetEmbeddableFileExtensionsDisplayValue(this ChatDocumentsOptions options)
    {
        return BuildDisplayValue(options?.EmbeddableFileExtensions);
    }

    private static string BuildAcceptValue(IEnumerable<string> extensions)
    {
        return string.Join(',', OrderExtensions(extensions));
    }

    private static string BuildDisplayValue(IEnumerable<string> extensions)
    {
        return string.Join(", ", OrderExtensions(extensions));
    }

    private static string[] OrderExtensions(IEnumerable<string> extensions)
    {
        return (extensions ?? []).Where(extension => !string.IsNullOrWhiteSpace(extension)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}