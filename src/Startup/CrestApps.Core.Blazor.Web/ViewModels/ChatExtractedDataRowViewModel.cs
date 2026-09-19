namespace CrestApps.Core.Blazor.Web.ViewModels;

public sealed class ChatExtractedDataRowViewModel
{
    public DateTime SessionStartedUtc { get; set; }

    public string SessionId { get; set; }

    public IReadOnlyDictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
}
