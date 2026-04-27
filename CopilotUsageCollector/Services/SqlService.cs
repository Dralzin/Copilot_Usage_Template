using System.Data;
using System.Globalization;
using CopilotUsageCollector.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CopilotUsageCollector.Services;

public class SqlService
{
    private readonly ILogger<SqlService> _logger;
    private readonly string _tableName;

    public SqlService(ILogger<SqlService> logger, string tableName)
    {
        _logger = logger;
        _tableName = tableName;
    }

    public async Task<SqlConnection> OpenConnectionAsync(string connectionString)
    {
        // First connect to master to ensure the target database exists
        var builder = new SqlConnectionStringBuilder(connectionString);
        var targetDatabase = builder.InitialCatalog;

        if (!string.IsNullOrWhiteSpace(targetDatabase))
        {
            builder.InitialCatalog = "master";
            using var masterConnection = new SqlConnection(builder.ConnectionString);
            await masterConnection.OpenAsync();
            _logger.LogInformation("Checking if database [{Database}] exists on {Server}", targetDatabase, masterConnection.DataSource);

            var checkDbSql = $"""
                IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = @DbName)
                BEGIN
                    CREATE DATABASE [{targetDatabase}];
                END
                """;
            using var cmd = new SqlCommand(checkDbSql, masterConnection);
            cmd.Parameters.AddWithValue("@DbName", targetDatabase);
            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("Database [{Database}] is ready", targetDatabase);
        }

        // Now connect to the target database
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        _logger.LogInformation("SQL connection established (Server: {Server}, Database: {Database})",
            connection.DataSource, connection.Database);
        return connection;
    }

    public async Task EnsureTableExistsAsync(SqlConnection connection)
    {
        _logger.LogInformation("Ensuring [{TableName}] table exists", _tableName);

        string ddl = $"""
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = @TableName)
            BEGIN
                CREATE TABLE [dbo].[{_tableName}] (
                    [ReportRefreshDate]                     DATE            NOT NULL,
                    [UserPrincipalName]                     NVARCHAR(320)   NOT NULL,
                    [DisplayName]                           NVARCHAR(256)   NULL,
                    [LastActivityDate]                      DATE            NULL,
                    [CopilotChatLastActivityDate]           DATE            NULL,
                    [CopilotChatWorkLastActivityDate]       DATE            NULL,
                    [CopilotChatWebLastActivityDate]        DATE            NULL,
                    [TeamsCopilotLastActivityDate]          DATE            NULL,
                    [WordCopilotLastActivityDate]           DATE            NULL,
                    [ExcelCopilotLastActivityDate]          DATE            NULL,
                    [PowerPointCopilotLastActivityDate]     DATE            NULL,
                    [OutlookCopilotLastActivityDate]        DATE            NULL,
                    [OneNoteCopilotLastActivityDate]        DATE            NULL,
                    [LoopCopilotLastActivityDate]           DATE            NULL,
                    [M365AppCopilotLastActivityDate]        DATE            NULL,
                    [EdgeCopilotLastActivityDate]           DATE            NULL,
                    [AgentLastActivityDate]                 DATE            NULL,
                    [ReportPeriod]                          INT             NULL,
                    [CollectedDate]                         DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT [PK_{_tableName}] PRIMARY KEY CLUSTERED
                        ([ReportRefreshDate], [UserPrincipalName])
                );
            END
            """;

        using var cmd = new SqlCommand(ddl, connection);
        cmd.Parameters.AddWithValue("@TableName", _tableName);
        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("Table check complete");
    }

    public async Task EnsureViewsExistAsync(SqlConnection connection)
    {
        _logger.LogInformation("Ensuring database views exist");

        string viewDdl = $"""
            IF NOT EXISTS (SELECT * FROM sys.views WHERE name = 'CopilotInactiveUsers')
            BEGIN
                EXEC('
                CREATE VIEW [dbo].[CopilotInactiveUsers] AS
                SELECT TOP (10000) [ReportRefreshDate]
                      ,[UserPrincipalName]
                      ,[DisplayName]
                      ,[LastActivityDate]
                      ,[CopilotChatLastActivityDate]
                      ,[CopilotChatWorkLastActivityDate]
                      ,[CopilotChatWebLastActivityDate]
                      ,[TeamsCopilotLastActivityDate]
                      ,[WordCopilotLastActivityDate]
                      ,[ExcelCopilotLastActivityDate]
                      ,[PowerPointCopilotLastActivityDate]
                      ,[OutlookCopilotLastActivityDate]
                      ,[OneNoteCopilotLastActivityDate]
                      ,[LoopCopilotLastActivityDate]
                      ,[M365AppCopilotLastActivityDate]
                      ,[EdgeCopilotLastActivityDate]
                      ,[AgentLastActivityDate]
                      ,[ReportPeriod]
                      ,[CollectedDate]
                  FROM [dbo].[{_tableName}]
                  WHERE [LastActivityDate] IS NULL
                    AND [CopilotChatLastActivityDate] IS NULL
                    AND [CopilotChatWorkLastActivityDate] IS NULL
                    AND [CopilotChatWebLastActivityDate] IS NULL
                    AND [EdgeCopilotLastActivityDate] IS NULL
                    AND [ExcelCopilotLastActivityDate] IS NULL
                    AND [LoopCopilotLastActivityDate] IS NULL
                    AND [M365AppCopilotLastActivityDate] IS NULL
                    AND [OneNoteCopilotLastActivityDate] IS NULL
                    AND [OutlookCopilotLastActivityDate] IS NULL
                    AND [PowerPointCopilotLastActivityDate] IS NULL
                    AND [TeamsCopilotLastActivityDate] IS NULL
                    AND [WordCopilotLastActivityDate] IS NULL
                ')
            END
            """;

        using var cmd = new SqlCommand(viewDdl, connection);
        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("View check complete");
    }

