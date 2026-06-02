# Quick Start: Integrate C# Audit Service into a C# Project

## Step 1: Build or Pack the Reusable Library

```bash
cd PostMigrationUmbraco_SiteAudit
dotnet pack .\AuditApp.Package\AuditApp.Package.csproj -c Release
```

This produces `AuditApp.Package\bin\Release\WSU.MigrationAudit.0.1.0.nupkg`.

## Step 2: Reference the Package from Your App

**Option A: local project reference during development**

```bash
cd path/to/your/app
dotnet add reference ..\PostMigrationUmbraco_SiteAudit\AuditApp.Package\AuditApp.Package.csproj
```

**Option B: consume the packed NuGet package**

Change to the consuming project directory first, then add the package there.

```bash
cd path/to/your/app
dotnet add package WSU.MigrationAudit --source <your-package-feed>
```

## Step 3: Create Folders

```
YourProject/
├── Audits/
├── wwwroot/
│   └── reports/
└── Pages/
    └── ... your own UI or API surface
```

## Step 4: Update Program.cs (or Startup.cs)

**For .NET 6+ (Program.cs):**

```csharp
using AuditApp;
using AuditApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Razor Pages
builder.Services.AddRazorPages();

// Register the packaged audit engine
builder.Services.AddMigrationAuditServices();

var app = builder.Build();

// ... rest of configuration
app.MapRazorPages();
app.Run();
```

**For .NET Framework (Startup.cs):**

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddRazorPages();
    services.AddMigrationAuditServices();
}
```

## Step 5: Call the Package

For a web app with a separate browser client, call the package from the server-side project, not from a Blazor WebAssembly client.

Recommended pattern:

- install and register `WSU.MigrationAudit` in the server/web project
- create a server-side API endpoint that injects `AuditService`
- let the UI call that endpoint
- on success, redirect the UI from `/Audit` to a dedicated results page such as `/Audit/Results`
- on the results page, show the HTML report as the primary link and optionally preview it inline

```csharp
using AuditApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace YourApp.Api;

[ApiController]
[Route("api/audit")]
public class AuditController : ControllerBase
{
    private readonly AuditService _auditService;

    public AuditController(AuditService auditService)
    {
        _auditService = auditService;
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] AuditRunRequest request)
    {
        var progressMessages = new List<string>();
        var progress = new Progress<string>(message => progressMessages.Add(message));

        var summary = await _auditService.RunAuditAsync(MapToConfig(request), progress);

        return Ok(new
        {
            success = true,
            messages = progressMessages,
            summary = new
            {
                summary.SiteName,
                summary.RunDate,
                summary.PassCount,
                summary.ReviewCount,
                summary.FailCount,
                HtmlUrl = ToReportUrl(summary.HtmlReportPath),
                ExecutiveHtmlUrl = ToReportUrl(summary.ExecutiveHtmlPath),
                XlsxUrl = ToReportUrl(summary.XlsxPath)
            }
        });
    }
}
```

If you are using Blazor WebAssembly, do not add `AuditApp` or `OfficeOpenXml` directly to the browser client project. Keep the audit package on the server side.

## Step 6: Create Report Output Folder

```bash
mkdir wwwroot/reports
```

And add route handler in Program.cs:

```csharp
app.UseStaticFiles();  // Ensure this exists

// Add static file middleware for reports
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/reports"))
    {
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "Audits", context.Request.Path.Value.TrimStart('/'));
        if (File.Exists(filePath))
        {
            context.Response.ContentType = GetContentType(filePath);
            await context.Response.SendFileAsync(filePath);
            return;
        }
    }
    await next();
});
```

## Step 7: Test the Integration

1. **Build the project:**
   ```bash
   dotnet build
   ```

2. **Run the project:**
   ```bash
   dotnet run
   ```

3. **Navigate to:**
   ```
   https://localhost:5001/audit
   ```

4. **Fill in the form:**
   - Site Name: "Test Site"
   - Source URL: "https://example.com"
   - Test URL: "https://example-dev.com"
   - Click "Start Audit"

5. **Expected success flow:**
    - `/Audit` submits the request
    - the server returns typed report metadata
    - the app navigates to `/Audit/Results`
    - the results page shows the HTML report link and related artifact links

## Step 8: Verify Output

After audit completes, you should see:

- ✓ A dedicated results page such as `/Audit/Results` or equivalent host-specific success surface
- ✓ Links to generated reports (HTML, Executive Preview, Excel, and optionally CSV)
- ✓ HTML report preview inline when the host app supports it
- ✓ HTML report with Section Release Readiness and Detailed Rows tables
- ✓ Release readiness CSV with section-based rows
- ✓ Fix On Test Site top priorities
- ✓ Files in: `Audits/Audit_<SITE>_<TIMESTAMP>/`

### Subdomain-To-Subpath Migrations

If the source site is a subdomain but the test site lives under a path, enable **Source is a subdomain mapped under the test URL path**.

Example:
- Source: `https://cougarhealth.wsu.edu`
- Test: `https://dev.studentaffairs.wsu.edu/chs`

With that setting enabled, the audit preserves `/chs` as the site root prefix instead of dropping it and probing the host root.

## Common Issues

### Issue: "Service not registered"

