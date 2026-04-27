using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace CopilotUsageCollector.Services;

public class GraphAuthService
{
    private readonly ILogger<GraphAuthService> _logger;
    private readonly IMsalHttpClientFactory? _httpClientFactory;

    public GraphAuthService(ILogger<GraphAuthService> logger, IMsalHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> AcquireTokenAsync(string tenantId, string clientId, string clientSecret)
    {
        _logger.LogInformation("Acquiring OAuth token for tenant {TenantId}", tenantId);

        var builder = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId);

        if (_httpClientFactory != null)
        {
            builder = builder.WithHttpClientFactory(_httpClientFactory);
        }

        var app = builder.Build();

        try
        {
            var result = await app.AcquireTokenForClient(
                new[] { "https://graph.microsoft.com/.default" })
                .ExecuteAsync();

            _logger.LogInformation("Token acquired successfully (expires {ExpiresOn})", result.ExpiresOn);
            return result.AccessToken;
        }
        catch (MsalServiceException ex)
        {
            _logger.LogError("Authentication failed: {Message} (StatusCode: {StatusCode})",
                ex.Message, ex.StatusCode);
            throw new InvalidOperationException(
                $"Failed to acquire Graph API token. Verify TenantId, ClientId, and ClientSecret. MSAL error: {ex.Message}", ex);
        }
    }
}
