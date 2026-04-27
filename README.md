# M365 Copilot Usage Collector

A .NET 8 console application that automates collection of Microsoft 365 Copilot user-level usage data via the Microsoft Graph API and persists it to a SQL Server database. It can run once or continuously in the background on a configurable interval, and optionally launches a dashboard to visualize the collected data.

## How It Works

The program runs as a five-step pipeline:

1. **Read configuration** — All settings (tenant ID, client credentials, SQL connection string, report period, etc.) are read from the embedded `Properties/Resources.resx` file at startup. The `InTesting` flag controls whether local or remote SQL settings are used: when `InTesting` is `true` (default), local settings apply; when `false`, the remote variants (`RemoteSqlConnectionString`, `RemoteDatabaseName`, `RemoteSqlUsername`, `RemoteSqlPassword`) override their local counterparts. The values are validated; if any required value is missing or invalid the program exits immediately with a descriptive error.

2. **Authenticate to Microsoft Graph** — Uses [MSAL](https://learn.microsoft.com/en-us/entra/msal/) (`ConfidentialClientApplication`) to acquire an OAuth 2.0 access token via the client-credentials flow. A proxy-aware HTTP handler is used so the program works behind corporate proxies. A lightweight connectivity probe to `graph.microsoft.com` is executed before the real call so network issues surface early.

3. **Retrieve Copilot usage data** — Calls the Microsoft Graph Beta endpoint:
   ```
   GET /beta/reports/getMicrosoft365CopilotUsageUserDetail(period='{period}')?$top=999
   ```
   The response is paginated; the program follows every `@odata.nextLink` until all pages are consumed. Transient errors (HTTP 429 Too Many Requests, 503 Service Unavailable, 504 Gateway Timeout) are retried up to 3 times with exponential backoff. The `Retry-After` header is honoured when present.

4. **Import into SQL Server** — Connects to the configured SQL Server instance, auto-creates the target database and table if they don't exist, then bulk-loads all records with `SqlBulkCopy`. Existing rows are deleted before insert so each run represents a full refresh of the configured report period. A `CopilotInactiveUsers` view is also created to surface users with zero Copilot activity.

5. **Launch dashboard** *(optional)* — If `DashboardMode` is set to `Console`, `Html`, or `Blazor`, a dashboard is launched after the first successful data import to visualize the collected data.

**Continuous mode:** When `RefreshIntervalHours` is set to a value greater than `0`, the program runs continuously in the background, repeating steps 1–4 at the configured interval. Press **Ctrl+C** for a graceful shutdown. In continuous mode with the Blazor dashboard, the web server runs for the lifetime of the process and refreshes show updated data.

```
┌─────────────┐     ┌──────────────┐     ┌──────────────┐     ┌────────────┐     ┌────────────┐
│ Resources   │────▶│ MSAL Auth    │────▶│ Graph API    │────▶│ SQL Server │────▶│ Dashboard  │
│ .resx       │     │ (OAuth 2.0)  │     │ (paginated)  │     │ (bulk load)│     │ (optional) │
└─────────────┘     └──────────────┘     └──────────────┘     └────────────┘     └────────────┘
                                                                    ▲
                                                                    │ RefreshIntervalHours > 0
                                                                    └──── repeat ◄────────────┘
```

## Features

- **App-only authentication** (client credentials) to Microsoft Graph API
- Retrieves M365 Copilot user-level usage detail across all apps: Teams, Word, Excel, PowerPoint, Outlook, OneNote, Loop, Copilot Chat (work & web), Edge, M365 App, and Agents
- **Full refresh** into SQL Server — deletes existing data and bulk-loads fresh records each run
- **Configurable report periods**: D7, D30, D90, D180
- **Retry logic** with exponential backoff for transient Graph API errors (429/503/504)
- **Auto-creates** the target database, table, and views on first run
- **Optional SQL authentication** — supports both Windows Integrated Security and SQL username/password
- **Testing / Remote mode** — toggle `InTesting` to switch between local and remote SQL Server targets without changing connection strings
- **Three dashboard modes** — `Console` (ASCII-art in terminal), `Html` (standalone Chart.js report opened in browser), `Blazor` (self-hosted web dashboard at `http://localhost:5050` with live refresh)
- **Continuous background mode** — set `RefreshIntervalHours` to automatically re-collect data on a schedule; graceful shutdown via Ctrl+C
- **Proxy-aware** — works behind corporate HTTP proxies
- Optional file-based logging via `Microsoft.Extensions.Logging`

---

## Prerequisites

### 1. Azure AD / Entra ID App Registration

1. Go to [Azure Portal > App registrations](https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)
2. Create a new registration (or use an existing one)
3. Under **API permissions**, add:
   - `Microsoft Graph` > **Application** > `Reports.Read.All`
   - `Microsoft Graph` > **Application** > `ReportSettings.Read.All`
4. **Grant admin consent** for the tenant
5. Under **Certificates & secrets**, create a client secret and note the value

### 2. SQL Server

- SQL Server 2016+ or Azure SQL Database
- The program auto-creates the database and `CopilotUsageDetail` table on first run

### 3. .NET 8

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

---

## Configuration

All settings are stored in `CopilotUsageCollector/Properties/Resources.resx`. Edit this file before building:

| Resource Key | Required | Default | Description |
|--------------|----------|---------|-------------|
| `TenantId` | Yes | — | Azure AD / Entra ID tenant ID (GUID) |
| `ClientId` | Yes | — | App registration client ID (GUID) |
| `ClientSecret` | Yes | — | App registration client secret |
| `SqlConnectionString` | Yes | *(localdb)* | Local SQL Server connection string. Used when `InTesting` is `true`. Overridden by `RemoteSqlConnectionString` when `InTesting` is `false`. |
| `DatabaseName` | No | `CopilotDB` | Optional local database name. When provided, overrides the database in `SqlConnectionString`. Overridden by `RemoteDatabaseName` when `InTesting` is `false`. |
| `TableName` | Yes | `CopilotUsageDetail` | Target table name for usage data |
| `Period` | Yes | `D90` | Report period: `D7`, `D30`, `D90`, `D180` |
| `LogPath` | No | *(empty)* | File path for log output (leave empty to disable file logging) |
| `SqlUsername` | No | *(empty)* | Optional local SQL Server username. When provided, overrides Integrated Security. Overridden by `RemoteSqlUsername` when `InTesting` is `false`. |
| `SqlPassword` | No | *(empty)* | Optional local SQL Server password. Overridden by `RemoteSqlPassword` when `InTesting` is `false`. |
| `InTesting` | No | `true` | Set to `true` to use local SQL settings, or `false` to use remote SQL settings. |
| `RemoteSqlConnectionString` | No | *(empty)* | Remote SQL Server connection string. Overrides `SqlConnectionString` when `InTesting` is `false`. |
| `RemoteDatabaseName` | No | *(empty)* | Optional remote database name. Overrides `DatabaseName` when `InTesting` is `false`. |
| `RemoteSqlUsername` | No | *(empty)* | Optional remote SQL Server username. Overrides `SqlUsername` when `InTesting` is `false`. Falls back to `RemoteSqlConnectionString` when empty. |
| `RemoteSqlPassword` | No | *(empty)* | Optional remote SQL Server password. Overrides `SqlPassword` when `InTesting` is `false`. Falls back to `RemoteSqlConnectionString` when empty. |
| `DashboardMode` | No | `None` | Dashboard to launch after data collection. Options: `None` (disabled), `Console` (ASCII in terminal), `Html` (Chart.js report opened in browser), `Blazor` (self-hosted web dashboard at `http://localhost:5050`). |
| `RefreshIntervalHours` | No | `0` | Hours between automatic data refresh cycles. `0` = run once and exit. Values > 0 run continuously in the background. |

### Example `Resources.resx` values

```xml
<data name="TenantId" xml:space="preserve">
  <value>00000000-0000-0000-0000-000000000000</value>
</data>
<data name="ClientId" xml:space="preserve">
  <value>11111111-1111-1111-1111-111111111111</value>
</data>
<data name="ClientSecret" xml:space="preserve">
  <value>YourClientSecret</value>
</data>
<data name="SqlConnectionString" xml:space="preserve">
  <value>Data Source=(localdb)\MSSQLLocalDB;Integrated Security=True;</value>
</data>
<data name="DatabaseName" xml:space="preserve">
  <value>CopilotDB</value>
</data>
<data name="TableName" xml:space="preserve">
  <value>CopilotUsageDetail</value>
</data>
<data name="Period" xml:space="preserve">
  <value>D90</value>
</data>
<data name="SqlUsername" xml:space="preserve">
  <value></value>
</data>
<data name="SqlPassword" xml:space="preserve">
  <value></value>
</data>
<data name="LogPath" xml:space="preserve">
  <value></value>
</data>
<data name="InTesting" xml:space="preserve">
  <value>true</value>
</data>
<data name="RemoteSqlConnectionString" xml:space="preserve">
  <value>Data Source=myserver.database.windows.net;Encrypt=True;TrustServerCertificate=False;</value>
</data>
<data name="RemoteDatabaseName" xml:space="preserve">
  <value>CopilotDB</value>
</data>
<data name="RemoteSqlUsername" xml:space="preserve">
  <value>sqladmin</value>
</data>
<data name="RemoteSqlPassword" xml:space="preserve">
  <value>P@ssw0rd</value>
</data>
<data name="DashboardMode" xml:space="preserve">
  <value>None</value>
</data>
<data name="RefreshIntervalHours" xml:space="preserve">
  <value>0</value>
</data>
```

---

## Build & Run

```bash
cd CopilotUsageCollector
dotnet build
dotnet run
```

### Publish as Single-File Executable

```bash
dotnet publish CopilotUsageCollector -c Release -r win-x64 --self-contained
./CopilotUsageCollector/bin/Release/net8.0/win-x64/publish/CopilotUsageCollector.exe
```

---

## SQL Table Schema

The program creates a `CopilotUsageDetail` table (name is configurable) with a composite primary key on `(ReportRefreshDate, UserPrincipalName)`.

| Column | SQL Type | Nullable | Description |
|--------|----------|----------|-------------|
| `ReportRefreshDate` | `DATE` | **No** (PK) | The date the report data was last refreshed by Microsoft. This is set by the Graph API and typically lags 1–2 days behind the current date. Together with `UserPrincipalName` it forms the composite primary key. |
| `UserPrincipalName` | `NVARCHAR(320)` | **No** (PK) | The user's User Principal Name (UPN, e.g. `user@contoso.com`). This is the unique identifier for a licensed Copilot user within the tenant. |
| `DisplayName` | `NVARCHAR(256)` | Yes | The user's display name as shown in Azure AD / Entra ID (e.g. `Jane Doe`). |
| `LastActivityDate` | `DATE` | Yes | The most recent date the user had **any** Copilot activity across all M365 apps. `NULL` if the user has never used Copilot during the report period. |
| `CopilotChatLastActivityDate` | `DATE` | Yes | The last date the user interacted with **Microsoft Copilot Chat** (the standalone chat experience, combining both work and web). `NULL` if never used. |
| `CopilotChatWorkLastActivityDate` | `DATE` | Yes | The last date the user used **Copilot Chat in work mode** — grounded in the organization's Microsoft 365 data (email, files, chats). `NULL` if never used. |
| `CopilotChatWebLastActivityDate` | `DATE` | Yes | The last date the user used **Copilot Chat in web mode** — grounded in public web data. `NULL` if never used. |
| `TeamsCopilotLastActivityDate` | `DATE` | Yes | The last date the user used **Copilot in Microsoft Teams** (e.g. meeting summaries, chat compose, intelligent recap). `NULL` if never used. |
| `WordCopilotLastActivityDate` | `DATE` | Yes | The last date the user used **Copilot in Word** (e.g. draft, rewrite, summarize documents). `NULL` if never used. |
| `ExcelCopilotLastActivityDate` | `DATE` | Yes | The last date the user used **Copilot in Excel** (e.g. formula suggestions, data analysis, chart generation). `NULL` if never used. |
| `PowerPointCopilotLastActivityDate` | `DATE` | Yes | The last date the user used **Copilot in PowerPoint** (e.g. create presentation from prompt, summarize slides). `NULL` if never used. |
| `OutlookCopilotLastActivityDate` | `DATE` | Yes | The last date the user used **Copilot in Outlook** (e.g. email drafting, summarize email thread, coaching tips). `NULL` if never used. |
| `OneNoteCopilotLastActivityDate` | `DATE` | Yes | The last date the user used **Copilot in OneNote** (e.g. summarize notes, generate to-do lists, rewrite). `NULL` if never used. |
| `LoopCopilotLastActivityDate` | `DATE` | Yes | The last date the user used **Copilot in Microsoft Loop** (e.g. draft content, brainstorm, summarize pages). `NULL` if never used. |
| `M365AppCopilotLastActivityDate` | `DATE` | Yes | The last date the user used **Copilot in the Microsoft 365 App** (microsoft365.com / Office.com). `NULL` if never used. |
| `EdgeCopilotLastActivityDate` | `DATE` | Yes | The last date the user used **Copilot in Microsoft Edge** (the sidebar Copilot experience in the browser). `NULL` if never used. |
| `AgentLastActivityDate` | `DATE` | Yes | The last date the user interacted with a **Copilot Agent** (custom declarative agents built with Copilot Studio or other tools). `NULL` if never used. |
| `ReportPeriod` | `INT` | Yes | The report period in days that was requested (e.g. `7`, `30`, `90`, or `180`). Matches the numeric portion of the `Period` resource. |
| `CollectedDate` | `DATETIME2` | **No** | The UTC timestamp of when this row was inserted by the collector. Defaults to `SYSUTCDATETIME()`. Useful for auditing when data was last refreshed. |

### Database View: `CopilotInactiveUsers`

The program also creates a `CopilotInactiveUsers` view that returns all users who have **zero Copilot activity** across every tracked application — i.e. all per-app `LastActivityDate` columns are `NULL`. This is useful for identifying licensed users who have never engaged with Copilot.

---

## NuGet Dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.Identity.Client` | MSAL authentication (client credentials) |
| `Microsoft.Data.SqlClient` | SQL Server connectivity and bulk copy |
| `Microsoft.Extensions.Logging.Console` | Structured console logging |

---

## Running Tests

```bash
cd CopilotUsageCollector.Tests
dotnet test
```

---

## Project Structure

```
CopilotUsageCollector/
├── CopilotUsageCollector.csproj
├── Program.cs                       # Entry point, validation, orchestration
├── Models/
│   └── CopilotUsageRecord.cs        # POCO for Graph API JSON response
├── Properties/
│   ├── Resources.resx               # Configuration values (edit before build)
│   └── Resources.Designer.cs        # Auto-generated typed accessor
├── Dashboard/
│   ├── DashboardDataService.cs       # Shared SQL queries for all dashboards
│   ├── ConsoleDashboard.cs           # ASCII-art terminal dashboard
│   ├── HtmlDashboard.cs              # Standalone Chart.js HTML report
│   └── BlazorDashboard.cs            # Self-hosted ASP.NET Core web dashboard
├── Services/
│   ├── GraphAuthService.cs           # MSAL client credentials auth
│   ├── GraphApiService.cs            # Graph API with pagination + retry
│   ├── ProxyAwareMsalHttpClientFactory.cs  # HTTP handler for corporate proxies
│   └── SqlService.cs                 # DB/table creation, bulk copy, views
CopilotUsageCollector.Tests/
├── CopilotUsageCollector.Tests.csproj
└── UnitTest1.cs                      # xUnit tests
```

---

## Dashboard

Set `DashboardMode` in `Resources.resx` to enable a post-collection dashboard. All three modes visualize the same data: total/active/inactive user counts, per-app adoption breakdown, and the 10 most recently active users.

| Mode | Description |
|------|-------------|
| `None` | No dashboard (default). |
| `Console` | Renders an ASCII-art dashboard directly in the terminal with bar charts and summary stats. |
| `Html` | Generates a standalone HTML file with Chart.js doughnut and horizontal bar charts, saves it to the temp folder, and opens it in the default browser. |
| `Blazor` | Launches a self-hosted ASP.NET Core web server at `http://localhost:5050` with a dark-themed dashboard. Includes a **Refresh** button and a `/api/data` JSON endpoint. In continuous mode, the server stays running for the lifetime of the process. In single-run mode, press Ctrl+C to stop. |

---

## Continuous Mode

When `RefreshIntervalHours` is greater than `0`, the program runs as a long-lived background process:

- Executes the full collect → import cycle immediately on start
- Sleeps for the configured interval, then repeats
- Logs the next scheduled collection time after each cycle
- If a cycle fails (network error, Graph API failure, SQL error), it logs the error and **retries at the next interval** instead of crashing
- **Ctrl+C** triggers a graceful shutdown — finishes the current operation, closes SQL connections, and exits cleanly
- The Blazor dashboard (if enabled) runs concurrently in the background for the entire lifetime of the process
- Console and Html dashboards render once after the first successful cycle

Set `RefreshIntervalHours` to `0` (default) to run once and exit.

---

## Troubleshooting

| Error | Cause | Fix |
|-------|-------|-----|
| HTTP 401/403 from Graph | Missing or insufficient permissions | Ensure `Reports.Read.All` and `ReportSettings.Read.All` (Application) are granted with admin consent |
| HTTP 429 from Graph | Rate limited | The program auto-retries with backoff; reduce run frequency if persistent |
| SQL connection error | Bad connection string or firewall | Verify connection string and `SqlUsername`/`SqlPassword`; check SQL Server firewall rules |
| No data returned | No Copilot licenses or activity | Confirm Copilot licenses are assigned and users have had activity in the requested period |

