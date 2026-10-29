using CrestApps.Core.AI.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

public sealed class VoiceLibraryViewModel
{
    public string Deployment { get; set; }

    public string Query { get; set; }

    public string Language { get; set; }

    public SpeechVoiceGender? Gender { get; set; }

    public string Cursor { get; set; }

    public IEnumerable<SelectListItem> Deployments { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether a deployment was chosen and its provider has a voice library.
    /// </summary>
    public bool HasLibrary { get; set; }

    public bool SupportsCloning { get; set; }

    public bool HasSearched { get; set; }

    public IReadOnlyList<SpeechVoice> Voices { get; set; } = [];

    public string NextCursor { get; set; }
}
