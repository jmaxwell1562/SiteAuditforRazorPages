# Python to C# Mapping Reference

This document shows how Python audit.py functions map to C# services.

## Configuration & Arguments

### Python
```python
import argparse
parser = argparse.ArgumentParser(description="WSU Migration Auditor")
parser.add_argument("--site", help="Site Name", default="Site")
parser.add_argument("--source", help="Source URL", default="")
parser.add_argument("--test_url", help="Test URL", default="")
# ... more args
args = parser.parse_args()

SITE_NAME = args.site
SOURCE_BASE = args.source
TEST_BASE = args.test_url
```

### C#
```csharp
public class AuditConfig
{
    public string SiteName { get; set; }
    public string SourceUrl { get; set; }
    public string TestUrl { get; set; }
    // ... properties for all args
}

// Used in PageModel
[BindProperty]
public AuditConfig Config { get; set; }
```

---

## URL Utilities

### Python: normalize_path()
```python
def normalize_path(path):
    cleaned = (path or "").strip()
    if not cleaned:
        return "/"
    return "/" + cleaned.strip("/") + "/"
```

### C#: UrlUtilityService
```csharp
public string NormalizePath(string path)
{
    string cleaned = (path ?? "").Trim();
    if (string.IsNullOrEmpty(cleaned))
        return "/";
    return "/" + cleaned.Trim('/') + "/";
}
```

---

### Python: join_test_url()
```python
def join_test_url(base, path):
    base = normalize_site_base(base)
    normalized_path = "/" + (path or "").lstrip("/")
    
    parsed_base = urlparse(base)
    base_path = "/" + parsed_base.path.strip("/") if parsed_base.path.strip("/") else ""
    
    if base_path and not should_prefix_test_base_path(base_path, SOURCE_BASE):
        host_root = f"{parsed_base.scheme}://{parsed_base.netloc}".rstrip("/")
        return f"{host_root}{normalized_path}"
    
    return f"{base}{normalized_path}"
```

### C#: UrlUtilityService
```csharp
public string JoinTestUrl(string baseUrl, string path, string sourceBase = "")
{
    baseUrl = NormalizeSiteBase(baseUrl);
    string normalizedPath = "/" + (path ?? "").TrimStart('/');

    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        return baseUrl + normalizedPath;

    string basePath = uri.AbsolutePath.TrimEnd('/');
    
    if (!string.IsNullOrEmpty(basePath) && !ShouldPrefixTestBasePath(basePath, sourceBase))
    {
        string hostRoot = $"{uri.Scheme}://{uri.Host}";
        if (uri.Port != 80 && uri.Port != 443 && uri.Port != -1)
            hostRoot += $":{uri.Port}";
        return hostRoot + normalizedPath;
    }

    return baseUrl + normalizedPath;
}
```

---

### Python: probe_url()
```python
def probe_url(url):
    """Fetch URL once without following redirects."""
    try:
        response = requests.get(url, allow_redirects=False, timeout=20)
        is_redirect = response.is_redirect or response.is_permanent_redirect
        return response.status_code, is_redirect
    except Exception as e:
        print(f"[WARN] Probe failed for {url}: {str(e)[:120]}")
        return None, False
```

### C#: UrlUtilityService
```csharp
public async Task<(int? status, bool isRedirect)> ProbeUrlAsync(string url)
{
    try
    {
        using (var handler = GetHandlerForUrl(url))
        using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) })
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                bool isRedirect = (int)response.StatusCode >= 300 && (int)response.StatusCode < 400;
                return ((int)response.StatusCode, isRedirect);
            }
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[WARN] Probe failed for {url}: {ex.Message.Substring(0, Math.Min(120, ex.Message.Length))}");
        return (null, false);
    }
}
```

---

## Scoring & Analysis

### Python: determine_root_cause()
```python
def determine_root_cause(status, score, source_status, test_status, redirect):
    if redirect:
        return "Redirect handling required"
    if status == "FAIL":
        if source_status == 200 and (test_status is None or test_status >= 400):
            return "Migration gap: source healthy, test failing"
        if source_status is not None and source_status >= 400 and test_status is not None and test_status >= 400:
            return "Shared instability: source and test both failing"
        # ... more cases
    if status == "REVIEW":
        return f"Content mismatch: {score:.1%} similarity"
    # ... etc
```

### C#: AuditAnalysisService
```csharp
public string DetermineRootCause(string status, double score, int? sourceStatus, int? testStatus, bool isRedirect)
{
    if (isRedirect)
        return "Redirect handling required";

    if (status == "FAIL")
    {
        if (sourceStatus == 200 && (testStatus == null || testStatus >= 400))
            return "Migration gap: source healthy, test failing";
        if (sourceStatus != null && sourceStatus >= 400 && testStatus != null && testStatus >= 400)
            return "Shared instability: source and test both failing";
        // ... more cases
    }
    if (status == "REVIEW")
        return $"Content mismatch: {score:P1} similarity";
    // ... etc
}
```

