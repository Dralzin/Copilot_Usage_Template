using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CopilotUsageCollector.Dashboard;

/// <summary>
/// Shared data queries used by all dashboard implementations.
/// </summary>
public class DashboardDataService
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly ILogger _logger;

    public DashboardDataService(string connectionString, string tableName, ILogger logger)
    {
        _connectionString = connectionString;
        _tableName = tableName;
        _logger = logger;
    }

    public async Task<DashboardData> GetDashboardDataAsync()
    {
        _logger.LogInformation("Querying dashboard data from [{TableName}]", _tableName);

        var data = new DashboardData();

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Total users and active/inactive counts
        data.TotalUsers = await GetScalarIntAsync(connection,
            $"SELECT COUNT(*) FROM [dbo].[{_tableName}]");
        data.InactiveUsers = await GetScalarIntAsync(connection,
            $"""
            SELECT COUNT(*) FROM [dbo].[{_tableName}]
            WHERE [CopilotChatLastActivityDate] IS NULL
              AND [CopilotChatWorkLastActivityDate] IS NULL
              AND [CopilotChatWebLastActivityDate] IS NULL
              AND [TeamsCopilotLastActivityDate] IS NULL
              AND [WordCopilotLastActivityDate] IS NULL
              AND [ExcelCopilotLastActivityDate] IS NULL
              AND [PowerPointCopilotLastActivityDate] IS NULL
              AND [OutlookCopilotLastActivityDate] IS NULL
              AND [OneNoteCopilotLastActivityDate] IS NULL
              AND [LoopCopilotLastActivityDate] IS NULL
              AND [M365AppCopilotLastActivityDate] IS NULL
              AND [EdgeCopilotLastActivityDate] IS NULL
              AND [AgentLastActivityDate] IS NULL
            """);
        data.ActiveUsers = data.TotalUsers - data.InactiveUsers;

        // Per-app adoption counts
        data.AppAdoption = await GetAppAdoptionAsync(connection);

        // Report metadata
        data.ReportRefreshDate = await GetScalarStringAsync(connection,
            $"SELECT TOP 1 CONVERT(VARCHAR, [ReportRefreshDate], 23) FROM [dbo].[{_tableName}] ORDER BY [ReportRefreshDate] DESC");
        data.ReportPeriod = await GetScalarIntAsync(connection,
            $"SELECT TOP 1 [ReportPeriod] FROM [dbo].[{_tableName}]");
        data.CollectedDate = await GetScalarStringAsync(connection,
            $"SELECT TOP 1 CONVERT(VARCHAR, [CollectedDate], 120) FROM [dbo].[{_tableName}] ORDER BY [CollectedDate] DESC");

        // Top 10 most recently active users
        data.RecentActiveUsers = await GetRecentActiveUsersAsync(connection);

        _logger.LogInformation("Dashboard data loaded: {Total} total, {Active} active, {Inactive} inactive",
            data.TotalUsers, data.ActiveUsers, data.InactiveUsers);

        return data;
    }

    private async Task<Dictionary<string, int>> GetAppAdoptionAsync(SqlConnection connection)
    {
        var apps = new Dictionary<string, int>();
        var columns = new (string Column, string Label)[]
        {
            ("CopilotChatLastActivityDate", "Copilot Chat"),
            ("CopilotChatWorkLastActivityDate", "Copilot Chat (Work)"),
            ("CopilotChatWebLastActivityDate", "Copilot Chat (Web)"),
            ("TeamsCopilotLastActivityDate", "Teams"),
            ("WordCopilotLastActivityDate", "Word"),
            ("ExcelCopilotLastActivityDate", "Excel"),
            ("PowerPointCopilotLastActivityDate", "PowerPoint"),
            ("OutlookCopilotLastActivityDate", "Outlook"),
            ("OneNoteCopilotLastActivityDate", "OneNote"),
            ("LoopCopilotLastActivityDate", "Loop"),
            ("M365AppCopilotLastActivityDate", "M365 App"),
            ("EdgeCopilotLastActivityDate", "Edge"),
            ("AgentLastActivityDate", "Agents"),
        };

        foreach (var (column, label) in columns)
        {
            var count = await GetScalarIntAsync(connection,
                $"SELECT COUNT(*) FROM [dbo].[{_tableName}] WHERE [{column}] IS NOT NULL");
            apps[label] = count;
        }

        return apps;
    }

    private async Task<List<RecentUser>> GetRecentActiveUsersAsync(SqlConnection connection)
    {
        var users = new List<RecentUser>();
        var sql = $"""
            SELECT TOP 10 [DisplayName], [UserPrincipalName], [LastActivityDate]
            FROM [dbo].[{_tableName}]
            WHERE [LastActivityDate] IS NOT NULL
            ORDER BY [LastActivityDate] DESC
            """;

        using var cmd = new SqlCommand(sql, connection);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(new RecentUser
            {
                DisplayName = reader.IsDBNull(0) ? "(unknown)" : reader.GetString(0),
                UserPrincipalName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                LastActivityDate = reader.IsDBNull(2) ? null : reader.GetDateTime(2).ToString("yyyy-MM-dd")
            });
        }

        return users;
    }

    private async Task<int> GetScalarIntAsync(SqlConnection connection, string sql)
    {
        using var cmd = new SqlCommand(sql, connection);
        var result = await cmd.ExecuteScalarAsync();
        return result is int i ? i : 0;
    }

    private async Task<string?> GetScalarStringAsync(SqlConnection connection, string sql)
    {
        using var cmd = new SqlCommand(sql, connection);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }
}

public class DashboardData
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int InactiveUsers { get; set; }
    public Dictionary<string, int> AppAdoption { get; set; } = new();
    public string? ReportRefreshDate { get; set; }
    public int ReportPeriod { get; set; }
    public string? CollectedDate { get; set; }
    public List<RecentUser> RecentActiveUsers { get; set; } = new();
}

public class RecentUser
{
    public string DisplayName { get; set; } = "";
    public string UserPrincipalName { get; set; } = "";
    public string? LastActivityDate { get; set; }
}
