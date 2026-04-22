namespace CrestApps.Core.Blazor.Web;

internal static class AuthRedirectHelper
{
    public static string NormalizeReturnUrl(string returnUrl)
    {
        if (!IsLocalUrl(returnUrl))
        {
            return "/";
        }

        return returnUrl;
    }

    public static string NormalizeReturnUrl(string returnUrl, HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var absoluteUri) &&
            string.Equals(absoluteUri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(absoluteUri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase) &&
            absoluteUri.Port == request.Host.Port.GetValueOrDefault(absoluteUri.IsDefaultPort ? absoluteUri.Port : -1))
        {
            returnUrl = string.Concat(absoluteUri.AbsolutePath, absoluteUri.Query, absoluteUri.Fragment);
        }

        return NormalizeReturnUrl(returnUrl);
    }

    public static bool IsLocalUrl(string url)
    {
        return !string.IsNullOrEmpty(url) &&
            ((url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))) ||
            (url[0] == '~' && url.Length > 1 && url[1] == '/'));
    }
}