---

### Python: sort_audit_results()
```python
def sort_audit_results(rows):
    status_order = {
        "FAIL": 0,
        "REVIEW": 1,
        "SOFT PASS": 2,
        "PASS": 3,
        "SKIP": 4,
        "ERROR": 5,
    }

    rows.sort(
        key=lambda row: (
            status_order.get((row.get("status") or "").strip(), 99),
            to_float(row.get("score", 0)),
            row.get("test_site") or "",
            row.get("path") or "",
        )
    )
    return rows
```

### C#: AuditAnalysisService
```csharp
public List<AuditResult> SortResults(IEnumerable<AuditResult> results)
{
    var statusOrder = new Dictionary<string, int>
    {
        { "FAIL", 0 },
        { "REVIEW", 1 },
        { "SOFT PASS", 2 },
        { "PASS", 3 },
        { "SKIP", 4 },
        { "ERROR", 5 }
    };

    return results
        .OrderBy(r => statusOrder.TryGetValue(r.Status ?? "", out var order) ? order : 99)
        .ThenBy(r => r.Score)
        .ThenBy(r => r.TestSite)
        .ThenBy(r => r.Path)
        .ToList();
}
```

---

### Python: cluster_failure_patterns()
```python
def cluster_failure_patterns(rows):
    failure_statuses = {"FAIL", "ERROR"}
    failures = [row for row in rows if (row.get("status") or "").strip() in failure_statuses]
    
    for row in rows:
        row["section"] = section_from_path(row.get("path", ""))
        row["test_site"] = row.get("test_site") or "root"
        row["failure_cluster"] = ""
        row["systemic_breakage"] = False
        row["systemic_reason"] = ""
    
    if not failures:
        return []
    
    grouped = {}
    for row in failures:
        test_site = row["test_site"]
        section = row["section"]
        signature = normalize_signature(row)
        key = (test_site, section, signature)
        grouped.setdefault(key, []).append(row)
    
    # Process each cluster group...
    summary_rows = []
    # ... cluster analysis logic
    return summary_rows
```

### C#: AuditAnalysisService
```csharp
public (List<AuditResult> annotated, List<FailureCluster> clusters) ClusterFailures(List<AuditResult> results)
{
    var failureStatuses = new HashSet<string> { "FAIL", "ERROR" };
    var failures = results
        .Where(r => failureStatuses.Contains(r.Status ?? ""))
        .ToList();

    foreach (var row in results)
    {
        row.Section = SectionFromPath(row.Path);
        row.TestSite = row.TestSite ?? "root";
        row.FailureCluster = "";
        row.SystemicBreakage = false;
        row.SystemicReason = "";
    }

    if (failures.Count == 0)
        return (results, new List<FailureCluster>());

    var grouped = new Dictionary<(string, string, string), List<AuditResult>>();
    
    foreach (var row in failures)
    {
        var testSite = row.TestSite;
        var section = row.Section;
        var signature = NormalizeSignature(row);
        var key = (testSite, section, signature);
        
        if (!grouped.ContainsKey(key))
            grouped[key] = new List<AuditResult>();
        grouped[key].Add(row);
    }
    
    // ... cluster analysis logic
    return (results, summaries);
}
```

---

## Content Comparison

### Python: SequenceMatcher-like similarity
```python
from difflib import SequenceMatcher
# Python's difflib uses longest contiguous matching subsequence
ratio = SequenceMatcher(None, source_text, test_text).ratio()
```

### C#: Custom token-based similarity
```csharp
private double CalculateSimilarity(string source, string test)
{
    var sourceWords = TokenizeText(source);
    var testWords = TokenizeText(test);

    if (sourceWords.Count == 0 || testWords.Count == 0)
        return 0.0;

    int matches = 0;
    var testWordSet = new HashSet<string>(testWords);

    foreach (var word in sourceWords)
    {
        if (testWordSet.Contains(word))
            matches++;
    }

    double similarity = (double)matches / Math.Max(sourceWords.Count, testWords.Count);
    return Math.Min(1.0, similarity);
}

private List<string> TokenizeText(string text)
{
    var words = Regex.Split(text, @"[\s\p{P}]+")
        .Where(w => !string.IsNullOrEmpty(w) && w.Length > 2)
        .Select(w => w.ToLower())
        .ToList();

    return words;
}
```

---

## Async Patterns

### Python: asyncio
```python
import asyncio

async def audit_page(url):
    # Do work
    pass

async def main():
    semaphore = asyncio.Semaphore(5)  # Max 5 concurrent
    
    tasks = []
    for path in paths:
        task = semaphore_task(path)
        tasks.append(task)
    
    results = await asyncio.gather(*tasks)

asyncio.run(main())
```

