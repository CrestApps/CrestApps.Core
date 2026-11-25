using System.Security.Cryptography.X509Certificates;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default implementation of <see cref="IOAuth2MtlsHandlerFactory"/>. It creates a <see cref="SocketsHttpHandler"/>
/// that presents the client certificate and does not follow redirects, so the token request and the certificate reach
/// only the token endpoint.
/// </summary>
public sealed class DefaultOAuth2MtlsHandlerFactory : IOAuth2MtlsHandlerFactory
{
    /// <summary>
    /// Creates a new <see cref="SocketsHttpHandler"/> that presents the client certificate and does not follow redirects.
    /// </summary>
    /// <param name="clientCertificate">The client certificate to present during the TLS handshake.</param>
    /// <returns>A new handler that the caller owns and disposes.</returns>
    public HttpMessageHandler CreateHandler(X509Certificate2 clientCertificate)
    {
        ArgumentNullException.ThrowIfNull(clientCertificate);

        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            SslOptions =
            {
                ClientCertificates = [clientCertificate],
            },
        };
    }
}
