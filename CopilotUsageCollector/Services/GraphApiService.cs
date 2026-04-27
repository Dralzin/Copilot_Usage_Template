using System.Net;
using System.Text.Json;
using CopilotUsageCollector.Models;
using Microsoft.Extensions.Logging;

namespace CopilotUsageCollector.Services;

public class GraphApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GraphApiService> _logger;
    private const int MaxRetries = 3;

    public GraphApiService(HttpClient httpClient, ILogger<GraphApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<CopilotUsageRecord>> GetCopilotUsageDataAsync(string accessToken, string period)
    {
        // Beta endpoint — requires Reports.Read.All and ReportSettings.Read.All with admin consent
        // Use $top=999 to maximize page size and reduce round-trips
        var uri = $"https://graph.microsoft.com/beta/reports/getMicrosoft365CopilotUsageUserDetail(period='{period}')?$top=999";
        _logger.LogInformation("Graph API URI: {Uri}", uri);
        var allResults = new List<CopilotUsageRecord>();
        string? currentUri = uri;

        while (currentUri != null)
        {
            var response = await CallWithRetryAsync(currentUri, accessToken);

            if (response?.Value != null)
            {
                allResults.AddRange(response.Value);
            }

            currentUri = response?.NextLink;
        }

        _logger.LogInformation("Retrieved {Count} usage records from Graph API", allResults.Count);
        return allResults;
    }

    private async Task<GraphApiResponse?> CallWithRetryAsync(string uri, string accessToken)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("Calling Graph API (attempt {Attempt}): {Uri}", attempt, uri);

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Graph API returned HTTP {StatusCode} — not retryable. Response: {Body}",
                        (int)response.StatusCode, body);
                    throw new InvalidOperationException(
                        $"Graph API authorization error (HTTP {(int)response.StatusCode}). " +
                        "Ensure the Entra ID app registration has the following APPLICATION permissions " +
                        "(not delegated) with admin consent granted: " +
                        "Reports.Read.All and ReportSettings.Read.All. " +
                        $"Response: {body}");
                }

                if (IsRetryableStatusCode(response.StatusCode) && attempt < MaxRetries)
                {
                    var backoffSeconds = GetBackoffSeconds(response, attempt);
                    _logger.LogWarning("HTTP {StatusCode} — retrying in {Backoff}s (attempt {Attempt} of {Max})",
                        (int)response.StatusCode, backoffSeconds, attempt, MaxRetries);
                    await Task.Delay(TimeSpan.FromSeconds(backoffSeconds));
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Graph API returned HTTP {StatusCode}. Response body: {Body}",
                        (int)response.StatusCode, errorBody);
                    throw new InvalidOperationException(
                        $"Graph API error (HTTP {(int)response.StatusCode}): {errorBody}");
                }

                var stream = await response.Content.ReadAsStreamAsync();
                return await JsonSerializer.DeserializeAsync<GraphApiResponse>(stream);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                LogRetryableError(ex, attempt, "Request timed out");
                if (attempt >= MaxRetries)
                    throw new InvalidOperationException(
                        $"Graph API request timed out after {MaxRetries} retries.", ex);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                LogRetryableError(ex, attempt, FormatHttpError(ex));
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (HttpRequestException ex) when (attempt >= MaxRetries)
            {
                _logger.LogError(ex, "Graph API call failed after {Max} attempts. {Detail}",
                    MaxRetries, FormatHttpError(ex));
                throw new InvalidOperationException(
                    $"Graph API request failed after {MaxRetries} retries. Last error: {FormatHttpError(ex)}", ex);
            }
        }

        // Should not reach here, but satisfy compiler
        throw new InvalidOperationException($"Graph API request failed after {MaxRetries} retries.");
    }

    private void LogRetryableError(Exception ex, int attempt, string detail)
    {
        var backoff = Math.Pow(2, attempt);
        _logger.LogWarning(ex, "Error on attempt {Attempt} of {Max} — retrying in {Backoff}s. {Detail}",
            attempt, MaxRetries, backoff, detail);
    }

    private static string FormatHttpError(HttpRequestException ex)
    {
        var parts = new List<string>();
        if (ex.StatusCode.HasValue) parts.Add($"StatusCode={ex.StatusCode}");
        parts.Add(ex.Message);
        if (ex.InnerException != null) parts.Add($"Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        return string.Join(" | ", parts);
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests ||
        statusCode == HttpStatusCode.ServiceUnavailable ||
        statusCode == HttpStatusCode.GatewayTimeout;

    private static double GetBackoffSeconds(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
            return delta.TotalSeconds;
        if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
            return Math.Max(1, (date - DateTimeOffset.UtcNow).TotalSeconds);
        return Math.Pow(2, attempt);
    }
}
