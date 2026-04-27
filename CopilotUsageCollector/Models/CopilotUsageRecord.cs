using System.Text.Json.Serialization;

namespace CopilotUsageCollector.Models;

/// <summary>
/// Represents a single user record from the Graph API
/// GET /beta/reports/getMicrosoft365CopilotUsageUserDetail response.
/// </summary>
public class CopilotUsageRecord
{
    [JsonPropertyName("reportRefreshDate")]
    public string? ReportRefreshDate { get; set; }

    [JsonPropertyName("userPrincipalName")]
    public string? UserPrincipalName { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("lastActivityDate")]
    public string? LastActivityDate { get; set; }

    [JsonPropertyName("copilotChatLastActivityDate")]
    public string? CopilotChatLastActivityDate { get; set; }

    [JsonPropertyName("copilotChatWorkLastActivityDate")]
    public string? CopilotChatWorkLastActivityDate { get; set; }

    [JsonPropertyName("copilotChatWebLastActivityDate")]
    public string? CopilotChatWebLastActivityDate { get; set; }

    [JsonPropertyName("teamsCopilotLastActivityDate")]
    public string? TeamsCopilotLastActivityDate { get; set; }

    [JsonPropertyName("wordCopilotLastActivityDate")]
    public string? WordCopilotLastActivityDate { get; set; }

    [JsonPropertyName("excelCopilotLastActivityDate")]
    public string? ExcelCopilotLastActivityDate { get; set; }

    [JsonPropertyName("powerPointCopilotLastActivityDate")]
    public string? PowerPointCopilotLastActivityDate { get; set; }

    [JsonPropertyName("outlookCopilotLastActivityDate")]
    public string? OutlookCopilotLastActivityDate { get; set; }

    [JsonPropertyName("oneNoteCopilotLastActivityDate")]
    public string? OneNoteCopilotLastActivityDate { get; set; }

    [JsonPropertyName("loopCopilotLastActivityDate")]
    public string? LoopCopilotLastActivityDate { get; set; }

    [JsonPropertyName("m365AppCopilotLastActivityDate")]
    public string? M365AppCopilotLastActivityDate { get; set; }

    [JsonPropertyName("edgeCopilotLastActivityDate")]
    public string? EdgeCopilotLastActivityDate { get; set; }

    [JsonPropertyName("agentLastActivityDate")]
    public string? AgentLastActivityDate { get; set; }
}

/// <summary>
/// Wrapper for the OData JSON response with pagination.
/// </summary>
public class GraphApiResponse
{
    [JsonPropertyName("value")]
    public List<CopilotUsageRecord>? Value { get; set; }

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; set; }
}
