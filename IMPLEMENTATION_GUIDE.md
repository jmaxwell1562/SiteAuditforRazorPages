# C# Audit Service Port - Implementation Guide

## Overview

This is a complete C# port of the Python `audit.py` website migration auditor, designed for ASP.NET Core Razor Pages. The architecture is modular and extensible, with clear separation of concerns across multiple services, and the core engine is now packaged in `AuditApp.Package` for reuse in other C# applications.

## Reusable Package

- Project: `AuditApp.Package/AuditApp.Package.csproj`
- Package ID: `WSU.MigrationAudit`
- Service registration: `AddMigrationAuditServices()`
- Pack command: `dotnet pack .\AuditApp.Package\AuditApp.Package.csproj -c Release`

Minimal host setup:

```csharp
using AuditApp;

builder.Services.AddMigrationAuditServices();
```

## Architecture

### Services

#### 1. **UrlUtilityService**
Handles URL manipulation, normalization, and network operations.

**Key Methods:**
- `NormalizePath(path)` - Normalize path format (leading/trailing slashes)
- `NormalizeSiteBase(url)` - Remove trailing slash from URL
- `JoinTestUrl(base, path, sourceBase, forcePrefixBasePath)` - Smart URL joining with section/root detection and explicit subdomain-to-subpath support
- `ProbeUrlAsync(url)` - HTTP HEAD request to check status without loading content
- `MaybeCorrectTestBaseAsync(sourceBase, testBase, paths)` - Auto-detect and fix w2/wdev3 host mismatches
- `IsLocalhostUrl(url)` - Check if URL is localhost (for SSL bypass)
- `SanitizeSiteName(siteName)` - Make site name filesystem-safe

**Usage:**
```csharp
var urlService = new UrlUtilityService(httpClient);
var normalizedPath = urlService.NormalizePath("/about/contact");
// Returns: "/about/contact/"

var testUrl = urlService.JoinTestUrl("https://test.edu/about", "/contact", "https://prod.edu");
// Smart joining based on path type (section vs root)
```

#### 2. **AuditAnalysisService**
Scoring, clustering, and result analysis.

**Key Methods:**
- `DetermineRootCause(status, score, sourceStatus, testStatus, isRedirect)` - Categorize failure reason
- `SortResults(results)` - Sort by status priority, then score
- `ClusterFailures(results)` - Group failures by test site, section, and failure signature
- `BuildQueues(results)` - Separate into Queue A (fixes) and Queue B (source/shared issues)
- `CalculateReadiness(results)` - Determine GO/CONDITIONAL GO/NO GO status
- `GetTopPriority(results, limit)` - Top N priority items
- `CountByStatus(results)` - Count by status type

**Usage:**
```csharp
var analysisService = new AuditAnalysisService();

// Cluster and analyze
var (annotated, clusters) = analysisService.ClusterFailures(results);

// Get priority queues
var (queueA, queueB) = analysisService.BuildQueues(annotated);

// Check readiness
var readiness = analysisService.CalculateReadiness(annotated);
if (readiness.GoCount > 0) {
    Console.WriteLine("Release is GO");
}
```

#### 3. **PageComparisonService**
Page content comparison and similarity scoring.

**Key Methods:**
- `CompareUrlsAsync(sourceUrl, testUrl)` - Compare content, return 0.0-1.0 score
- `FetchPageAsync(url)` - Fetch and extract text content
- `ExtractTextContent(html)` - Parse HTML and extract visible text (removes scripts, styles)

**Usage:**
```csharp
var comparisonService = new PageComparisonService(urlService);
double similarity = await comparisonService.CompareUrlsAsync(sourceUrl, testUrl);

if (similarity >= 0.95) {
    // High parity - PASS
}
```

#### 4. **ReportGenerationService**
Generate CSV, XLSX, and HTML reports.

**Key Methods:**
- `WriteCsvAsync(summary)` - Generate main audit CSV
- `WriteClusterSummaryAsync(summary)` - Write failure clusters
- `WriteReadinessSummaryAsync(summary)` - Write readiness summary
- `WriteExcelAsync(summary)` - Generate formatted Excel workbook
- `WriteHtmlAsync(summary)` - Generate HTML report with section readiness and detailed rows

**Usage:**
```csharp
var reportService = new ReportGenerationService();

await reportService.WriteCsvAsync(summary);      // Main report CSV
await reportService.WriteExcelAsync(summary);    // Colored Excel workbook
await reportService.WriteHtmlAsync(summary);     // Interactive HTML report
```

#### 5. **AuditService** (Orchestrator)
Main audit orchestration - ties all services together.