**Error:** `InvalidOperationException: Unable to resolve service for type 'AuditService'`

**Solution:** Ensure the package extension is registered in Program.cs:
```csharp
builder.Services.AddMigrationAuditServices();
```

### Issue: "Reports folder not found"

**Solution:** Ensure `Audits/` folder exists:
```bash
mkdir Audits
```

### Issue: "Localhost SSL certificate errors"

**Solution:** Already handled by `UrlUtilityService.GetHandlerForUrl()` - it disables SSL verification for localhost.

### Issue: "Timeout on long audits"

**Solution:** Increase timeout in `PageComparisonService`:
```csharp
using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) })
```

### Issue: "The loading audit runner message never goes away"

**Solution:** If your host uses an embedded WebAssembly runner on `/Audit`, remove any permanent static fallback block once the component mounts and redirect successful runs to a separate results page.

## Customization

### Change Max Concurrent Tabs

```csharp
// In AuditModel.cshtml.cs
Config.MaxTabs = 10;  // Default is 5
```

### Add More Test Environments

```html
<!-- In Audit.cshtml -->
<select asp-for="Config.TestUrl">
    <option value="https://custom-test.edu/">Custom Test</option>
    <option value="https://staging.example.com/">Staging</option>
</select>
```

### Customize Report CSS

Edit the `<style>` section in `Audit.cshtml` or `ReportGenerationService.WriteHtmlAsync()`.

## Next Steps

1. **Test with your real sites:** Update site names, URLs, and run full audits
2. **Integrate into CI/CD:** Call `OnPostStartAuditAsyncApi()` from build pipeline
3. **Add database storage:** Save `AuditSummary` to database for history
4. **Extend path discovery:** Implement `DiscoverPathsAsync()` to fetch from sitemap.xml
5. **Add authentication:** Protect audit pages with ASP.NET Core Identity

## Example: Running from Command Line

Create a console wrapper:

```csharp
// Program.cs (for console app)
using AuditApp;
using AuditApp.Models;
using AuditApp.Services;

var services = new ServiceCollection();
services.AddMigrationAuditServices();
services.AddScoped<ReportGenerationService>();
services.AddScoped<AuditService>();

var provider = services.BuildServiceProvider();
var auditService = provider.GetRequiredService<AuditService>();

var config = new AuditConfig
{
    SiteName = "MyWebsite",
    SourceUrl = "https://mysite.com",
    TestUrl = "https://mysite-dev.com",
    TestScope = "single"
};

var progress = new Progress<string>(msg => Console.WriteLine(msg));
var summary = await auditService.RunAuditAsync(config, progress);

Console.WriteLine($"\nResults:");
Console.WriteLine($"PASS: {summary.PassCount}");
Console.WriteLine($"FAIL: {summary.FailCount}");
Console.WriteLine($"Report: {summary.HtmlReportPath}");
```

## Architecture Diagram

```
┌─────────────────────────────────────────────┐
│      Razor Page (Audit.cshtml)              │
│  - Form input (site name, URLs, options)    │
│  - Status display                            │
│  - Report links                              │
└────────────────┬────────────────────────────┘
                 │ User input
                 ▼
┌─────────────────────────────────────────────┐
│   PageModel (Audit.cshtml.cs)               │
│  - Binds form to AuditConfig                │
│  - Calls AuditService.RunAuditAsync()       │
└────────────────┬────────────────────────────┘
                 │ IProgress<string>
                 ▼
┌─────────────────────────────────────────────┐
│       AuditService (Orchestrator)           │
│  - Coordinates full audit flow              │
│  - Calls other services in sequence         │
└────────────────┬────────────────────────────┘
                 │
        ┌────────┼────────┐
        │        │        │
        ▼        ▼        ▼
    ┌──────────────────────────────────────┐
    │  UrlUtilityService                   │
    │  - URL normalization & joining       │
    │  - HTTP probing                      │
    │  - Host mismatch detection           │
    └──────────────────────────────────────┘
    
    ┌──────────────────────────────────────┐
    │  PageComparisonService               │
    │  - Fetch pages via HTTP              │
    │  - Extract text content              │
    │  - Calculate similarity score        │
    └──────────────────────────────────────┘
    
    ┌──────────────────────────────────────┐
    │  AuditAnalysisService                │
    │  - Score pages                       │
    │  - Cluster failures                  │
    │  - Build result queues               │
    │  - Calculate readiness               │
    └──────────────────────────────────────┘
    
    ┌──────────────────────────────────────┐
    │  ReportGenerationService             │
    │  - Write CSV                         │
    │  - Write Excel                       │
    │  - Write HTML report                 │
    └──────────────────────────────────────┘
                 │
                 ▼
        ┌────────────────────────┐
        │  AuditSummary Results  │
        │  - Counts              │
        │  - Queues A & B        │
        │  - Report file paths   │
        └────────────────────────┘
                 │
                 ▼
        Displayed in UI + Files saved to disk
```

## Support

For issues or questions:
1. Check the `IMPLEMENTATION_GUIDE.md` for detailed API documentation
2. Review individual service comments
3. Refer to the Python `audit.py` for original logic
4. Test with small audits first (set `MaxPaths = 10`)

Good luck!
