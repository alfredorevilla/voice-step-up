namespace StepUp.Api.Auth;

/// <summary>
/// The HTTP verifier carries biometric audio. Plain http is accepted only on loopback or to a single-label host name
/// (a service on the compose network); anything else must use https.
/// </summary>
public static class VerifierEndpointGuard
{
    public static Uri Require(string? baseAddress)
    {
        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Verifier:Http:BaseAddress must be an absolute http or https URL.");
        }

        var onPrivateNetwork = uri.IsLoopback || (uri.HostNameType == UriHostNameType.Dns && !uri.Host.Contains('.', StringComparison.Ordinal));
        if (uri.Scheme == Uri.UriSchemeHttp && !onPrivateNetwork)
        {
            throw new InvalidOperationException("Verifier:Http:BaseAddress must use https unless it is loopback or a compose service name.");
        }

        return uri;
    }
}
