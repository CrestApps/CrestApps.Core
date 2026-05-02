using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;

namespace CrestApps.Core.Blazor.Web.ViewModels;

public sealed class AIConnectionViewModel
{
    private const string ApiKeyAuthenticationType = "ApiKey";

    public string ItemId { get; set; }

    public string Name { get; set; }

    public string DisplayText { get; set; }

    public string Source { get; set; }

    public string Endpoint { get; set; }

    public string AuthenticationType { get; set; }

    public string ApiKey { get; set; }

    public bool IsReadOnly { get; set; }

    public List<KeyValuePair<string, string>> Providers { get; set; } = [];

    public List<KeyValuePair<string, string>> AuthenticationTypes { get; set; } = [];

    public static AIConnectionViewModel FromConnection(AIProviderConnection connection)
    {
        var model = new AIConnectionViewModel
        {
            ItemId = connection.ItemId,
            Name = connection.Name,
            DisplayText = connection.DisplayText,
            Source = AIProviderNameNormalizer.Normalize(connection.Source),
            IsReadOnly = connection.IsReadOnly,
        };

        if (connection.Properties != null)
        {
            model.Endpoint = connection.Properties.TryGetValue("Endpoint", out var ep) ? ep?.ToString() : null;
            model.AuthenticationType = connection.Properties.TryGetValue("AuthenticationType", out var auth) ? auth?.ToString() : null;
        }

        return model;
    }

    public void ApplyTo(AIProviderConnection connection)
    {
        connection.Name = Name;
        connection.DisplayText = DisplayText;
        connection.Source = AIProviderNameNormalizer.Normalize(Source);

        connection.Properties ??= new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(Endpoint))
        {
            connection.Properties["Endpoint"] = Endpoint;
        }
        else
        {
            connection.Properties.Remove("Endpoint");
        }

        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            connection.Properties["ApiKey"] = ApiKey;
        }

        if (!string.IsNullOrWhiteSpace(AuthenticationType))
        {
            connection.Properties["AuthenticationType"] = AuthenticationType;
        }
        else
        {
            connection.Properties.Remove("AuthenticationType");
        }
    }

    public bool UsesApiKeyAuthentication()
    {
        return string.Equals(AuthenticationType, ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase);
    }
}
