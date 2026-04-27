using CopilotUsageCollector.Services;
using CopilotUsageCollector.Dashboard;
using CopilotUsageCollector.Properties;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

// Hide the console window for non-Console dashboard modes
var dashboardModeEarly = Resources.DashboardMode ?? "None";
if (!string.Equals(dashboardModeEarly, "Console", StringComparison.OrdinalIgnoreCase))
{
    HideConsoleWindow();
}

static void HideConsoleWindow()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        var handle = GetConsoleWindow();
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, 0); // SW_HIDE = 0

            // Remove from taskbar by clearing WS_EX_APPWINDOW and setting WS_EX_TOOLWINDOW
            const int GWL_EXSTYLE = -20;
            const int WS_EX_APPWINDOW = 0x00040000;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            var style = GetWindowLong(handle, GWL_EXSTYLE);
            style = (style & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW;
            SetWindowLong(handle, GWL_EXSTYLE, style);
        }
    }
}

[DllImport("kernel32.dll")]
static extern IntPtr GetConsoleWindow();

[DllImport("user32.dll")]
static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

[DllImport("user32.dll")]
static extern int GetWindowLong(IntPtr hWnd, int nIndex);

[DllImport("user32.dll")]
static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

[DllImport("kernel32.dll")]
static extern bool FreeConsole();

