using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AuditApp.Models;
using AuditApp.Services;

namespace AuditApp.Pages
{
    public class AuditModel : PageModel
    {
        private const string CustomTestUrlOptionValue = "__custom__";
        private static readonly Regex TimestampedReportPattern = new(
            @"^(?<site>.+?)_audit_report_(?<stamp>\d{8}(?:_\d{6})?)\.html$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] TestUrlOptions =
        {
            "https://wdev3-testing.asis.wsu.edu/",
            "http://asis-wdev1.ad.wsu.edu/",
            "https://w2-testing.asis.wsu.edu/",
            "https://w3-testing.asis.wsu.edu/",
            "https://stepone.wsu.edu/",
            "https://dev.cub.wsu.edu/",
            "https://u7.dev.urec.wsu.edu/",
            "https://localhost:7019/"
        };

        private readonly AuditService _auditService;
        private readonly UrlUtilityService _urlService;

        [BindProperty]
        public AuditConfig Config { get; set; } = new();

        [BindProperty]
        public bool IsRunning { get; set; }

        [BindProperty]
        public string CustomTestUrl { get; set; } = string.Empty;

        public AuditSummary LastSummary { get; set; }
        public ReportHistoryEntry LatestReport { get; set; }
        public ReportHistoryEntry PreviousReport { get; set; }
        public ReportHistoryComparison PreviousComparison { get; set; } = new();
        public List<string> StatusMessages { get; set; } = new();
        public string RunButtonState { get; set; } = "Ready";
        public string RunDurationNotice { get; } = "Quick runs usually finish in a few minutes. Full-site runs can take 10 to 30+ minutes before reports appear.";
        public IReadOnlyList<string> AvailableTestUrls => TestUrlOptions;
        public string CustomTestUrlOption => CustomTestUrlOptionValue;
        public bool UsesCustomTestUrl => !string.IsNullOrWhiteSpace(CustomTestUrl);

        public AuditModel(AuditService auditService, UrlUtilityService urlService)
        {
            _auditService = auditService;
            _urlService = urlService;
        }

        public void OnGet()
        {
            DisableCaching();
            InitializeDefaults();
            LoadReportHistory(Config.SiteName);
        }

        public async Task<IActionResult> OnPostStartAuditAsync()
        {
            if (!HasRequiredInputs())
            {
                InitializeDefaults();
                LoadReportHistory(Config.SiteName);
                ModelState.AddModelError("", GetRequiredInputMessage());
                return Page();
            }

            IsRunning = true;
            RunButtonState = "Running";
            LoadReportHistory(Config.SiteName);

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    StatusMessages.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
                });

                if (HasLegacyBatchSiteMappings())
                {
                    var batchResults = await RunBatchAuditAsync(progress);
                    var successCount = batchResults.Count(result => result.IsSuccess);
                    StatusMessages.Add(successCount > 0
                        ? $"✓ Batch audit complete. Generated {successCount} report set(s)."
                        : "✗ Batch audit did not generate any reports.");
                }
                else
                {
                    LastSummary = await _auditService.RunAuditAsync(Config, progress);
                    LoadReportHistory(Config.SiteName);
                    StatusMessages.Add("✓ Audit complete!");
                }

                IsRunning = false;
                RunButtonState = "Complete";
            }
            catch (Exception ex)
            {
                var friendlyMessage = GetFriendlyErrorMessage(ex);
                IsRunning = false;
                RunButtonState = "Error";
                StatusMessages.Add($"✗ Error: {friendlyMessage}");
                ModelState.AddModelError("", friendlyMessage);
            }

