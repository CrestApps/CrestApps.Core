namespace CrestApps.Core.Blazor.Web.ViewModels;

public sealed class ChatExtractedDataIndexViewModel
{
    public string ProfileId { get; set; }

    public DateTime? StartDateUtc { get; set; }

    public DateTime? EndDateUtc { get; set; }

    public IReadOnlyList<SelectOption> Profiles { get; set; } = [];

    public IReadOnlyList<string> Columns { get; set; } = [];

    public IReadOnlyList<ChatExtractedDataRowViewModel> Rows { get; set; } = [];

    public bool ShowReport { get; set; }
}

public sealed class ChatExtractedDataRowViewModel
{
    public DateTime SessionStartedUtc { get; set; }

    public string SessionId { get; set; }

    public IReadOnlyDictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
}