// Read configuration from Resources.resx
var tenantId = Resources.TenantId;
var clientId = Resources.ClientId;
var clientSecret = Resources.ClientSecret;
var sqlConnectionString = Resources.SqlConnectionString;
var databaseName = Resources.DatabaseName;
var tableName = Resources.TableName;
var period = Resources.Period;
var logPath = string.IsNullOrWhiteSpace(Resources.LogPath) ? null : Resources.LogPath;

    // When InTesting is false, override local settings with remote settings
    var inTesting = !string.Equals(Resources.InTesting, "false", StringComparison.OrdinalIgnoreCase);
    if (!inTesting)
    {
        if (!string.IsNullOrWhiteSpace(Resources.RemoteSqlConnectionString))
            sqlConnectionString = Resources.RemoteSqlConnectionString;
        if (!string.IsNullOrWhiteSpace(Resources.RemoteDatabaseName))
            databaseName = Resources.RemoteDatabaseName;
    }

    // Validate configuration values
    if (string.IsNullOrWhiteSpace(tenantId) || tenantId.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
    {
        return 1;
    }
    if (string.IsNullOrWhiteSpace(clientId) || clientId.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
    {
        return 1;
    }
    if (string.IsNullOrWhiteSpace(clientSecret) || clientSecret.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
    {
        return 1;
    }
    if (string.IsNullOrWhiteSpace(sqlConnectionString) || sqlConnectionString.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
    {
        return 1;
    }
    if (!Guid.TryParse(tenantId, out _))
    {
        return 1;
    }
    if (!Guid.TryParse(clientId, out _))
    {
        return 1;
    }
    if (string.IsNullOrWhiteSpace(tableName) || tableName.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
    {
        return 1;
    }
    if (period is not ("D7" or "D30" or "D90" or "D180"))
    {
        return 1;
    }

    // Inject optional database name (and optional SQL credentials) into connection string
    var connBuilder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(sqlConnectionString);
    if (!string.IsNullOrWhiteSpace(databaseName) && !databaseName.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
    {
        connBuilder.InitialCatalog = databaseName;
    }

    var sqlUsername = !inTesting ? Resources.RemoteSqlUsername : Resources.SqlUsername;
    var sqlPassword = !inTesting ? Resources.RemoteSqlPassword : Resources.SqlPassword;
    if (!string.IsNullOrWhiteSpace(sqlUsername))
    {
        connBuilder.UserID = sqlUsername;
        connBuilder.Password = sqlPassword ?? string.Empty;
        connBuilder.IntegratedSecurity = false;
    }

    sqlConnectionString = connBuilder.ConnectionString;

    var dashboardMode = Resources.DashboardMode ?? "None";

    // Parse refresh interval (0 = run once and exit)
    if (!double.TryParse(Resources.RefreshIntervalHours, System.Globalization.CultureInfo.InvariantCulture, out var refreshIntervalHours))
        refreshIntervalHours = 0;

    return await RunAsync(tenantId, clientId, clientSecret, sqlConnectionString, tableName, period, logPath, dashboardMode, refreshIntervalHours);

// ── Main orchestration

static async Task<int> RunAsync(string tenantId, string clientId, string clientSecret,
    string sqlConnectionString, string tableName, string period, string? logPath, string dashboardMode,
    double refreshIntervalHours)
{
    using var loggerFactory = CreateLoggerFactory(logPath, dashboardMode);
    var logger = loggerFactory.CreateLogger("CopilotUsageCollector");

    var runContinuously = refreshIntervalHours > 0;

    if (runContinuously)
    {
        logger.LogInformation("Running in continuous mode — refresh every {Hours} hour(s). Press Ctrl+C to stop.", refreshIntervalHours);
    }

    // Set up cancellation for graceful shutdown
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        logger.LogInformation("Shutdown requested — finishing current cycle...");
    };

    var isDashboardEnabled = !string.Equals(dashboardMode, "None", StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(dashboardMode);
    var isBlazorMode = string.Equals(dashboardMode, "Blazor", StringComparison.OrdinalIgnoreCase);
    var isConsoleMode = string.Equals(dashboardMode, "Console", StringComparison.OrdinalIgnoreCase);
    var isHtmlMode = string.Equals(dashboardMode, "Html", StringComparison.OrdinalIgnoreCase);

    // Only create the dashboard data service when a dashboard is enabled
    DashboardDataService? dashboardDataService = isDashboardEnabled
        ? new DashboardDataService(sqlConnectionString, tableName, logger)
        : null;

    // Launch Blazor dashboard in the background (if configured in continuous mode) — it runs for the lifetime of the process
    Task? blazorTask = null;
    if (isBlazorMode && runContinuously)
    {
        blazorTask = Task.Run(() => BlazorDashboard.RunAsync(dashboardDataService!, logger, cts.Token), cts.Token);
    }

    var cycleCount = 0;

    do
    {
        cycleCount++;
        Microsoft.Data.SqlClient.SqlConnection? sqlConnection = null;

        try
        {
            if (runContinuously)
                logger.LogInformation("═══ Collection cycle #{Cycle} starting ═══", cycleCount);

            logger.LogInformation("═══════════════════════════════════════════════════════════════");
            logger.LogInformation("M365 Copilot Usage Collector (C#) — Starting");
            logger.LogInformation("Period: {Period} | Tenant: {TenantId}", period, tenantId);
            logger.LogInformation("═══════════════════════════════════════════════════════════════");

            // Create a proxy-aware handler for corporate environments
            var handler = new HttpClientHandler
            {
                UseProxy = true,
                Proxy = System.Net.WebRequest.DefaultWebProxy,
                DefaultProxyCredentials = System.Net.CredentialCache.DefaultCredentials
            };

            // Step 1: Authenticate
            logger.LogInformation("Step 1/4: Authenticating to Microsoft Graph");
            var msalHttpClientFactory = new ProxyAwareMsalHttpClientFactory(handler);
            var authService = new GraphAuthService(loggerFactory.CreateLogger<GraphAuthService>(), msalHttpClientFactory);
            var accessToken = await authService.AcquireTokenAsync(tenantId, clientId, clientSecret);

            // Step 2: Retrieve data
            logger.LogInformation("Step 2/4: Retrieving Copilot usage data");
            using var httpClient = new HttpClient(handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromMinutes(5)
            };

            // Pre-flight connectivity check
            try
            {
                logger.LogInformation("Verifying connectivity to graph.microsoft.com...");
                using var probe = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/$metadata");
                using var probeResponse = await httpClient.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead);
                logger.LogInformation("Connectivity OK (HTTP {StatusCode})", (int)probeResponse.StatusCode);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cannot reach graph.microsoft.com. Check network/proxy/firewall. Inner: {Inner}",
                    ex.InnerException?.Message ?? "(none)");
                if (!runContinuously) return 1;
                logger.LogWarning("Will retry in {Hours} hour(s)...", refreshIntervalHours);
                goto WaitForNextCycle;
            }

            var graphService = new GraphApiService(httpClient, loggerFactory.CreateLogger<GraphApiService>());
            var usageData = await graphService.GetCopilotUsageDataAsync(accessToken, period);

            if (usageData.Count == 0)
            {
                logger.LogWarning("No usage data returned from Graph API for period {Period}", period);
                logger.LogWarning("This may indicate no Copilot licenses are assigned or no activity in this period");
                if (!runContinuously) return 0;
                goto WaitForNextCycle;
            }

            // Step 3: Connect to SQL
            logger.LogInformation("Step 3/4: Connecting to SQL Server");
            var sqlService = new SqlService(loggerFactory.CreateLogger<SqlService>(), tableName);
            sqlConnection = await sqlService.OpenConnectionAsync(sqlConnectionString);

            // Step 4: Initialize table and import
            await sqlService.EnsureTableExistsAsync(sqlConnection);
            await sqlService.EnsureViewsExistAsync(sqlConnection);

            logger.LogInformation("Step 4/4: Importing data to SQL");
            var rowCount = await sqlService.ImportDataAsync(sqlConnection, usageData, period);

            logger.LogInformation("═══════════════════════════════════════════════════════════════");
            logger.LogInformation("Collection complete");
            logger.LogInformation("  Records retrieved : {Count}", usageData.Count);
            logger.LogInformation("  Rows inserted     : {RowCount}", rowCount);
            logger.LogInformation("═══════════════════════════════════════════════════════════════");

            // Launch/refresh dashboard — Console refreshes every cycle, others launch once
            if (isDashboardEnabled && dashboardDataService != null)
            {
                if (isConsoleMode)
                {
                    await ConsoleDashboard.RunAsync(dashboardDataService, logger);
                }
                else if (cycleCount == 1)
                {
                    logger.LogInformation("Launching {DashboardMode} dashboard", dashboardMode);

                    if (isHtmlMode)
                    {
                        await HtmlDashboard.RunAsync(dashboardDataService, logger);
                    }
                    else if (isBlazorMode && !runContinuously)
                    {
                        // Single-run mode: launch Blazor and block
                        await BlazorDashboard.RunAsync(dashboardDataService, logger, cts.Token);
                    }
                    else if (!isBlazorMode)
                    {
                        logger.LogWarning("Unknown DashboardMode '{Mode}'. Valid options: None, Console, Html, Blazor", dashboardMode);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError("FATAL: {Message}", ex.Message);
            logger.LogError("Stack: {StackTrace}", ex.StackTrace);
            if (!runContinuously) return 1;
        }
        finally
        {
            if (sqlConnection is { State: System.Data.ConnectionState.Open })
            {
                await sqlConnection.CloseAsync();
                await sqlConnection.DisposeAsync();
                logger.LogInformation("SQL connection closed");
            }
        }

    WaitForNextCycle:
        if (runContinuously && !cts.Token.IsCancellationRequested)
        {
            var nextRun = DateTime.Now.AddHours(refreshIntervalHours);
            logger.LogInformation("Next collection at {NextRun:yyyy-MM-dd HH:mm:ss} ({Hours}h from now)", nextRun, refreshIntervalHours);

            try
            {
                await Task.Delay(TimeSpan.FromHours(refreshIntervalHours), cts.Token);
            }
            catch (TaskCanceledException)
            {
                // Ctrl+C pressed during wait
            }
        }

    } while (runContinuously && !cts.Token.IsCancellationRequested);

    if (blazorTask != null)
    {
        logger.LogInformation("Stopping Blazor dashboard...");
        cts.Cancel();
        try
        {
            await blazorTask;
        }
        catch
        {
            // Suppress all errors on shutdown
        }
    }

    logger.LogInformation("Application stopped.");

    // Close the console window when the program stops
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        FreeConsole();
    }

    return 0;
}

static ILoggerFactory CreateLoggerFactory(string? logPath, string dashboardMode)
{
    var builder = LoggerFactory.Create(b =>
    {
        b.SetMinimumLevel(LogLevel.Information);
    });

    // If a log path is specified, add a file logger via a simple provider
    if (!string.IsNullOrWhiteSpace(logPath))
    {
        var factory = builder;
        var fileProvider = new FileLoggerProvider(logPath);
        factory.AddProvider(fileProvider);
        return factory;
    }

    return builder;
}

// ── Simple file logger ──────────────────────────────────────────────────────

sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    public FileLoggerProvider(string path) => _path = path;
    public ILogger CreateLogger(string categoryName) => new FileLogger(_path, categoryName);
    public void Dispose() { }
}

sealed class FileLogger : ILogger
{
    private readonly string _path;
    private readonly string _category;
    private static readonly object Lock = new();

    public FileLogger(string path, string category) { _path = path; _category = category; }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var level = logLevel switch
        {
            LogLevel.Warning => "WARNING",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "ERROR",
            _ => "INFO"
        };
        var entry = $"[{timestamp}] [{level}] {formatter(state, exception)}";
        lock (Lock)
        {
            File.AppendAllText(_path, entry + Environment.NewLine);
        }
    }
}