            return Page();
        }

        public IActionResult OnGetReportHistory(string siteName)
        {
            DisableCaching();
            var reports = ResolveReportHistory(siteName);
            var latest = reports.FirstOrDefault();
            var previous = reports.Skip(1).FirstOrDefault();
            var comparison = BuildComparison(latest, previous);

            return new JsonResult(new
            {
                hasReports = latest != null,
                latest,
                previous,
                comparison
            });
        }

        /// <summary>
        /// API endpoint for AJAX status updates
        /// </summary>
        public async Task<IActionResult> OnPostStartAuditApiAsync()
        {
            DisableCaching();

            if (!HasRequiredInputs())
            {
                return new JsonResult(new
                {
                    success = false,
                    messages = new[] { GetRequiredInputMessage() },
                    error = GetRequiredInputMessage()
                });
            }

            var messages = new List<string>();
            ReportHistoryEntry previousLatest = null;
            var progress = new Progress<string>(msg =>
            {
                messages.Add(msg);
            });

            try
            {
                if (HasLegacyBatchSiteMappings())
                {
                    var batchResults = await RunBatchAuditAsync(progress);
                    var successfulResults = batchResults.Where(result => result.IsSuccess).ToList();
                    if (successfulResults.Count == 0)
                    {
                        throw new InvalidOperationException("Batch audit did not publish any new reports.");
                    }

                    return new JsonResult(new
                    {
                        success = true,
                        batchMode = true,
                        hasReports = false,
                        messages,
                        batchReports = successfulResults.Select(result => new
                        {
                            siteName = result.SiteName,
                            generatedAtDisplay = result.LatestReport?.GeneratedAtDisplay ?? string.Empty,
                            htmlUrl = result.LatestReport?.HtmlUrl,
                            executiveHtmlUrl = result.LatestReport?.ExecutiveHtmlUrl,
                            xlsxUrl = result.LatestReport?.XlsxUrl,
                            passCount = result.LatestReport?.PassCount ?? 0,
                            reviewCount = result.LatestReport?.ReviewCount ?? 0,
                            failCount = result.LatestReport?.FailCount ?? 0,
                            redirectCount = result.LatestReport?.RedirectCount ?? 0,
                            error = result.ErrorMessage
                        })
                    });
                }

                previousLatest = ResolveReportHistory(Config.SiteName).FirstOrDefault();
                var summary = await _auditService.RunAuditAsync(Config, progress);
                if (!HasGeneratedArtifacts(summary))
                {
                    throw new InvalidOperationException("Audit completed without generating a new HTML report, CSV, and Excel report.");
                }

                var reports = ResolveReportHistory(Config.SiteName);
                var latest = reports.FirstOrDefault();
                var previous = reports.Skip(1).FirstOrDefault();

                if (latest == null || previousLatest?.TimestampToken == latest.TimestampToken)
                {
                    throw new InvalidOperationException("Audit did not publish a new report for the selected site.");
                }

                return new JsonResult(new
                {
                    success = true,
                    batchMode = false,
                    hasReports = latest != null,
                    messages,
                    latest,
                    previous,
                    comparison = BuildComparison(latest, previous),
                    summary = new
                    {
                        summary.SiteName,
                        summary.RunDate,
                        summary.Timestamp,
                        summary.PassCount,
                        summary.SoftPassCount,
                        summary.FailCount,
                        summary.ReviewCount,
                        summary.RedirectCount,
                        summary.AuditFolder,
                        summary.MainCsvPath,
                        summary.HtmlReportPath,
                        summary.XlsxPath
                    }
                });
            }
            catch (Exception ex)
            {
                var friendlyMessage = GetFriendlyErrorMessage(ex);
                messages.Add($"Error: {friendlyMessage}");
                return new JsonResult(new
                {
                    success = false,
                    messages,
                    error = friendlyMessage
                });
            }
        }

        private static string GetFriendlyErrorMessage(Exception ex)
        {
            var message = ex.Message ?? string.Empty;
            if (message.Contains("used by another process", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Unable to access report file", StringComparison.OrdinalIgnoreCase))
            {
                return "A report file is open in Excel or another app. Close that file, or rename the open workbook, and run the audit again.";
            }

            return message;
        }

        private void InitializeDefaults()
        {
            NormalizeConfigUrls();
            NormalizeTestScopeSelection();
            NormalizeTestUrlSelection();
            Config.MaxTabs = Config.MaxTabs <= 0 ? 5 : Config.MaxTabs;
            Config.MaxPaths = Config.MaxPaths < 0 ? 0 : Config.MaxPaths;
            RunButtonState = IsRunning ? "Running" : "Ready";
        }

        private bool HasRequiredInputs()
        {
            NormalizeConfigUrls();
            NormalizeTestScopeSelection();
            NormalizeTestUrlSelection();
            if (IsLegacyBatchModeSelected())
                return !string.IsNullOrWhiteSpace(Config.TestUrl)
                    && !string.IsNullOrWhiteSpace(Config.BatchSiteMappingsFile);

            return !string.IsNullOrWhiteSpace(Config.SiteName)
                && !string.IsNullOrWhiteSpace(Config.SourceUrl)
                && !string.IsNullOrWhiteSpace(Config.TestUrl);
        }

        private string GetRequiredInputMessage()
        {
            return IsLegacyBatchModeSelected()
                ? "Test URL and Batch Site Mappings File are required for external batch runs."
                : "Site Name, Source URL, and Test URL are required.";
        }

        private void NormalizeTestScopeSelection()
        {
            var normalizedScope = (Config.TestScope ?? string.Empty).Trim().ToLowerInvariant();
            Config.TestScope = normalizedScope switch
            {
                "batch" => "batch",
                "instance" => "instance",
                "ask" => "single",
                _ => "single"
            };
        }

        private bool IsInstanceModeSelected()
        {
            return string.Equals(Config.TestScope, "instance", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsLegacyBatchModeSelected()
        {
            return string.Equals(Config.TestScope, "batch", StringComparison.OrdinalIgnoreCase);
        }

        private bool HasLegacyBatchSiteMappings()
        {
            return IsLegacyBatchModeSelected() && !string.IsNullOrWhiteSpace(Config.BatchSiteMappingsFile);
        }

        private void NormalizeTestUrlSelection()
        {
            var configuredValue = Config.TestUrl?.Trim() ?? string.Empty;
            var customValue = CustomTestUrl?.Trim() ?? string.Empty;

            if (string.Equals(configuredValue, CustomTestUrlOptionValue, StringComparison.Ordinal))
            {
                Config.TestUrl = customValue;
            }
            else if (!string.IsNullOrWhiteSpace(configuredValue) && !IsPresetTestUrl(configuredValue))
            {
                CustomTestUrl = configuredValue;
            }
            else if (!string.IsNullOrWhiteSpace(customValue) && string.IsNullOrWhiteSpace(configuredValue))
            {
                Config.TestUrl = customValue;
            }

            if (IsPresetTestUrl(Config.TestUrl))
            {
                CustomTestUrl = string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(Config.TestUrl))
            {
                CustomTestUrl = Config.TestUrl;
            }
        }

        private void NormalizeConfigUrls()
        {
            Config.SourceUrl = NormalizeUrlInput(Config.SourceUrl);
            Config.TestUrl = NormalizeUrlInput(Config.TestUrl);
            CustomTestUrl = NormalizeUrlInput(CustomTestUrl);
        }

        private string NormalizeUrlInput(string value)
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
                return string.Empty;

            if (string.Equals(trimmed, CustomTestUrlOptionValue, StringComparison.Ordinal))
                return trimmed;

            return _urlService.NormalizeSiteBase(trimmed);
        }

        private static bool IsPresetTestUrl(string value)
        {
            return TestUrlOptions.Any(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasGeneratedArtifacts(AuditSummary summary)
        {
            return summary != null
                && !string.IsNullOrWhiteSpace(summary.HtmlReportPath)
                && !string.IsNullOrWhiteSpace(summary.MainCsvPath)
                && !string.IsNullOrWhiteSpace(summary.XlsxPath)
                && System.IO.File.Exists(summary.HtmlReportPath)
                && System.IO.File.Exists(summary.MainCsvPath)
                && System.IO.File.Exists(summary.XlsxPath);
        }

        private void DisableCaching()
        {
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";
        }

        private void LoadReportHistory(string siteName)
        {
            var reports = ResolveReportHistory(siteName);
            LatestReport = reports.FirstOrDefault();
            PreviousReport = reports.Skip(1).FirstOrDefault();
            PreviousComparison = BuildComparison(LatestReport, PreviousReport);
        }

        private List<ReportHistoryEntry> ResolveReportHistory(string siteName)
        {
            var siteKey = NormalizeSiteKey(siteName);
            if (string.IsNullOrEmpty(siteKey))
                return new List<ReportHistoryEntry>();

            var auditsRoot = Path.Combine(Directory.GetCurrentDirectory(), "Audits");
            if (!Directory.Exists(auditsRoot))
                return new List<ReportHistoryEntry>();

            return Directory
                .EnumerateFiles(auditsRoot, "*_audit_report_*.html", SearchOption.AllDirectories)
                .Select(BuildHistoryEntry)
                .Where(entry => entry != null && NormalizeSiteKey(entry.SiteName) == siteKey)
                .OrderByDescending(entry => entry.GeneratedAt)
                .ToList();
        }

        private ReportHistoryEntry BuildHistoryEntry(string htmlPath)
        {
            var fileName = Path.GetFileName(htmlPath);
            var match = TimestampedReportPattern.Match(fileName);
            if (!match.Success)
                return null;

            var siteName = match.Groups["site"].Value;
            var stamp = match.Groups["stamp"].Value;
            var auditFolder = Path.GetDirectoryName(htmlPath) ?? string.Empty;
            var executivePath = Path.Combine(auditFolder, $"{siteName}_executive_view_{stamp}.html");
            var csvPath = Path.Combine(auditFolder, $"{siteName}_audit_report_{stamp}.csv");
            var xlsxPath = Path.Combine(auditFolder, $"{siteName}_audit_report_{stamp}.xlsx");

            var entry = new ReportHistoryEntry
            {
                SiteName = siteName.Replace('_', ' '),
                AuditFolder = auditFolder,
                HtmlFilePath = htmlPath,
                HtmlFileName = fileName,
                HtmlUrl = $"/reports/{fileName}",
                ExecutiveHtmlPath = System.IO.File.Exists(executivePath) ? executivePath : null,
                ExecutiveHtmlUrl = System.IO.File.Exists(executivePath) ? $"/reports/{Path.GetFileName(executivePath)}" : null,
                MainCsvPath = System.IO.File.Exists(csvPath) ? csvPath : null,
                MainCsvUrl = System.IO.File.Exists(csvPath) ? $"/reports/{Path.GetFileName(csvPath)}" : null,
                XlsxPath = System.IO.File.Exists(xlsxPath) ? xlsxPath : null,
                XlsxUrl = System.IO.File.Exists(xlsxPath) ? $"/reports/{Path.GetFileName(xlsxPath)}" : null,
                TimestampToken = stamp,
                GeneratedAt = ParseTimestamp(stamp)
            };

            PopulateCounts(entry);
            return entry;
        }

        private void PopulateCounts(ReportHistoryEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.MainCsvPath) || !System.IO.File.Exists(entry.MainCsvPath))
                return;

            using var stream = new FileStream(entry.MainCsvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            _ = reader.ReadLine();

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var columns = line.Split(',');
                if (columns.Length < 8)
                    continue;

                var status = columns[7].Trim().Trim('"');
                switch (status)
                {
                    case "PASS":
                        entry.PassCount++;
                        break;
                    case "SOFT PASS":
                        entry.SoftPassCount++;
                        break;
                    case "REVIEW":
                        entry.ReviewCount++;
                        break;
                    case "FAIL":
                        entry.FailCount++;
                        break;
                    case "SKIP":
                        entry.RedirectCount++;
                        break;
                }
            }
        }

        private static ReportHistoryComparison BuildComparison(ReportHistoryEntry latest, ReportHistoryEntry previous)
        {
            if (latest == null || previous == null)
                return new ReportHistoryComparison();

            return new ReportHistoryComparison
            {
                PassDelta = latest.PassCount - previous.PassCount,
                ReviewDelta = latest.ReviewCount - previous.ReviewCount,
                FailDelta = latest.FailCount - previous.FailCount
            };
        }

        private static DateTime ParseTimestamp(string stamp)
        {
            if (DateTime.TryParseExact(
                    stamp,
                    new[] { "yyyyMMdd_HHmmss", "yyyyMMdd" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                return parsed;
            }

            return DateTime.MinValue;
        }

        private static string NormalizeSiteKey(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9]+", string.Empty).ToLowerInvariant();
        }

        private async Task<List<BatchAuditRunResult>> RunBatchAuditAsync(IProgress<string> progress)
        {
            var batchPlans = ParseBatchSiteMappings();
            if (batchPlans.Count == 0)
                throw new InvalidOperationException("Add at least one valid Batch Site Mappings entry before starting the audit.");

            var results = new List<BatchAuditRunResult>();
            foreach (var plan in batchPlans)
            {
                progress?.Report($"Running batch audit for {plan.SiteName}...");

                try
                {
                    var batchConfig = new AuditConfig
                    {
                        SiteName = plan.SiteName,
                        SourceUrl = plan.SourceUrl,
                        TestUrl = plan.TestUrl,
                        SourceIsSubdomain = plan.SourceIsSubdomain,
                        TestSlug = "/",
                        TestScope = "single",
                        RedirectOverridePaths = Config.RedirectOverridePaths,
                        MaxTabs = Config.MaxTabs,
                        MaxPaths = Config.MaxPaths
                    };

                    var previousLatest = ResolveReportHistory(plan.SiteName).FirstOrDefault();
                    var summary = await _auditService.RunAuditAsync(batchConfig, progress);
                    if (!HasGeneratedArtifacts(summary))
                        throw new InvalidOperationException($"{plan.SiteName} completed without generating a new HTML report, CSV, and Excel report.");

                    var latest = ResolveReportHistory(plan.SiteName).FirstOrDefault();
                    if (latest == null || previousLatest?.TimestampToken == latest.TimestampToken)
                        throw new InvalidOperationException($"{plan.SiteName} did not publish a new report.");

                    results.Add(new BatchAuditRunResult
                    {
                        SiteName = plan.SiteName,
                        LatestReport = latest,
                        IsSuccess = true
                    });

                    progress?.Report($"✓ Batch audit complete for {plan.SiteName}.");
                }
                catch (Exception ex)
                {
                    var friendlyMessage = GetFriendlyErrorMessage(ex);
                    results.Add(new BatchAuditRunResult
                    {
                        SiteName = plan.SiteName,
                        IsSuccess = false,
                        ErrorMessage = friendlyMessage
                    });
                    progress?.Report($"Error: {plan.SiteName}: {friendlyMessage}");
                }
            }

            return results;
        }

        private List<BatchAuditPlanEntry> ParseBatchSiteMappings()
        {
            var entries = new List<BatchAuditPlanEntry>();
            if (string.IsNullOrWhiteSpace(Config.BatchSiteMappingsFile))
                return entries;

            if (!System.IO.File.Exists(Config.BatchSiteMappingsFile))
                throw new InvalidOperationException($"Batch Site Mappings File not found: {Config.BatchSiteMappingsFile}");

            var lines = System.IO.File.ReadLines(Config.BatchSiteMappingsFile)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
                .ToList();

            foreach (var line in lines)
            {
                var parts = line.Contains('|')
                    ? line.Split('|')
                    : line.Split('\t');

                var trimmedParts = parts.Select(part => part.Trim()).Where(part => !string.IsNullOrWhiteSpace(part)).ToList();
                if (trimmedParts.Count < 2)
                    throw new InvalidOperationException($"Invalid Batch Site Mappings entry: '{line}'. Use 'Site Name | Source URL | Test Path or Full Test URL'.");

                var siteName = trimmedParts[0];
                var sourceUrl = trimmedParts[1];
                var testTarget = trimmedParts.Count >= 3 ? trimmedParts[2] : string.Empty;

                entries.Add(new BatchAuditPlanEntry
                {
                    SiteName = siteName,
                    SourceUrl = sourceUrl,
                    TestUrl = ResolveBatchTestUrl(Config.TestUrl, sourceUrl, testTarget),
                    SourceIsSubdomain = ShouldPreserveBatchPathPrefix(testTarget)
                });
            }

            return entries;
        }

        private static bool ShouldPreserveBatchPathPrefix(string testTarget)
        {
            if (string.IsNullOrWhiteSpace(testTarget))
                return false;

            if (!LooksLikeAbsoluteUrl(testTarget))
                return true;

            return Uri.TryCreate(testTarget, UriKind.Absolute, out var targetUri)
                && !string.IsNullOrWhiteSpace(targetUri.AbsolutePath.Trim('/'));
        }

        private static string ResolveBatchTestUrl(string baseTestUrl, string sourceUrl, string testTarget)
        {
            if (LooksLikeAbsoluteUrl(testTarget))
                return testTarget;

            var normalizedBase = (baseTestUrl ?? string.Empty).Trim();
            if (!Uri.TryCreate(normalizedBase, UriKind.Absolute, out var baseUri))
                return normalizedBase;

            var hostRoot = $"{baseUri.Scheme}://{baseUri.Host}";
            if (baseUri.Port != 80 && baseUri.Port != 443 && baseUri.Port != -1)
                hostRoot += $":{baseUri.Port}";

            var targetPath = !string.IsNullOrWhiteSpace(testTarget)
                ? testTarget.Trim()
                : DeriveDefaultBatchPath(sourceUrl);

            targetPath = targetPath.Trim('/');
            return string.IsNullOrWhiteSpace(targetPath)
                ? hostRoot
                : $"{hostRoot}/{targetPath}";
        }

        private static string DeriveDefaultBatchPath(string sourceUrl)
        {
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri))
                return string.Empty;

            var hostParts = sourceUri.Host.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return hostParts.Length > 0 ? hostParts[0] : string.Empty;
        }

        private static bool LooksLikeAbsoluteUrl(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private sealed class BatchAuditPlanEntry
        {
            public string SiteName { get; set; }
            public string SourceUrl { get; set; }
            public string TestUrl { get; set; }
            public bool SourceIsSubdomain { get; set; }
        }

        private sealed class BatchAuditRunResult
        {
            public string SiteName { get; set; }
            public ReportHistoryEntry LatestReport { get; set; }
            public bool IsSuccess { get; set; }
            public string ErrorMessage { get; set; }
        }
    }
}
