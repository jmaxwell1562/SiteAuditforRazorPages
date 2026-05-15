using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AuditApp.Models;

namespace AuditApp.Services
{
    /// <summary>
    /// Utility functions for URL normalization and test URL construction
    /// </summary>
    public class UrlUtilityService
    {
        private readonly HttpClient _httpClient;

        public UrlUtilityService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// Normalize a path: ensure leading/trailing slashes
        /// </summary>
        public string NormalizePath(string path)
        {
            string cleaned = (path ?? "").Trim();
            if (string.IsNullOrEmpty(cleaned))
                return "/";
            return "/" + cleaned.Trim('/') + "/";
        }

        /// <summary>
        /// Remove trailing slash from URL
        /// </summary>
        public string NormalizeSiteBase(string url)
        {
            return (url ?? "").TrimEnd('/');
        }

        /// <summary>
        /// Extract the first-level hostname segment (e.g., "wsu" from "wsu.edu")
        /// </summary>
        public string SourceSiteLabel(string sourceBase)
        {
            if (string.IsNullOrEmpty(sourceBase))
                return "";

            if (!Uri.TryCreate(sourceBase, UriKind.Absolute, out var uri))
                return "";

            var host = uri.Host.ToLower();
            if (string.IsNullOrEmpty(host))
                return "";

            var parts = host.Split('.');
            return parts.Length > 0 ? parts[0] : "";
        }

        /// <summary>
        /// Extract label from test URL path (first segment)
        /// </summary>
        public string TestSiteLabel(string testBase)
        {
            if (!Uri.TryCreate(testBase, UriKind.Absolute, out var uri))
                return "root";

            var path = uri.AbsolutePath.Trim('/');
            if (string.IsNullOrEmpty(path))
                return "root";

            var parts = path.Split('/');
            return parts.Length > 0 && !string.IsNullOrEmpty(parts[0]) ? parts[0] : "root";
        }

        /// <summary>
        /// Determine if test base path should be prefixed (i.e., it's a site-root slug, not a section)
        /// </summary>
        public bool ShouldPrefixTestBasePath(string basePath, string sourceBase)
        {
            string cleaned = (basePath ?? "").Trim('/').ToLower();
            if (string.IsNullOrEmpty(cleaned))
                return false;

            // Multi-segment paths are sections/deep links, not site-root slugs
            if (cleaned.Contains("/"))
                return false;

            return cleaned == SourceSiteLabel(sourceBase);
        }

        /// <summary>
        /// Join test URL with path, handling section vs root logic
        /// </summary>
        public string JoinTestUrl(string baseUrl, string path, string sourceBase = "")
        {
            baseUrl = NormalizeSiteBase(baseUrl);
            string normalizedPath = "/" + (path ?? "").TrimStart('/');

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
                return baseUrl + normalizedPath;

            string basePath = uri.AbsolutePath.TrimEnd('/');
            if (string.IsNullOrEmpty(basePath) || basePath == "/")
                basePath = "";

            // If test_url path is a section (not source site slug), join against host root
            if (!string.IsNullOrEmpty(basePath) && !ShouldPrefixTestBasePath(basePath, sourceBase))
            {
                string hostRoot = $"{uri.Scheme}://{uri.Host}";
                if (uri.Port != 80 && uri.Port != 443 && uri.Port != -1)
                    hostRoot += $":{uri.Port}";
                return hostRoot + normalizedPath;
            }

            // Avoid duplicate segments
            if (!string.IsNullOrEmpty(basePath) && normalizedPath.StartsWith(basePath + "/"))
            {
                normalizedPath = normalizedPath.Substring(basePath.Length);
            }
            else if (!string.IsNullOrEmpty(basePath) && normalizedPath.TrimEnd('/') == basePath)
            {
                normalizedPath = "/";
            }

            if (!normalizedPath.StartsWith("/"))
                normalizedPath = "/" + normalizedPath;

            return baseUrl + normalizedPath;
        }

