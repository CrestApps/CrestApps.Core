using System.Security.Cryptography.X509Certificates;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Creates the HTTP handler that sends an OAuth 2.0 token request with mutual TLS (mTLS) client authentication.
/// Replace this service to control how those requests connect, for example to check the destination address at
/// connect time or to send them through a proxy.
/// </summary>
public interface IOAuth2MtlsHandlerFactory
{
    /// <summary>
    /// Creates a new handler that presents the client certificate. Each call must return a new handler: the caller
    /// sends one token request through it and then disposes it. The handler should not follow redirects, because the
    /// request form carries the client credential.
    /// </summary>
    /// <param name="clientCertificate">The client certificate to present during the TLS handshake.</param>
    /// <returns>A new handler that the caller owns and disposes.</returns>
    HttpMessageHandler CreateHandler(X509Certificate2 clientCertificate);
}