### C#: async/await with SemaphoreSlim
```csharp
public async Task<List<AuditResult>> AuditPathsAsync(...)
{
    var results = new List<AuditResult>();
    var semaphore = new SemaphoreSlim(maxConcurrency);

    var tasks = new List<Task>();
    foreach (var testBase in testBases)
    {
        foreach (var path in paths)
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var result = await AuditPageAsync(...);
                    lock (results)
                    {
                        results.Add(result);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }
    }

    await Task.WhenAll(tasks);
    return results;
}
```

---

## Report Generation

### Python: CSV writing
```python
import csv

def write_csv(base_dir, safe_site_name, run_date, rows):
    filename = f"{safe_site_name}_audit_report_{run_date}.csv"
    filepath = os.path.join(base_dir, filename)
    
    with open(filepath, 'w', newline='', encoding='utf-8') as f:
        writer = csv.DictWriter(f, fieldnames=['path', 'status', 'score', ...])
        writer.writeheader()
        writer.writerows(rows)
    
    return filepath
```

### C#: CSV writing with StringBuilder
```csharp
public async Task<string> WriteCsvAsync(AuditSummary summary)
{
    var filePath = Path.Combine(
        summary.AuditFolder,
        $"{summary.SafeSiteName}_audit_report_{summary.RunDate}.csv");

    var sb = new StringBuilder();
    sb.AppendLine("path,source_url,test_url,test_site,source_status,test_status,score,status,note,root_cause,section");

    foreach (var result in summary.AllResults)
    {
        var row = new[]
        {
            EscapeCsv(result.Path),
            EscapeCsv(result.SourceUrl),
            // ... more fields
        };
        sb.AppendLine(string.Join(",", row));
    }

    File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    return await Task.FromResult(filePath);
}
```

---

### Python: Excel with openpyxl
```python
from openpyxl import Workbook
from openpyxl.styles import PatternFill, Font

def write_excel(base_dir, results):
    wb = Workbook()
    ws = wb.active
    
    ws['A1'] = 'Path'
    ws['B1'] = 'Status'
    
    for i, result in enumerate(results, 2):
        ws[f'A{i}'] = result['path']
        ws[f'B{i}'] = result['status']
    
    wb.save(f"report_{date}.xlsx")
```

### C#: Excel with EPPlus
```csharp
public async Task<string> WriteExcelAsync(AuditSummary summary)
{
    EPPlus.LicenseContext.SetLicense(LicenseContext.NonCommercial);

    var filePath = Path.Combine(
        summary.AuditFolder,
        $"{summary.SafeSiteName}_audit_report_{summary.RunDate}.xlsx");

    using (var package = new ExcelPackage())
    {
        var resultsSheet = package.Workbook.Worksheets.Add("Results");
        var headers = new[] { "Path", "Status", "Score", "Root Cause" };
        
        for (int i = 0; i < headers.Length; i++)
        {
            resultsSheet.Cells[1, i + 1].Value = headers[i];
            resultsSheet.Cells[1, i + 1].Style.Font.Bold = true;
        }

        int row = 2;
        foreach (var result in summary.AllResults)
        {
            resultsSheet.Cells[row, 1].Value = result.Path;
            resultsSheet.Cells[row, 2].Value = result.Status;
            resultsSheet.Cells[row, 3].Value = result.Score;
            resultsSheet.Cells[row, 4].Value = result.RootCause;
            row++;
        }

        package.SaveAs(new FileInfo(filePath));
    }

    return await Task.FromResult(filePath);
}
```

---

## Summary of Mappings

| Python | C# Equivalent |
|--------|---------------|
| `def` | `public void/async Task` |
| `asyncio.Semaphore` | `SemaphoreSlim` |
| `requests.get()` | `HttpClient.GetAsync()` |
| `urlparse()` | `Uri.TryCreate()` |
| `dict` | `Dictionary<K, V>` |
| `list` | `List<T>` |
| `set` | `HashSet<T>` |
| `.format()` | `$"string {var}"` |
| `csv.DictWriter` | `StringBuilder + string.Join()` |
| `openpyxl.Workbook` | `ExcelPackage` |
| `@property` | `{ get; set; }` |
| `if __name__ == '__main__'` | `Program.cs Main()` |
| `try/except` | `try/catch` |
| `print()` | `Console.WriteLine()` or `Debug.WriteLine()` |
| `datetime.now()` | `DateTime.Now` |
| `os.makedirs()` | `Directory.CreateDirectory()` |
| `json.loads()` | `JsonConvert.DeserializeObject()` |

---

## Key Design Differences

1. **Dependency Injection**: C# uses DI container; Python uses globals
2. **Type Safety**: C# has compile-time type checking; Python is dynamic
3. **Async Model**: Python `asyncio`; C# `async/await` (simpler, more intuitive)
4. **Configuration**: Python `argparse`; C# form binding + classes
5. **Reporting**: Python `print()`; C# services + UI components
6. **Progress**: Python console output; C# `IProgress<T>` for cross-layer communication

All functionality is preserved while leveraging C# and ASP.NET Core patterns!
