using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AuditApp.Models;

namespace AuditApp.Services
{
    /// <summary>
    /// Service for scoring, analyzing, and clustering audit results
    /// </summary>
    public class AuditAnalysisService
    {
        // Scoring thresholds
        public const double PassThreshold = 0.95;
        public const double SoftPassThreshold = 0.90;

        /// <summary>
        /// Determine root cause of failure
        /// </summary>
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
                if (testStatus != null && testStatus >= 500)
                    return "Test server error";
                if (testStatus == 404)
                    return "Missing route/content on test";
                return "Failing page needs manual investigation";
            }

            if (status == "REVIEW")
                return $"Content mismatch: {score:P1} similarity";

            if (status == "SOFT PASS")
                return "Minor content differences";

            if (status == "PASS")
                return "High content parity";

            if (status == "SKIP")
                return "Redirected URL skipped";

            return "Unexpected error during audit";
        }

        /// <summary>
        /// Sort results by status priority, then score
        /// </summary>
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

        /// <summary>
        /// Extract section from path (first path segment)
        /// </summary>
        public string SectionFromPath(string path)
        {
            string cleaned = (path ?? "").Trim();
            if (string.IsNullOrEmpty(cleaned) || cleaned == "/")
                return "home";

            var parts = cleaned.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : "home";
        }

        /// <summary>
        /// Normalize failure signature from root_cause or HTTP pattern
        /// </summary>
        public string NormalizeSignature(AuditResult row)
        {
            if (!string.IsNullOrEmpty(row.RootCause))
                return row.RootCause.ToLower();

            if (row.TestStatus != null && row.TestStatus >= 500)
                return "test server error";
            if (row.TestStatus == 404)
                return "missing route/content on test";
            if (row.SourceStatus != null && row.TestStatus != null)
                return $"http pattern {row.SourceStatus}/{row.TestStatus}";

            return "unknown failure signature";
        }

        /// <summary>
        /// Cluster failures into groups by test site, section, and signature
        /// </summary>
        public (List<AuditResult> annotated, List<FailureCluster> clusters) ClusterFailures(List<AuditResult> results)
        {
            var failureStatuses = new HashSet<string> { "FAIL", "ERROR" };
            var failures = results
                .Where(r => failureStatuses.Contains(r.Status ?? ""))
                .ToList();

            // Initialize clustering fields
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

            // Group by (test_site, section, signature)
            var grouped = new Dictionary<(string, string, string), List<AuditResult>>();
            var signatureSections = new Dictionary<(string, string), HashSet<string>>();

            foreach (var row in failures)
            {
                var testSite = row.TestSite;
                var section = row.Section;
                var signature = NormalizeSignature(row);
                var key = (testSite, section, signature);

                if (!grouped.ContainsKey(key))
                    grouped[key] = new List<AuditResult>();
                grouped[key].Add(row);

                var sigKey = (testSite, signature);
                if (!signatureSections.ContainsKey(sigKey))
                    signatureSections[sigKey] = new HashSet<string>();
                signatureSections[sigKey].Add(section);
            }

            var summaries = new List<FailureCluster>();
            var sectionClusterCounts = new Dictionary<(string, string), int>();

            var sortedGroups = grouped
                .OrderByDescending(x => x.Value.Count)
                .ThenBy(x => x.Key.Item1)
                .ThenBy(x => x.Key.Item2)
                .ThenBy(x => x.Key.Item3)
                .ToList();

            foreach (var (key, clusterRows) in sortedGroups)
            {
                var (testSite, section, signature) = key;
                var sectionScope = (testSite, section);
                var count = sectionClusterCounts.TryGetValue(sectionScope, out var c) ? c : 0;
                sectionClusterCounts[sectionScope] = count + 1;

                var clusterId = $"{testSite}:{section}#{count + 1}";

                var sectionFailures = failures
                    .Where(r => r.TestSite == testSite && r.Section == section)
                    .ToList();
                int sectionFailureCount = sectionFailures.Count;
                int clusterCount = clusterRows.Count;
                double sectionRatio = sectionFailureCount > 0 ? (double)clusterCount / sectionFailureCount : 0.0;

                var sigKey = (testSite, signature);
                var crossSectionCount = signatureSections.TryGetValue(sigKey, out var sections) ? sections.Count : 0;
                int crossSectionTotal = 0;
                if (signatureSections.TryGetValue(sigKey, out var sigs))
                {
                    foreach (var sec in sigs)
                    {
                        var gkey = (testSite, sec, signature);
                        if (grouped.TryGetValue(gkey, out var g))
                            crossSectionTotal += g.Count;
                    }
                }

                bool sectionLevelSystemic = clusterCount >= 3 && sectionRatio >= 0.60;
                bool crossSectionSystemic = crossSectionCount >= 3 && crossSectionTotal >= 6;
                bool isSystemic = sectionLevelSystemic || crossSectionSystemic;

                var reasonParts = new List<string>();
                if (sectionLevelSystemic)
                {
                    reasonParts.Add(
                        $"Section pattern: {clusterCount}/{sectionFailureCount} failures in '{section}' share '{signature}'");
                }
                if (crossSectionSystemic)
                {
                    reasonParts.Add(
                        $"Shared pattern: '{signature}' appears across {crossSectionCount} sections ({crossSectionTotal} failures)");
                }
                string systemicReason = string.Join(" | ", reasonParts);

                foreach (var row in clusterRows)
                {
                    row.FailureCluster = clusterId;
                    row.SystemicBreakage = isSystemic;
                    row.SystemicReason = systemicReason;
                }

                summaries.Add(new FailureCluster
                {
                    ClusterId = clusterId,
                    TestSite = testSite,
                    Section = section,
                    Signature = signature,
                    FailuresInCluster = clusterCount,
                    SectionFailures = sectionFailureCount,
                    SectionsWithSignature = crossSectionCount,
                    TotalFailuresWithSignature = crossSectionTotal,
                    SystemicBreakage = isSystemic,
                    SystemicReason = systemicReason
                });
            }

            return (results, summaries);
        }

        /// <summary>
        /// Separate results into Queue A (fixes needed) and Queue B (source/shared issues)
        /// </summary>
        public (List<AuditResult> queueA, List<AuditResult> queueB) BuildQueues(List<AuditResult> results)
        {
            var queueA = new List<AuditResult>();
            var queueB = new List<AuditResult>();

            var failAndReview = results
                .Where(r => r.Status == "FAIL" || r.Status == "REVIEW")
                .Where(r => r.Status != "SKIP")
                .ToList();

            foreach (var result in failAndReview)
            {
                // Queue B: Source/shared/API issues
                if (result.RootCause?.Contains("Shared instability") == true ||
                    result.RootCause?.Contains("Source") == true ||
                    result.RootCause?.Contains("API") == true ||
                    result.RootCause?.Contains("Redirect") == true)
                {
                    queueB.Add(result);
                }
                else
                {
                    // Queue A: Migration gaps to fix on test
                    queueA.Add(result);
                }
            }

            // Sort by priority (FAIL before REVIEW, then by score)
            queueA = SortResults(queueA);
            queueB = SortResults(queueB);

            return (queueA, queueB);
        }

        /// <summary>
        /// Calculate readiness status based on results
        /// </summary>
        public ReadinessStatus CalculateReadiness(List<AuditResult> results)
        {
            var readiness = new ReadinessStatus();

            int passCount = results.Count(r => r.Status == "PASS" || r.Status == "SOFT PASS");
            int failCount = results.Count(r => r.Status == "FAIL");
            int reviewCount = results.Count(r => r.Status == "REVIEW");
            int totalCount = results.Count;

            if (totalCount == 0)
            {
                readiness.GoCount = 0;
                readiness.ConditionalGoCount = 0;
                readiness.NoGoCount = 1;
                readiness.NoGoReasons.Add("No audit data available");
                return readiness;
            }

            double passRatio = (double)passCount / totalCount;
            double failRatio = (double)failCount / totalCount;

            // Heuristics for readiness (adjust as needed)
            if (failRatio == 0 && reviewCount == 0)
            {
                readiness.GoCount = 1;
            }
            else if (failRatio < 0.05 && passRatio > 0.8)
            {
                readiness.ConditionalGoCount = 1;
                if (failCount > 0)
                    readiness.NoGoReasons.Add($"{failCount} critical failures");
                if (reviewCount > 0)
                    readiness.NoGoReasons.Add($"{reviewCount} items need review");
            }
            else
            {
                readiness.NoGoCount = 1;
                if (failCount > 0)
                    readiness.NoGoReasons.Add($"{failCount} critical failures");
                if (reviewCount > 0)
                    readiness.NoGoReasons.Add($"{reviewCount} content mismatches");
            }

            return readiness;
        }

        /// <summary>
        /// Get top N priority results for display
        /// </summary>
        public List<AuditResult> GetTopPriority(List<AuditResult> results, int limit = 10)
        {
            return results
                .Where(r => r.Status == "FAIL" || r.Status == "REVIEW")
                .OrderBy(r => r.Status == "FAIL" ? 0 : 1)
                .ThenBy(r => r.Score)
                .Take(limit)
                .ToList();
        }

        /// <summary>
        /// Count results by status
        /// </summary>
        public Dictionary<string, int> CountByStatus(List<AuditResult> results)
        {
            var counts = new Dictionary<string, int>
            {
                { "PASS", 0 },
                { "SOFT PASS", 0 },
                { "REVIEW", 0 },
                { "FAIL", 0 },
                { "SKIP", 0 },
                { "ERROR", 0 }
            };

            foreach (var result in results)
            {
                if (counts.ContainsKey(result.Status ?? ""))
                    counts[result.Status]++;
            }

            return counts;
        }
    }
}