**Key Methods:**
- `RunAuditAsync(config, progress)` - Run full audit pipeline
- `DiscoverPathsAsync(sourceUrl, testBase, maxPaths, progress)` - Find URLs to audit
- `DiscoverTestBasesAsync(scope, seedBase, allowlist, allowlistFile, progress)` - Find test environments
- `AuditPathsAsync(...)` - Parallel audit of all paths
- `AuditPageAsync(...)` - Audit single page

**Usage:**
```csharp
var auditService = new AuditService(urlService, analysisService, comparisonService, reportService);

var config = new AuditConfig
{
    SiteName = "Cougar Card",
    SourceUrl = "https://cougarcard.wsu.edu",
    TestUrl = "https://wdev3-testing.asis.wsu.edu/cougarcard",
    SourceIsSubdomain = false,
    TestScope = "single",
    MaxTabs = 5,
    MaxPaths = 0  // 0 = all paths
};

var progress = new Progress<string>(msg => Console.WriteLine(msg));

var summary = await auditService.RunAuditAsync(config, progress);

Console.WriteLine($"Results: {summary.PassCount} pass, {summary.FailCount} fail");
Console.WriteLine($"Report: {summary.HtmlReportPath}");
```

## Data Models

### AuditConfig
```csharp
public class AuditConfig
{
    public string SiteName { get; set; }           // "Cougar Card"
    public string SourceUrl { get; set; }          // "https://cougarcard.wsu.edu"
    public string TestUrl { get; set; }            // "https://w2-testing.asis.wsu.edu/cougarcard"
    public string TestSlug { get; set; }           // (deprecated)
    public string TestScope { get; set; }          // "single" or "batch"
    public string TestAllowlist { get; set; }      // legacy
    public string TestAllowlistFile { get; set; }  // legacy
    public string RedirectOverridePaths { get; set; }
    public int MaxTabs { get; set; }               // Parallel tabs (default 5)
    public int MaxPaths { get; set; }              // Limit paths (0 = all)
}
```

### Current Batch Model
- Each batch row represents one source host mapped to one target base path or full target URL.
- Source path discovery uses the source sitemap and links that stay on that source host.
- If a program is nested under a parent site in navigation or Umbraco but its source content lives on another domain, it must be listed on its own batch row.
- Full target URLs with path prefixes such as `https://w3-testing.asis.wsu.edu/aea/camp/` are supported and preserve the full target path during URL joins.

### AuditResult
```csharp
public class AuditResult
{
    public string Path { get; set; }                // "/about"
    public string SourceUrl { get; set; }           // Full source URL
    public string TestUrl { get; set; }             // Full test URL
    public string TestSite { get; set; }            // "root" or path segment
    public int? SourceStatus { get; set; }          // 200, 404, etc.
    public int? TestStatus { get; set; }            // 200, 404, etc.
    public double Score { get; set; }               // 0.0-1.0 similarity
    public string Status { get; set; }              // PASS, SOFT PASS, REVIEW, FAIL, SKIP, ERROR
    public string RootCause { get; set; }           // Human-readable reason
    public string Section { get; set; }             // Section from path
    public bool SystemicBreakage { get; set; }      // Part of systemic issue?
}
```

### AuditSummary
```csharp
public class AuditSummary
{
    public string SiteName { get; set; }
    public string RunDate { get; set; }             // YYYYMMDD
    public string Timestamp { get; set; }           // YYYYMMdd_HHmmss
    
    // Counts
    public int PassCount { get; set; }
    public int FailCount { get; set; }
    public int ReviewCount { get; set; }
    
    // Files generated
    public string MainCsvPath { get; set; }
    public string HtmlReportPath { get; set; }
    public string XlsxPath { get; set; }
    
    // Results
    public List<AuditResult> AllResults { get; set; }
    public List<AuditResult> QueueA { get; set; }    // Fix on test site
    public List<AuditResult> QueueB { get; set; }    // Source/shared issues
    
    // Readiness
    public ReadinessStatus Readiness { get; set; }
}
```

## Integration with Razor Pages

### 1. Setup (Program.cs or Startup.cs)

```csharp
// Register services
builder.Services.AddScoped<UrlUtilityService>();
builder.Services.AddScoped<AuditAnalysisService>();
builder.Services.AddScoped<PageComparisonService>();
builder.Services.AddScoped<ReportGenerationService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddHttpClient();
```

### 2. Razor Page Model (Audit.cshtml.cs)

```csharp
public class AuditModel : PageModel
{
    private readonly AuditService _auditService;

    [BindProperty]
    public AuditConfig Config { get; set; }

    public AuditModel(AuditService auditService)
    {
        _auditService = auditService;
    }

    public async Task<IActionResult> OnPostStartAuditAsync()
    {
        var progress = new Progress<string>(msg => StatusMessages.Add(msg));
        LastSummary = await _auditService.RunAuditAsync(Config, progress);
        return Page();
    }
}
```

