# Quick Start: Integrate C# Audit Service into Razor Pages

## Step 1: Install Dependencies

```bash
cd your-project
dotnet add package HtmlAgilityPack
dotnet add package EPPlus
```

## Step 2: Create Folders

```
YourProject/
├── Models/
│   └── AuditModels.cs              (copy provided file)
├── Services/
│   ├── UrlUtilityService.cs        (copy provided file)
│   ├── AuditAnalysisService.cs     (copy provided file)
│   ├── PageComparisonService.cs    (copy provided file)
│   ├── ReportGenerationService.cs  (copy provided file)
│   └── AuditService.cs             (copy provided file)
└── Pages/
    ├── Audit.cshtml                (copy provided file)
    └── Audit.cshtml.cs             (copy provided file)
```

## Step 3: Update Program.cs (or Startup.cs)

**For .NET 6+ (Program.cs):**

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Razor Pages
builder.Services.AddRazorPages();

// Add HttpClient
builder.Services.AddHttpClient();

// Register Audit Services
builder.Services.AddScoped<UrlUtilityService>();
builder.Services.AddScoped<AuditAnalysisService>();
builder.Services.AddScoped<PageComparisonService>();
builder.Services.AddScoped<ReportGenerationService>();
builder.Services.AddScoped<AuditService>();

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
    services.AddHttpClient();
    
    services.AddScoped<UrlUtilityService>();
    services.AddScoped<AuditAnalysisService>();
    services.AddScoped<PageComparisonService>();
    services.AddScoped<ReportGenerationService>();
    services.AddScoped<AuditService>();
}
```

## Step 4: Create Report Output Folder

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

## Step 5: Test the Integration

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

## Step 6: Verify Output

After audit completes, you should see:

- ✓ Summary counts (PASS, FAIL, REVIEW)
- ✓ Release readiness status
- ✓ Links to generated reports (CSV, Excel, HTML)
- ✓ Queue A and Queue B results
- ✓ Files in: `Audits/Audit_<SITE>_<TIMESTAMP>/`

## Common Issues

### Issue: "Service not registered"

**Error:** `InvalidOperationException: Unable to resolve service for type 'AuditService'`

**Solution:** Ensure all services are registered in Program.cs:
```csharp
builder.Services.AddScoped<UrlUtilityService>();
builder.Services.AddScoped<AuditAnalysisService>();
builder.Services.AddScoped<PageComparisonService>();
builder.Services.AddScoped<ReportGenerationService>();
builder.Services.AddScoped<AuditService>();
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
using AuditApp.Models;
using AuditApp.Services;

var services = new ServiceCollection();
services.AddHttpClient();
services.AddScoped<UrlUtilityService>();
services.AddScoped<AuditAnalysisService>();
services.AddScoped<PageComparisonService>();
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
