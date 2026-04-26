namespace CrestApps.Core.AI.Chat.Services;

/// <summary>
/// Represents the extracted Field Change.
/// </summary>
public sealed record ExtractedFieldChange(string FieldName, string Value, bool IsMultiple);