### 3. View (Audit.cshtml)

```html
<form method="post">
    <label>Site Name</label>
    <input asp-for="Config.SiteName" />
    
    <label>Source URL</label>
    <input asp-for="Config.SourceUrl" />
    
    <label>Test URL</label>
    <select asp-for="Config.TestUrl">
        <option value="https://wdev3-testing.asis.wsu.edu/">WDEV3</option>
        <option value="https://w2-testing.asis.wsu.edu/">W2</option>
    </select>
    
    <button type="submit" asp-page-handler="StartAudit">Start Audit</button>
</form>

@if (Model.LastSummary != null)
{
    <p>PASS: @Model.LastSummary.PassCount</p>
    <p>FAIL: @Model.LastSummary.FailCount</p>
    <a href="@Model.LastSummary.HtmlReportPath">View Report</a>
}
```

## Extending the Services

### Custom Similarity Scoring

Replace the word-based similarity in `PageComparisonService`:

```csharp
// Add to PageComparisonService
private double CalculateSimilarityAdvanced(string source, string test)
{
    // Implement Levenshtein distance
    // Or use ML-based similarity
    // Or use structural HTML comparison
}
```

### Sitemap Discovery

Extend `AuditService.DiscoverPathsAsync()`:

```csharp
private async Task<List<string>> DiscoverPathsAsync(...)
{
    var paths = new List<string>();
    
    // Fetch sitemap.xml
    var sitemapUrl = sourceUrl + "/sitemap.xml";
    var sitemapContent = await _comparisonService.FetchPageAsync(sitemapUrl);
    
    // Parse XML and extract URLs
    var doc = new XmlDocument();
    doc.LoadXml(sitemapContent);
    
    foreach (XmlNode node in doc.SelectNodes("//url/loc"))
    {
        paths.Add(node.InnerText);
    }
    
    return paths;
}
```

### Custom Report Formats

Add to `ReportGenerationService`:

```csharp
public async Task<string> WriteMarkdownAsync(AuditSummary summary)
{
    // Generate Markdown report
    var sb = new StringBuilder();
    sb.AppendLine($"# {summary.SiteName} Audit Report");
    sb.AppendLine($"- PASS: {summary.PassCount}");
    sb.AppendLine($"- FAIL: {summary.FailCount}");
    // ... etc
    return await Task.FromResult(filePath);
}

public async Task<string> WriteJsonAsync(AuditSummary summary)
{
    // Generate JSON for API integration
    var json = JsonConvert.SerializeObject(summary, Formatting.Indented);
    File.WriteAllText(filePath, json);
    return await Task.FromResult(filePath);
}
```

## NuGet Dependencies

```
HtmlAgilityPack             - HTML parsing and XPath queries
EPPlus                      - Excel workbook generation
Microsoft.AspNetCore.Mvc    - Razor Pages framework
```

Install via:
```bash
dotnet add package HtmlAgilityPack
dotnet add package EPPlus
```

## Performance Tuning

1. **Concurrency**: Adjust `config.MaxTabs` to control parallel requests (default 5)
2. **Path Limiting**: Set `config.MaxPaths = 80` for quick smoke tests
3. **Caching**: Cache sitemap fetches and URL probes
4. **Timeouts**: Adjust `HttpClient` timeout in `PageComparisonService`

## Error Handling

All services include try-catch blocks. Errors are:
- Logged to `System.Diagnostics.Debug`
- Captured in `AuditResult.Status = "ERROR"`
- Included in root cause analysis

## Testing

### Unit Test Example

```csharp
[TestClass]
public class UrlUtilityServiceTests
{
    private UrlUtilityService _service;

    [TestInitialize]
    public void Setup()
    {
        var httpClient = new HttpClient();
        _service = new UrlUtilityService(httpClient);
    }

    [TestMethod]
    public void NormalizePath_AddsSlashes()
    {
        var result = _service.NormalizePath("about/contact");
        Assert.AreEqual("/about/contact/", result);
    }

    [TestMethod]
    public void JoinTestUrl_HandlesSection()
    {
        var result = _service.JoinTestUrl(
            "https://test.edu/cougarcard",
            "/about",
            "https://prod.edu"
        );
        Assert.IsTrue(result.Contains("/about"));
    }
}
```

## Summary

This C# port provides a production-ready audit framework that:
- ✓ Maintains Python feature parity
- ✓ Integrates seamlessly with Razor Pages
- ✓ Uses modern async/await patterns
- ✓ Provides extensible architecture
- ✓ Generates professional reports
- ✓ Handles edge cases (localhost SSL, host mismatches, etc.)
