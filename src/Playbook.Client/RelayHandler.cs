namespace Playbook.Client;

/// <summary>
/// The football data APIs Playbook reads (Sleeper, ESPN, nflverse on GitHub) do not all allow
/// calls from a browser page on another site. Every outgoing request to one of them is sent to
/// the site's relay instead (noahdbuilds.com/playbook/api/relay/{host}/{path}), a Pages Function
/// that makes the same GET from Cloudflare and returns the answer. Requests to any other host
/// are left alone.
/// </summary>
public sealed class RelayHandler : DelegatingHandler
{
    /// <summary>Hosts the relay forwards to. Keep in step with the relay Function's allowlist.</summary>
    public static readonly IReadOnlySet<string> RelayedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "api.sleeper.app",
        "site.api.espn.com",
        "site.web.api.espn.com",
        "github.com",
        "raw.githubusercontent.com",
    };

    private readonly Uri _relayBase;

    public RelayHandler(Uri appBase)
    {
        _relayBase = new Uri(appBase, "api/relay/");
    }

    public static Uri? RelayUri(Uri relayBase, Uri? target)
    {
        if (target is null || !target.IsAbsoluteUri || target.Scheme != Uri.UriSchemeHttps ||
            !RelayedHosts.Contains(target.Host))
        {
            return null;
        }

        return new Uri(relayBase, target.Host + target.PathAndQuery);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if ((request.Method == HttpMethod.Get || request.Method == HttpMethod.Head) &&
            RelayUri(_relayBase, request.RequestUri) is { } relayed)
        {
            request.RequestUri = relayed;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
