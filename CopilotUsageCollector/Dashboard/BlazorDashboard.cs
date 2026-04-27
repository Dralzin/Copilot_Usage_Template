using System.Runtime.InteropServices;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CopilotUsageCollector.Dashboard;

/// <summary>
/// Launches a self-hosted Blazor-style web dashboard using minimal ASP.NET Core.
/// Serves a dynamic HTML page at http://localhost:5050 with live data from SQL.
/// Press Ctrl+C to stop.
/// </summary>
public static class BlazorDashboard
{
    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint uMode);

    public static async Task RunAsync(DashboardDataService dataService, ILogger logger, CancellationToken cancellationToken = default)
    {
        // Suppress Windows error popups (e.g. web server unreachable) on shutdown
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetErrorMode(0x0003); // SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX
        }
        logger.LogInformation("Launching Blazor dashboard at http://localhost:5050");

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.ListenLocalhost(5050));

        var app = builder.Build();

        app.MapGet("/", async (HttpContext ctx) =>
        {
            try
            {
                var data = await dataService.GetDashboardDataAsync();
                var html = BuildHtml(data);
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.WriteAsync(html);
            }
            catch { /* Suppress errors during shutdown */ }
        });

        app.MapGet("/api/data", async (HttpContext ctx) =>
        {
            try
            {
                var data = await dataService.GetDashboardDataAsync();
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsJsonAsync(data);
            }
            catch { /* Suppress errors during shutdown */ }
        });

        app.MapGet("/health", () => Results.Ok());

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
                Arguments = "http://localhost:5050",
                UseShellExecute = false
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { /* ignore if Edge can't be opened */ }

        logger.LogInformation("Blazor dashboard running at http://localhost:5050 — press Ctrl+C to stop");
        try
        {
            await app.RunAsync(cancellationToken);
        }
        catch
        {
            // Suppress all errors on shutdown
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    private static string BuildHtml(DashboardData data)
    {
        var appLabels = string.Join(", ", data.AppAdoption.Keys.Select(k => $"'{EscapeJs(k)}'"));
        var appValues = string.Join(", ", data.AppAdoption.Values);
        var adoptionPct = data.TotalUsers > 0 ? (double)data.ActiveUsers / data.TotalUsers * 100 : 0;

        var recentRows = new StringBuilder();
        foreach (var user in data.RecentActiveUsers)
        {
            recentRows.AppendLine($"<tr><td>{Escape(user.DisplayName)}</td><td>{Escape(user.UserPrincipalName)}</td><td>{Escape(user.LastActivityDate ?? "N/A")}</td></tr>");
        }

        return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>M365 Copilot Usage Dashboard (Blazor)</title>
            <script src="https://cdn.jsdelivr.net/npm/chart.js@4"></script>
            <style>
                * { margin: 0; padding: 0; box-sizing: border-box; }
                body { font-family: 'Segoe UI', Tahoma, sans-serif; background: #1a1a2e; color: #e0e0e0; padding: 24px; }
                h1 { text-align: center; margin-bottom: 8px; color: #00b4d8; }
                .subtitle { text-align: center; color: #999; margin-bottom: 24px; font-size: 14px; }
                .badge { display: inline-block; background: #00b4d8; color: #fff; padding: 2px 10px; border-radius: 12px; font-size: 11px; margin-left: 8px; }
                .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 16px; margin-bottom: 24px; }
                .card { background: #16213e; border-radius: 10px; padding: 20px; box-shadow: 0 2px 8px rgba(0,0,0,0.3); }
                .card h3 { margin-bottom: 12px; color: #00b4d8; font-size: 16px; }
                .stat { font-size: 40px; font-weight: 700; color: #00b4d8; }
                .stat-label { font-size: 13px; color: #999; margin-top: 4px; }
                .chart-container { position: relative; height: 350px; }
                table { width: 100%; border-collapse: collapse; font-size: 14px; }
                th, td { text-align: left; padding: 8px 12px; border-bottom: 1px solid #2a2a4a; }
                th { background: #1a1a3e; font-weight: 600; color: #00b4d8; }
                tr:hover { background: #1e2a4a; }
                .refresh-btn { display: block; margin: 0 auto 24px; padding: 8px 24px; background: #00b4d8; color: #fff; border: none; border-radius: 6px; cursor: pointer; font-size: 14px; }
                .refresh-btn:hover { background: #0096c7; }
                .footer { text-align: center; color: #666; font-size: 12px; margin-top: 24px; }
            </style>
        </head>
        <body>
            <h1>M365 Copilot Usage Dashboard <span class="badge">Blazor</span></h1>
            <p class="subtitle">
                Report Date: {{data.ReportRefreshDate ?? "N/A"}} &nbsp;|&nbsp;
                Period: D{{data.ReportPeriod}} &nbsp;|&nbsp;
                Collected: {{data.CollectedDate ?? "N/A"}}
            </p>

            <button class="refresh-btn" onclick="location.reload()">🔄 Refresh Data</button>

            <div class="grid">
                <div class="card">
                    <h3>Total Licensed Users</h3>
                    <div class="stat">{{data.TotalUsers}}</div>
                </div>
                <div class="card">
                    <h3>Active Users</h3>
                    <div class="stat" style="color:#2ecc71">{{data.ActiveUsers}}</div>
                    <div class="stat-label">{{adoptionPct:F1}}% adoption rate</div>
                </div>
                <div class="card">
                    <h3>Inactive Users</h3>
                    <div class="stat" style="color:#e74c3c">{{data.InactiveUsers}}</div>
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

            <p class="footer">Served by self-hosted ASP.NET Core &nbsp;|&nbsp; <a href="/api/data" style="color:#00b4d8">JSON API</a> &nbsp;|&nbsp; Last refreshed: {{DateTime.Now:yyyy-MM-dd HH:mm:ss}}</p>

            <script>
                Chart.defaults.color = '#ccc';
                Chart.defaults.borderColor = '#2a2a4a';

                new Chart(document.getElementById('pieChart'), {
                    type: 'doughnut',
                    data: {
                        labels: ['Active', 'Inactive'],
                        datasets: [{
                            data: [{{data.ActiveUsers}}, {{data.InactiveUsers}}],
                            backgroundColor: ['#2ecc71', '#e74c3c'],
                            borderWidth: 0
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
                            backgroundColor: '#00b4d8',
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
        </body>
        </html>
        """;
    }

    private static string Escape(string s) =>
        System.Net.WebUtility.HtmlEncode(s);

    private static string EscapeJs(string s) =>
        s.Replace("'", "\\'");
}
