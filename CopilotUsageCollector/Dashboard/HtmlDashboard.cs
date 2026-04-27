using System.Text;
using Microsoft.Extensions.Logging;

namespace CopilotUsageCollector.Dashboard;

/// <summary>
/// Generates a standalone HTML file with embedded Chart.js visualizations
/// and opens it in the default browser.
/// </summary>
public static class HtmlDashboard
{
    public static async Task RunAsync(DashboardDataService dataService, ILogger logger)
    {
        logger.LogInformation("Generating HTML dashboard");
        var data = await dataService.GetDashboardDataAsync();

        var appLabels = string.Join(", ", data.AppAdoption.Keys.Select(k => $"'{EscapeJs(k)}'"));
        var appValues = string.Join(", ", data.AppAdoption.Values);

        var recentRows = new StringBuilder();
        foreach (var user in data.RecentActiveUsers)
        {
            recentRows.AppendLine($"<tr><td>{Escape(user.DisplayName)}</td><td>{Escape(user.UserPrincipalName)}</td><td>{Escape(user.LastActivityDate ?? "N/A")}</td></tr>");
        }

        var adoptionPct = data.TotalUsers > 0 ? (double)data.ActiveUsers / data.TotalUsers * 100 : 0;

        var html = $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>M365 Copilot Usage Dashboard</title>
            <script src="https://cdn.jsdelivr.net/npm/chart.js@4"></script>
            <style>
                * { margin: 0; padding: 0; box-sizing: border-box; }
                body { font-family: 'Segoe UI', Tahoma, sans-serif; background: #f0f2f5; color: #333; padding: 24px; }
                h1 { text-align: center; margin-bottom: 8px; color: #0078d4; }
                .subtitle { text-align: center; color: #666; margin-bottom: 24px; font-size: 14px; }
                .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 16px; margin-bottom: 24px; }
                .card { background: #fff; border-radius: 8px; padding: 20px; box-shadow: 0 1px 3px rgba(0,0,0,0.12); }
                .card h3 { margin-bottom: 12px; color: #0078d4; font-size: 16px; }
                .stat { font-size: 36px; font-weight: 700; color: #0078d4; }
                .stat-label { font-size: 13px; color: #666; margin-top: 4px; }
                .chart-container { position: relative; height: 350px; }
                table { width: 100%; border-collapse: collapse; font-size: 14px; }
                th, td { text-align: left; padding: 8px 12px; border-bottom: 1px solid #e0e0e0; }
                th { background: #f8f9fa; font-weight: 600; }
                tr:hover { background: #f0f6ff; }
            </style>
        </head>
        <body>
            <h1>M365 Copilot Usage Dashboard</h1>
            <p class="subtitle">
                Report Date: {{data.ReportRefreshDate ?? "N/A"}} &nbsp;|&nbsp;
                Period: D{{data.ReportPeriod}} &nbsp;|&nbsp;
                Collected: {{data.CollectedDate ?? "N/A"}}
            </p>

            <div class="grid">
                <div class="card">
                    <h3>Total Licensed Users</h3>
                    <div class="stat">{{data.TotalUsers}}</div>
                </div>
                <div class="card">
                    <h3>Active Users</h3>
                    <div class="stat" style="color:#28a745">{{data.ActiveUsers}}</div>
                    <div class="stat-label">{{adoptionPct:F1}}% adoption rate</div>
                </div>
                <div class="card">
                    <h3>Inactive Users</h3>
                    <div class="stat" style="color:#dc3545">{{data.InactiveUsers}}</div>
                    <div class="stat-label">Zero Copilot activity in period</div>
                </div>
            </div>

            <div class="grid">
                <div class="card">
                    <h3>Active vs Inactive</h3>
                    <div class="chart-container">
                        <canvas id="pieChart"></canvas>
                    </div>
                </div>
                <div class="card">
                    <h3>Per-App Adoption</h3>
                    <div class="chart-container">
                        <canvas id="barChart"></canvas>
                    </div>
                </div>
            </div>

            <div class="card" style="margin-bottom:24px">
                <h3>Most Recently Active Users</h3>
                <table>
                    <thead><tr><th>Display Name</th><th>UPN</th><th>Last Active</th></tr></thead>
                    <tbody>{{recentRows}}</tbody>
                </table>
            </div>

            <script>
                new Chart(document.getElementById('pieChart'), {
                    type: 'doughnut',
                    data: {
                        labels: ['Active', 'Inactive'],
                        datasets: [{
                            data: [{{data.ActiveUsers}}, {{data.InactiveUsers}}],
                            backgroundColor: ['#28a745', '#dc3545'],
                            borderWidth: 2
                        }]
                    },
                    options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { position: 'bottom' } } }
                });

                new Chart(document.getElementById('barChart'), {
                    type: 'bar',
                    data: {
                        labels: [{{appLabels}}],
                        datasets: [{
                            label: 'Users with Activity',
                            data: [{{appValues}}],
                            backgroundColor: '#0078d4',
                            borderRadius: 4
                        }]
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        indexAxis: 'y',
                        plugins: { legend: { display: false } },
                        scales: { x: { beginAtZero: true } }
                    }
                });
            </script>
            <p style="text-align:center;color:#999;font-size:12px;margin-top:24px">Last refreshed: {{DateTime.Now:yyyy-MM-dd HH:mm:ss}}</p>
        </body>
        </html>
        """;

        var dashboardDir = Path.Combine(AppContext.BaseDirectory, "Dashboard");
        Directory.CreateDirectory(dashboardDir);
        var filePath = Path.Combine(dashboardDir, "CopilotUsageDashboard.html");
        await File.WriteAllTextAsync(filePath, html);
        logger.LogInformation("HTML dashboard written to {FilePath}", filePath);

        // Open in Microsoft Edge
        try
        {
            var edgePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"Microsoft\Edge\Application\msedge.exe");
            if (!File.Exists(edgePath))
            {
                edgePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"Microsoft\Edge\Application\msedge.exe");
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = edgePath,
                Arguments = $"\"{filePath}\"",
                UseShellExecute = false
            };
            System.Diagnostics.Process.Start(psi);
            logger.LogInformation("Opened HTML dashboard in Microsoft Edge");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not open Microsoft Edge. Open manually: {FilePath}", filePath);
        }
    }

    private static string Escape(string s) =>
        System.Net.WebUtility.HtmlEncode(s);

    private static string EscapeJs(string s) =>
        s.Replace("'", "\\'");
}