    public async Task<int> ImportDataAsync(
        SqlConnection connection,
        List<CopilotUsageRecord> data,
        string period)
    {
        if (data.Count == 0)
        {
            _logger.LogWarning("No data to import");
            return 0;
        }

        _logger.LogInformation("Importing {Count} records into [{TableName}] (period: {Period})", data.Count, _tableName, period);

        int periodInt = int.Parse(period.Replace("D", ""), CultureInfo.InvariantCulture);

        // Clear existing data
        _logger.LogInformation("Truncating existing data in [{TableName}]", _tableName);
        string truncateSql = $"DELETE FROM [dbo].[{_tableName}]";
        using (var cmd = new SqlCommand(truncateSql, connection) { CommandTimeout = 600 })
        {
            var deleted = await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("Deleted {Deleted} existing rows from [{TableName}]", deleted, _tableName);
        }

        // Build DataTable
        var dataTable = BuildDataTable(data, periodInt);

        // Bulk insert new data
        _logger.LogInformation("Bulk loading {Count} rows into [{TableName}]", dataTable.Rows.Count, _tableName);
        using (var bulkCopy = new SqlBulkCopy(connection))
        {
            bulkCopy.DestinationTableName = $"[dbo].[{_tableName}]";
            bulkCopy.BatchSize = 5000;
            bulkCopy.BulkCopyTimeout = 600;

            foreach (DataColumn col in dataTable.Columns)
            {
                bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            }

            await bulkCopy.WriteToServerAsync(dataTable);
        }

        _logger.LogInformation("Import complete — {Count} rows inserted into [{TableName}]", dataTable.Rows.Count, _tableName);
        return dataTable.Rows.Count;
    }

    private static DataTable BuildDataTable(List<CopilotUsageRecord> data, int periodInt)
    {
        var dt = new DataTable();
        dt.Columns.Add("ReportRefreshDate", typeof(DateTime));
        dt.Columns.Add("UserPrincipalName", typeof(string));
        dt.Columns.Add("DisplayName", typeof(string));
        dt.Columns.Add("LastActivityDate", typeof(DateTime));
        dt.Columns.Add("CopilotChatLastActivityDate", typeof(DateTime));
        dt.Columns.Add("CopilotChatWorkLastActivityDate", typeof(DateTime));
        dt.Columns.Add("CopilotChatWebLastActivityDate", typeof(DateTime));
        dt.Columns.Add("TeamsCopilotLastActivityDate", typeof(DateTime));
        dt.Columns.Add("WordCopilotLastActivityDate", typeof(DateTime));
        dt.Columns.Add("ExcelCopilotLastActivityDate", typeof(DateTime));
        dt.Columns.Add("PowerPointCopilotLastActivityDate", typeof(DateTime));
        dt.Columns.Add("OutlookCopilotLastActivityDate", typeof(DateTime));
        dt.Columns.Add("OneNoteCopilotLastActivityDate", typeof(DateTime));
        dt.Columns.Add("LoopCopilotLastActivityDate", typeof(DateTime));
        dt.Columns.Add("M365AppCopilotLastActivityDate", typeof(DateTime));
        dt.Columns.Add("EdgeCopilotLastActivityDate", typeof(DateTime));
        dt.Columns.Add("AgentLastActivityDate", typeof(DateTime));
        dt.Columns.Add("ReportPeriod", typeof(int));

        foreach (DataColumn col in dt.Columns)
            col.AllowDBNull = true;

        foreach (var record in data)
        {
            var row = dt.NewRow();
            row["ReportRefreshDate"] = ParseDate(record.ReportRefreshDate);
            row["UserPrincipalName"] = (object?)record.UserPrincipalName ?? DBNull.Value;
            row["DisplayName"] = (object?)record.DisplayName ?? DBNull.Value;
            row["LastActivityDate"] = ParseDate(record.LastActivityDate);
            row["CopilotChatLastActivityDate"] = ParseDate(record.CopilotChatLastActivityDate);
            row["CopilotChatWorkLastActivityDate"] = ParseDate(record.CopilotChatWorkLastActivityDate);
            row["CopilotChatWebLastActivityDate"] = ParseDate(record.CopilotChatWebLastActivityDate);
            row["TeamsCopilotLastActivityDate"] = ParseDate(record.TeamsCopilotLastActivityDate);
            row["WordCopilotLastActivityDate"] = ParseDate(record.WordCopilotLastActivityDate);
            row["ExcelCopilotLastActivityDate"] = ParseDate(record.ExcelCopilotLastActivityDate);
            row["PowerPointCopilotLastActivityDate"] = ParseDate(record.PowerPointCopilotLastActivityDate);
            row["OutlookCopilotLastActivityDate"] = ParseDate(record.OutlookCopilotLastActivityDate);
            row["OneNoteCopilotLastActivityDate"] = ParseDate(record.OneNoteCopilotLastActivityDate);
            row["LoopCopilotLastActivityDate"] = ParseDate(record.LoopCopilotLastActivityDate);
            row["M365AppCopilotLastActivityDate"] = ParseDate(record.M365AppCopilotLastActivityDate);
            row["EdgeCopilotLastActivityDate"] = ParseDate(record.EdgeCopilotLastActivityDate);
            row["AgentLastActivityDate"] = ParseDate(record.AgentLastActivityDate);
            row["ReportPeriod"] = periodInt;
            dt.Rows.Add(row);
        }

        return dt;
    }

    private static object ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DBNull.Value;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;
        return DBNull.Value;
    }
}