        /// <summary>
        /// Get HTTP client request options for a URL (e.g., disable SSL verification for localhost)
        /// </summary>
        public HttpClientHandler GetHandlerForUrl(string url)
        {
            var handler = new HttpClientHandler();
            if (IsLocalhostUrl(url))
            {
                handler.ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true;
            }
            return handler;
        }

        /// <summary>
        /// Check if URL is localhost
        /// </summary>
        public bool IsLocalhostUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            var host = uri.Host.ToLower();
            return host == "localhost" || host == "127.0.0.1" || host == "::1";
        }

        /// <summary>
        /// Sanitize site name for use in filenames
        /// </summary>
        public string SanitizeSiteName(string siteName)
        {
            string sanitized = Regex.Replace(siteName, @"[^A-Za-z0-9_\-]+", "_").Trim('_');
            return string.IsNullOrEmpty(sanitized) ? "Site" : sanitized;
        }

        /// <summary>
        /// Probe a URL to get HTTP status without following redirects
        /// </summary>
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
                System.Diagnostics.Debug.WriteLine($"[WARN] Probe failed for {url}: {ex.Message.Substring(0, Math.Min(120, ex.Message.Length))}");
                return (null, false);
            }
        }

        /// <summary>
        /// Detect and auto-correct w2/wdev3 host mismatches
        /// </summary>
        public async Task<string> MaybeCorrectTestBaseAsync(string sourceBase, string testBase, List<string> paths)
        {
            if (string.IsNullOrEmpty(sourceBase))
                return testBase;

            if (!Uri.TryCreate(testBase, UriKind.Absolute, out var uri))
                return testBase;

            var host = uri.Host.ToLower();
            var hostMap = new Dictionary<string, string>
            {
                { "wdev3-testing.asis.wsu.edu", "w2-testing.asis.wsu.edu" },
                { "w2-testing.asis.wsu.edu", "wdev3-testing.asis.wsu.edu" }
            };

            if (!hostMap.ContainsKey(host))
                return testBase;

            // Sample paths to detect mismatch
            var samplePaths = paths
                .Where(p => !string.IsNullOrEmpty(p) && p != "/")
                .Take(12)
                .ToList();

            if (samplePaths.Count == 0)
                return testBase;

            // Check for suspect patterns
            var suspectPaths = new List<string>();
            foreach (var path in samplePaths)
            {
                var sourceUrl = !string.IsNullOrEmpty(sourceBase) ? sourceBase + path : null;
                var (sourceStatus, _) = sourceUrl != null 
                    ? await ProbeUrlAsync(sourceUrl)
                    : (null, false);
                var (testStatus, _) = await ProbeUrlAsync(testBase + path);

                if (sourceStatus == 200 && (testStatus == null || testStatus >= 400))
                    suspectPaths.Add(path);
            }

            int threshold = Math.Max(3, (int)(samplePaths.Count * 0.6));
            if (suspectPaths.Count < threshold)
                return testBase;

            // Try alternate host
            var altHost = hostMap[host];
            var altPath = uri.AbsolutePath.TrimEnd('/');
            var altBase = !string.IsNullOrEmpty(altPath)
                ? $"{uri.Scheme}://{altHost}{altPath}"
                : $"{uri.Scheme}://{altHost}";

            int recovered = 0;
            foreach (var path in suspectPaths)
            {
                var (altStatus, _) = await ProbeUrlAsync(altBase + path);
                if (altStatus != null && altStatus < 400)
                    recovered++;
            }

            int suspectThreshold = Math.Max(3, (int)(suspectPaths.Count * 0.6));
            if (recovered >= suspectThreshold)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[WARN] Detected probable test-host mismatch ({host} -> {altHost}) based on sampled path health; " +
                    $"auto-switching to {altBase}");
                return altBase;
            }

            return testBase;
        }
    }
}
