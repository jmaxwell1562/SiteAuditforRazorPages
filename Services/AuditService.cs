using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AuditApp.Models;

namespace AuditApp.Services
{
    /// <summary>
    /// Main audit orchestration service
    /// Coordinates URL discovery, page comparison, result analysis, and file generation
    /// </summary>
    public class AuditService
    {
        private readonly UrlUtilityService _urlService;
        private readonly AuditAnalysisService _analysisService;
        private readonly PageComparisonService _comparisonService;
        private readonly ReportGenerationService _reportService;

        public AuditService(
            UrlUtilityService urlService,
            AuditAnalysisService analysisService,
            PageComparisonService comparisonService,
            ReportGenerationService reportService)
        {
            _urlService = urlService;
            _analysisService = analysisService;
            _comparisonService = comparisonService;
            _reportService = reportService;
        }

        /// <summary>
        /// Main audit entry point
        /// </summary>
        public async Task<AuditSummary> RunAuditAsync(AuditConfig config, IProgress<string> progress = null)
        {
            var summary = new AuditSummary
            {
                SiteName = config.SiteName,
                SafeSiteName = _urlService.SanitizeSiteName(config.SiteName),
                RunDate = DateTime.Now.ToString("yyyyMMdd"),
                Timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss")
            };

            progress?.Report($"Starting audit for {config.SiteName}...");

            try
            {
                // Setup
                var testBase = ResolveTestBase(config);
                progress?.Report($"Test base: {testBase}");

                summary.AuditFolder = Path.Combine("Audits", $"Audit_{summary.SafeSiteName}_{summary.Timestamp}");
                Directory.CreateDirectory(summary.AuditFolder);

                // Get redirect override paths
                var redirectOverrides = ParseRedirectOverrides(config.RedirectOverridePaths);
                progress?.Report($"Redirect overrides: {redirectOverrides.Count} paths");

                // Discover paths (sitemap, crawling, etc.)
                var paths = await DiscoverPathsAsync(config.SourceUrl, testBase, config.MaxPaths, progress);
                progress?.Report($"Discovered {paths.Count} paths");

                // Auto-correct host mismatch if needed
                testBase = await _urlService.MaybeCorrectTestBaseAsync(config.SourceUrl, testBase, paths);

                // Discover test bases for instance scope
                var testBases = await DiscoverTestBasesAsync(config.TestScope, testBase, config.TestAllowlist, config.TestAllowlistFile, progress);
                progress?.Report($"Test bases: {string.Join(", ", testBases.Select(t => t.Label))}");

                // Audit each path
                var results = await AuditPathsAsync(config.SourceUrl, testBases, paths, redirectOverrides, config.MaxTabs, progress);
                progress?.Report($"Audited {results.Count} paths");

                // Analyze results
                var (annotated, clusters) = _analysisService.ClusterFailures(results);
                summary.AllResults = _analysisService.SortResults(annotated);
                summary.Clusters = clusters;

                var (queueA, queueB) = _analysisService.BuildQueues(summary.AllResults);
                summary.QueueA = queueA;
                summary.QueueB = queueB;

                // Count results
                var counts = _analysisService.CountByStatus(summary.AllResults);
                summary.PassCount = counts["PASS"];
                summary.SoftPassCount = counts["SOFT PASS"];
                summary.ReviewCount = counts["REVIEW"];
                summary.FailCount = counts["FAIL"];
                summary.RedirectCount = counts["SKIP"];
                summary.ErrorCount = counts["ERROR"];

                // Readiness
                summary.Readiness = _analysisService.CalculateReadiness(summary.AllResults);

                // Generate reports
                progress?.Report("Generating reports...");
                await GenerateReportsAsync(summary, progress);

                progress?.Report($"✓ Audit complete: {summary.AuditFolder}");
                return summary;
            }
            catch (Exception ex)
            {
                progress?.Report($"✗ Audit failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Resolve test base URL from config
        /// </summary>
        private string ResolveTestBase(AuditConfig config)
        {
            if (!string.IsNullOrEmpty(config.TestUrl))
                return _urlService.NormalizeSiteBase(config.TestUrl);

            if (!string.IsNullOrEmpty(config.TestSlug) && config.TestSlug.StartsWith("http"))
                return _urlService.NormalizeSiteBase(config.TestSlug);

            // Legacy: construct w2-testing URL
            string slug = (config.TestSlug ?? "/").Trim('/');
            return $"https://w2-testing.asis.wsu.edu/{slug}";
        }

        /// <summary>
        /// Parse redirect override paths
        /// </summary>
        private HashSet<string> ParseRedirectOverrides(string raw)
        {
            var overrides = new HashSet<string> { _urlService.NormalizePath("/nutrition/net-nutrition/") };

            if (string.IsNullOrEmpty(raw))
                return overrides;

            foreach (var item in raw.Split(','))
            {
                string trimmed = item.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    overrides.Add(_urlService.NormalizePath(trimmed));
            }

            return overrides;
        }

        /// <summary>
        /// Discover paths from sitemap or crawling
        /// </summary>
        private async Task<List<string>> DiscoverPathsAsync(string sourceUrl, string testBase, int maxPaths, IProgress<string> progress)
        {
            var paths = new List<string> { "/" };

            // TODO: Implement sitemap parsing
            // For now, return basic paths
            if (maxPaths > 0)
                paths = paths.Take(maxPaths).ToList();

            return await Task.FromResult(paths);
        }

        /// <summary>
        /// Discover test base URLs for instance scope
        /// </summary>
        private async Task<List<TestBase>> DiscoverTestBasesAsync(
            string scope, string seedBase, string allowlist, string allowlistFile, IProgress<string> progress)
        {
            var testBases = new List<TestBase>
            {
                new TestBase { Url = seedBase, Label = _urlService.TestSiteLabel(seedBase) }
            };

            if (scope == "single")
                return await Task.FromResult(testBases);

            // TODO: Implement instance scope discovery
            // Parse allowlist and filter
            var (labels, urls) = ParseAllowlist(allowlist, allowlistFile);

            return await Task.FromResult(testBases);
        }

        /// <summary>
        /// Parse allowlist from string and file
        /// </summary>
        private (HashSet<string> labels, HashSet<string> urls) ParseAllowlist(string raw, string filePath)
        {
            var labels = new HashSet<string>();
            var urls = new HashSet<string>();

            if (!string.IsNullOrEmpty(raw))
            {
                foreach (var item in raw.Split(','))
                {
                    string token = item.Trim().TrimEnd('/');
                    if (token.StartsWith("http://") || token.StartsWith("https://"))
                        urls.Add(token.ToLower());
                    else
                        labels.Add(token.ToLower());
                }
            }

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    foreach (var line in File.ReadLines(filePath, Encoding.UTF8))
                    {
                        string cleaned = line.Trim();
                        if (string.IsNullOrEmpty(cleaned) || cleaned.StartsWith("#"))
                            continue;

                        foreach (var item in cleaned.Split(','))
                        {
                            string token = item.Trim().TrimEnd('/');
                            if (token.StartsWith("http://") || token.StartsWith("https://"))
                                urls.Add(token.ToLower());
                            else
                                labels.Add(token.ToLower());
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WARN] Failed to read allowlist file: {ex.Message}");
                }
            }

            return (labels, urls);
        }

        /// <summary>
        /// Audit all paths across all test bases
        /// </summary>
        private async Task<List<AuditResult>> AuditPathsAsync(
            string sourceBase,
            List<TestBase> testBases,
            List<string> paths,
            HashSet<string> redirectOverrides,
            int maxConcurrency,
            IProgress<string> progress)
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
                            var result = await AuditPageAsync(sourceBase, testBase.Url, path, testBase.Label, redirectOverrides);
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

        /// <summary>
        /// Audit a single page
        /// </summary>
        private async Task<AuditResult> AuditPageAsync(
            string sourceBase, string testBase, string path, string testSiteLabel, HashSet<string> redirectOverrides)
        {
            var result = new AuditResult
            {
                Path = path,
                TestSite = testSiteLabel,
                Section = _analysisService.SectionFromPath(path)
            };

            try
            {
                // Construct URLs
                var sourceUrl = !string.IsNullOrEmpty(sourceBase) ? sourceBase + path : null;
                var testUrl = _urlService.JoinTestUrl(testBase, path, sourceBase);

                result.SourceUrl = sourceUrl;
                result.TestUrl = testUrl;

                // Check if redirect is expected
                if (redirectOverrides.Contains(_urlService.NormalizePath(path)))
                {
                    result.Status = "SKIP";
                    result.Note = "Intentional redirect override";
                    result.IsRedirect = true;
                    return result;
                }

                // Probe source
                var (sourceStatus, sourceRedirect) = sourceUrl != null
                    ? await _urlService.ProbeUrlAsync(sourceUrl)
                    : (null, false);
                result.SourceStatus = sourceStatus;

                // Probe test
                var (testStatus, testRedirect) = await _urlService.ProbeUrlAsync(testUrl);
                result.TestStatus = testStatus;
                result.IsRedirect = testRedirect;

                // Determine status
                if (testRedirect)
                {
                    result.Status = "SKIP";
                    result.Note = "Test URL redirects";
                    return result;
                }

                if (testStatus != null && testStatus >= 400)
                {
                    result.Status = "FAIL";
                    result.Score = 0.0;
                    result.RootCause = _analysisService.DetermineRootCause("FAIL", 0.0, sourceStatus, testStatus, false);
                    return result;
                }

                // Fetch and compare content (if applicable)
                if (sourceStatus == 200 && testStatus == 200)
                {
                    var score = await _comparisonService.CompareUrlsAsync(sourceUrl, testUrl);
                    result.Score = score;

                    if (score >= AuditAnalysisService.PassThreshold)
                        result.Status = "PASS";
                    else if (score >= AuditAnalysisService.SoftPassThreshold)
                        result.Status = "SOFT PASS";
                    else
                        result.Status = "REVIEW";

                    result.RootCause = _analysisService.DetermineRootCause(result.Status, score, sourceStatus, testStatus, false);
                }
                else
                {
                    result.Status = "ERROR";
                    result.RootCause = _analysisService.DetermineRootCause("ERROR", 0.0, sourceStatus, testStatus, false);
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Status = "ERROR";
                result.Note = ex.Message.Substring(0, Math.Min(200, ex.Message.Length));
                result.RootCause = "Exception during audit";
                return result;
            }
        }

        /// <summary>
        /// Generate CSV, Excel, and HTML reports
        /// </summary>
        private async Task GenerateReportsAsync(AuditSummary summary, IProgress<string> progress)
        {
            progress?.Report("Writing CSV...");
            summary.MainCsvPath = await _reportService.WriteCsvAsync(summary);

            progress?.Report("Writing cluster summary...");
            summary.ClusterCsvPath = await _reportService.WriteClusterSummaryAsync(summary);

            progress?.Report("Writing readiness summary...");
            summary.ReadinessCsvPath = await _reportService.WriteReadinessSummaryAsync(summary);

            progress?.Report("Writing Excel report...");
            summary.XlsxPath = await _reportService.WriteExcelAsync(summary);

            progress?.Report("Writing HTML report...");
            summary.HtmlReportPath = await _reportService.WriteHtmlAsync(summary);
        }
    }
}
