namespace CrestApps.Core.Azure.DocumentIntelligence;

/// <summary>
/// Configures the Azure AI Document Intelligence reader.
/// </summary>
public sealed class DocumentIntelligenceOptions
{
    /// <summary>
    /// Gets or sets the service endpoint. Leaving it empty leaves the reader unregistered, so the local
    /// readers keep serving every document.
    /// </summary>
    public string Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the API key. Leave it empty to authenticate with the ambient Azure credential instead,
    /// which is the better choice wherever managed identity is available.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the model the reader analyzes with. The layout model is what reports paragraph roles,
    /// reading order, tables and captioned figures.
    /// </summary>
    public string ModelId { get; set; } = "prebuilt-layout";

    /// <summary>
    /// Gets or sets the file extensions the reader takes over. Anything else keeps its local reader.
    /// </summary>
    public IList<string> Extensions { get; set; } = [".pdf"];

    /// <summary>
    /// Gets or sets a value indicating whether the figure images are downloaded from the service. Turning
    /// this off keeps the captions and the layout but leaves figures without bytes, so nothing can show or
    /// transcribe them.
    /// </summary>
    public bool IncludeFigureContent { get; set; } = true;

    /// <summary>
    /// Gets or sets the largest document, in pages, the reader will send. The service is priced per page, so
    /// a long document is better served locally than billed in full. Set to <c>0</c> to remove the cap.
    /// </summary>
    public int MaxPages { get; set; }

    /// <summary>
    /// Gets or sets how long to wait for one analysis before giving up and letting the local reader take over.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Determines whether the options carry enough information to reach the service.
    /// </summary>
    /// <returns><see langword="true"/> when an endpoint is configured.</returns>
    public bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(Endpoint);
    }
}
