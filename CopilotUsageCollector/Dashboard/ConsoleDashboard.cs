using Microsoft.Extensions.Logging;

namespace CopilotUsageCollector.Dashboard;

/// <summary>
/// Renders Copilot usage data as an ASCII-art dashboard in the console.
/// </summary>
public static class ConsoleDashboard
{
    public static async Task RunAsync(DashboardDataService dataService, ILogger logger)
    {
        logger.LogInformation("Launching Console dashboard");
        var data = await dataService.GetDashboardDataAsync();

        // Fully clear console including scrollback buffer
        Console.Clear();
        Console.SetCursorPosition(0, 0);
        Console.Write("\x1b[3J");

        var separator = new string('═', 70);

        Console.WriteLine();
        Console.WriteLine(separator);
        Console.WriteLine("  M365 COPILOT USAGE DASHBOARD (Console)");
        Console.WriteLine(separator);
        Console.WriteLine();

        // Summary
        Console.WriteLine("  ┌─────────────────────────────────────────────────┐");
        Console.WriteLine($"  │  Report Date : {data.ReportRefreshDate ?? "N/A",-34}│");
        Console.WriteLine($"  │  Period      : D{data.ReportPeriod,-33}│");
        Console.WriteLine($"  │  Collected   : {data.CollectedDate ?? "N/A",-34}│");
        Console.WriteLine("  ├─────────────────────────────────────────────────┤");
        Console.WriteLine($"  │  Total Users : {data.TotalUsers,-34}│");
        Console.WriteLine($"  │  Active      : {data.ActiveUsers,-34}│");
        Console.WriteLine($"  │  Inactive    : {data.InactiveUsers,-34}│");

        if (data.TotalUsers > 0)
        {
            var pct = (double)data.ActiveUsers / data.TotalUsers * 100;
            Console.WriteLine($"  │  Adoption %  : {pct:F1}%{new string(' ', 32 - $"{pct:F1}%".Length)}│");
        }

        Console.WriteLine("  └─────────────────────────────────────────────────┘");
        Console.WriteLine();

        // Per-app adoption bar chart
        Console.WriteLine("  Per-App Adoption");
        Console.WriteLine("  " + new string('─', 50));

        var maxCount = data.AppAdoption.Values.DefaultIfEmpty(1).Max();
        const int barMaxWidth = 30;

        foreach (var (app, count) in data.AppAdoption.OrderByDescending(kv => kv.Value))
        {
            var barWidth = maxCount > 0 ? (int)((double)count / maxCount * barMaxWidth) : 0;
            var bar = new string('█', barWidth) + new string('░', barMaxWidth - barWidth);
            Console.WriteLine($"  {app,-22} {bar} {count,5}");
        }

        Console.WriteLine();

        // Recent active users
        if (data.RecentActiveUsers.Count > 0)
        {
            Console.WriteLine("  Most Recently Active Users");
            Console.WriteLine("  " + new string('─', 50));
            Console.WriteLine($"  {"Name",-25} {"Last Active",-15}");

            foreach (var user in data.RecentActiveUsers)
            {
                var name = user.DisplayName.Length > 24 ? user.DisplayName[..21] + "..." : user.DisplayName;
                Console.WriteLine($"  {name,-25} {user.LastActivityDate ?? "N/A",-15}");
            }

            Console.WriteLine();
        }

        Console.WriteLine(separator);
        Console.WriteLine($"  Last refreshed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        logger.LogInformation("Console dashboard rendered");
    }
}
