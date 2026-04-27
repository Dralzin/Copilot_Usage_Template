using Microsoft.Identity.Client;

namespace CopilotUsageCollector.Services;

/// <summary>
/// Provides MSAL with an HttpClient that uses the corporate proxy with default credentials.
/// </summary>
public class ProxyAwareMsalHttpClientFactory : IMsalHttpClientFactory
{
    private readonly HttpClientHandler _handler;

    public ProxyAwareMsalHttpClientFactory(HttpClientHandler handler)
    {
        _handler = handler;
    }

    public HttpClient GetHttpClient()
    {
        return new HttpClient(_handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
    }
}
