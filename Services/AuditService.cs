using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AuditApp.Models;
using HtmlAgilityPack;
using System.Xml.Linq;

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
                testBase = await _urlService.MaybeCorrectTestBaseAsync(config.SourceUrl, testBase, paths, config.SourceIsSubdomain);

                // Discover test bases for instance scope
                var testBases = await DiscoverTestBasesAsync(config.TestScope, testBase, config.TestAllowlist, config.TestAllowlistFile, progress);
                progress?.Report($"Test bases: {string.Join(", ", testBases.Select(t => t.Label))}");

                // Audit each path
                var results = await AuditPathsAsync(config.SourceUrl, testBases, paths, redirectOverrides, config.MaxTabs, config.SourceIsSubdomain, progress);
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
            var discoveredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/" };
            var normalizedSource = _urlService.NormalizeSiteBase(sourceUrl);
            var discoveryLimit = maxPaths > 0 ? maxPaths : 5000;

            if (string.IsNullOrWhiteSpace(normalizedSource))
                return discoveredPaths.ToList();

            progress?.Report("Discovering paths from sitemap...");
            var sitemapCount = await DiscoverPathsFromSitemapAsync(normalizedSource, discoveredPaths, discoveryLimit, progress);

            if (sitemapCount <= 1)
            {
                progress?.Report("Sitemap did not yield enough paths; crawling source links...");
                await CrawlSourcePathsAsync(normalizedSource, discoveredPaths, discoveryLimit, progress);
            }

            var orderedPaths = discoveredPaths
                .Select(_urlService.NormalizePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path == "/" ? 0 : 1)
                .ThenBy(path => path.Length)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (maxPaths > 0)
                orderedPaths = orderedPaths.Take(maxPaths).ToList();

            return orderedPaths;
        }

        private async Task<int> DiscoverPathsFromSitemapAsync(string sourceBase, HashSet<string> paths, int limit, IProgress<string> progress)
        {
            var visitedSitemaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidates = new[]
            {
                _urlService.JoinSourceUrl(sourceBase, "/sitemap"),
                _urlService.JoinSourceUrl(sourceBase, "/sitemap.xml"),
                _urlService.JoinSourceUrl(sourceBase, "/sitemap_index.xml")
            };

            foreach (var sitemapUrl in candidates)
            {
                await ReadSitemapRecursiveAsync(sitemapUrl, sourceBase, paths, visitedSitemaps, limit, progress);
                if (paths.Count >= limit)
                    break;
            }

            return paths.Count;
        }

        private async Task ReadSitemapRecursiveAsync(
            string sitemapUrl,
            string sourceBase,
            HashSet<string> paths,
            HashSet<string> visitedSitemaps,
            int limit,
            IProgress<string> progress)
        {
            if (string.IsNullOrWhiteSpace(sitemapUrl)
                || paths.Count >= limit
                || !visitedSitemaps.Add(sitemapUrl))
            {
                return;
            }

            var content = await FetchTextAsync(sitemapUrl);
            if (string.IsNullOrWhiteSpace(content))
                return;

            try
            {
                var document = XDocument.Parse(content);
                var rootName = document.Root?.Name.LocalName;
                if (string.Equals(rootName, "sitemapindex", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var loc in document.Descendants().Where(node => node.Name.LocalName == "loc"))
                    {
                        await ReadSitemapRecursiveAsync(loc.Value.Trim(), sourceBase, paths, visitedSitemaps, limit, progress);
                        if (paths.Count >= limit)
                            break;
                    }

                    return;
                }

                if (!string.Equals(rootName, "urlset", StringComparison.OrdinalIgnoreCase))
                    return;

                foreach (var loc in document.Descendants().Where(node => node.Name.LocalName == "loc"))
                {
                    if (TryNormalizeSourcePath(sourceBase, loc.Value, out var path))
                    {
                        paths.Add(path);
                        if (paths.Count >= limit)
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"Sitemap parse warning: {ex.Message}");
            }
        }

        private async Task CrawlSourcePathsAsync(string sourceBase, HashSet<string> paths, int limit, IProgress<string> progress)
        {
            var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pendingPaths = new Queue<string>();
            pendingPaths.Enqueue("/");

            while (pendingPaths.Count > 0 && paths.Count < limit)
            {
                var currentPath = pendingPaths.Dequeue();
                if (!visitedPaths.Add(currentPath))
                    continue;

                var currentUrl = _urlService.JoinSourceUrl(sourceBase, currentPath);
                var html = await FetchTextAsync(currentUrl);
                if (string.IsNullOrWhiteSpace(html))
                    continue;

                foreach (var discoveredPath in ExtractSourceLinks(sourceBase, html))
                {
                    if (paths.Add(discoveredPath) && paths.Count < limit)
                    {
                        pendingPaths.Enqueue(discoveredPath);
                    }

                    if (paths.Count >= limit)
                        break;
                }
            }
        }

        private IEnumerable<string> ExtractSourceLinks(string sourceBase, string html)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var document = new HtmlDocument();
            document.LoadHtml(html);

            foreach (var link in document.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
            {
                var href = link.GetAttributeValue("href", string.Empty)?.Trim();
                if (string.IsNullOrWhiteSpace(href)
                    || href.StartsWith("#", StringComparison.Ordinal)
                    || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                    || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                    || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryNormalizeSourcePath(sourceBase, href, out var path) && !LooksLikeBinaryAsset(path))
                {
                    results.Add(path);
                }
            }

            return results;
        }

        private bool TryNormalizeSourcePath(string sourceBase, string candidateUrl, out string path)
        {
            path = string.Empty;
            if (string.IsNullOrWhiteSpace(candidateUrl)
                || !Uri.TryCreate(sourceBase, UriKind.Absolute, out var sourceUri))
            {
                return false;
            }

            Uri resolvedUri;
            if (Uri.TryCreate(candidateUrl, UriKind.Absolute, out var absoluteUri))
            {
                resolvedUri = absoluteUri;
            }
            else if (!Uri.TryCreate(sourceUri, candidateUrl, out resolvedUri))
            {
                return false;
            }

            if (!string.Equals(resolvedUri.Host, sourceUri.Host, StringComparison.OrdinalIgnoreCase))
                return false;

            var scopedBasePath = sourceUri.AbsolutePath.TrimEnd('/');
            var resolvedPath = resolvedUri.AbsolutePath;

            if (!string.IsNullOrEmpty(scopedBasePath) && scopedBasePath != "/")
            {
                if (string.Equals(resolvedPath, scopedBasePath, StringComparison.OrdinalIgnoreCase))
                {
                    path = "/";
                    return true;
                }

                if (!resolvedPath.StartsWith(scopedBasePath + "/", StringComparison.OrdinalIgnoreCase))
                    return false;

                resolvedPath = resolvedPath.Substring(scopedBasePath.Length);
            }

            path = _urlService.NormalizePath(resolvedPath);
            return true;
        }

        private static bool LooksLikeBinaryAsset(string path)
        {
            var extension = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(extension))
                return false;

            var blockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".gif", ".svg", ".webp", ".pdf", ".doc", ".docx",
                ".xls", ".xlsx", ".zip", ".mp4", ".mp3", ".avi", ".mov", ".css", ".js", ".json", ".xml"
            };

            return blockedExtensions.Contains(extension);
        }

        private async Task<string> FetchTextAsync(string url)
        {
            try
            {
                using var handler = _urlService.GetHandlerForUrl(url);
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead);
                if (!response.IsSuccessStatusCode)
                    return string.Empty;

                return await response.Content.ReadAsStringAsync();
            }
            catch
            {
                return string.Empty;
            }
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

            var (labels, urls) = ParseAllowlist(allowlist, allowlistFile);
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { seedBase };

            foreach (var url in urls)
            {
                var normalizedUrl = _urlService.NormalizeSiteBase(url);
                if (string.IsNullOrWhiteSpace(normalizedUrl) || !seenUrls.Add(normalizedUrl))
                    continue;

                testBases.Add(new TestBase
                {
                    Url = normalizedUrl,
                    Label = _urlService.TestSiteLabel(normalizedUrl)
                });
            }

            if (Uri.TryCreate(_urlService.NormalizeSiteBase(seedBase), UriKind.Absolute, out var seedUri))
            {
                var hostRoot = $"{seedUri.Scheme}://{seedUri.Host}";
                if (seedUri.Port != 80 && seedUri.Port != 443 && seedUri.Port != -1)
                    hostRoot += $":{seedUri.Port}";

                foreach (var label in labels)
                {
                    var cleanedLabel = label.Trim('/');
                    if (string.IsNullOrWhiteSpace(cleanedLabel))
                        continue;

                    var siblingBase = _urlService.NormalizeSiteBase($"{hostRoot}/{cleanedLabel}");
                    if (!seenUrls.Add(siblingBase))
                        continue;

                    testBases.Add(new TestBase
                    {
                        Url = siblingBase,
                        Label = cleanedLabel
                    });
                }
            }

            if (scope == "instance" && testBases.Count == 1)
            {
                progress?.Report("Instance scope selected, but no sibling allowlist entries were supplied; auditing the selected test site only.");
            }

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
                foreach (var item in SplitAllowlistTokens(raw))
                {
                    AddAllowlistToken(item, labels, urls);
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

                        foreach (var item in SplitAllowlistTokens(cleaned))
                        {
                            AddAllowlistToken(item, labels, urls);
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

        private static IEnumerable<string> SplitAllowlistTokens(string raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim())
                .Where(token => !string.IsNullOrWhiteSpace(token));
        }

        private static void AddAllowlistToken(string rawToken, HashSet<string> labels, HashSet<string> urls)
        {
            var token = (rawToken ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(token))
                return;

            if (token.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                urls.Add(token.TrimEnd('/').ToLower());
                return;
            }

            foreach (var label in token.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var cleanedLabel = label.Trim().Trim('/').ToLower();
                if (!string.IsNullOrWhiteSpace(cleanedLabel))
                    labels.Add(cleanedLabel);
            }
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
            bool sourceIsSubdomain,
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
                            var result = await AuditPageAsync(sourceBase, testBase.Url, path, testBase.Label, redirectOverrides, sourceIsSubdomain);
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
            string sourceBase, string testBase, string path, string testSiteLabel, HashSet<string> redirectOverrides, bool sourceIsSubdomain)
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
                var sourceUrl = !string.IsNullOrEmpty(sourceBase) ? _urlService.JoinSourceUrl(sourceBase, path) : null;
                var testUrl = _urlService.JoinTestUrl(testBase, path, sourceBase, sourceIsSubdomain);

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

            progress?.Report("Writing executive HTML report...");
            summary.ExecutiveHtmlPath = await _reportService.WriteExecutiveHtmlAsync(summary);
        }
    }
}
