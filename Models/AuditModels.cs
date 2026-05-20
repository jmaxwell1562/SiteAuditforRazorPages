using System;
using System.Collections.Generic;

namespace AuditApp.Models
{
    /// <summary>
    /// Configuration for an audit run
    /// </summary>
    public class AuditConfig
    {
        public string SiteName { get; set; }
        public string SourceUrl { get; set; }
        public string TestUrl { get; set; }
        public bool SourceIsSubdomain { get; set; }
        public string TestSlug { get; set; } = "/";
        public string TestScope { get; set; } = "ask"; // ask, single, instance
        public string TestAllowlist { get; set; } = "";
        public string TestAllowlistFile { get; set; } = "";
        public string BatchSiteMappings { get; set; } = "";
        public string BatchSiteMappingsFile { get; set; } = "";
        public string RedirectOverridePaths { get; set; } = "";
        public int MaxTabs { get; set; } = 5;
        public int MaxPaths { get; set; } = 0; // 0 = all paths
    }

    /// <summary>
    /// Result of a single page audit
    /// </summary>
    public class AuditResult
    {
        public string Path { get; set; }
        public string SourceUrl { get; set; }
        public string TestUrl { get; set; }
        public string TestSite { get; set; }
        public int? SourceStatus { get; set; }
        public int? TestStatus { get; set; }
        public double Score { get; set; } // 0.0 to 1.0 similarity
        public string Status { get; set; } // PASS, SOFT PASS, REVIEW, FAIL, SKIP, ERROR
        public string Note { get; set; }
        public string RootCause { get; set; }
        public bool IsRedirect { get; set; }
        public string Section { get; set; }
        public string FailureCluster { get; set; }
        public bool SystemicBreakage { get; set; }
        public string SystemicReason { get; set; }
    }

    /// <summary>
    /// Failure cluster summary
    /// </summary>
    public class FailureCluster
    {
        public string ClusterId { get; set; }
        public string TestSite { get; set; }
        public string Section { get; set; }
        public string Signature { get; set; }
        public int FailuresInCluster { get; set; }
        public int SectionFailures { get; set; }
        public int SectionsWithSignature { get; set; }
        public int TotalFailuresWithSignature { get; set; }
        public bool SystemicBreakage { get; set; }
        public string SystemicReason { get; set; }
    }

    /// <summary>
    /// Release readiness data
    /// </summary>
    public class ReadinessStatus
    {
        public int GoCount { get; set; }
        public int ConditionalGoCount { get; set; }
        public int NoGoCount { get; set; }
        public List<string> NoGoReasons { get; set; } = new();
    }

    /// <summary>
    /// Summary of audit results
    /// </summary>
    public class AuditSummary
    {
        public string SiteName { get; set; }
        public string SafeSiteName { get; set; }
        public string RunDate { get; set; }
        public string Timestamp { get; set; }
        public string AuditFolder { get; set; }

        // Count results
        public int PassCount { get; set; }
        public int SoftPassCount { get; set; }
        public int ReviewCount { get; set; }
        public int FailCount { get; set; }
        public int RedirectCount { get; set; }
        public int ErrorCount { get; set; }

        // Files generated
        public string MainCsvPath { get; set; }
        public string ClusterCsvPath { get; set; }
        public string ReadinessCsvPath { get; set; }
        public string HtmlReportPath { get; set; }
        public string ExecutiveHtmlPath { get; set; }
        public string XlsxPath { get; set; }

        // Readiness
        public ReadinessStatus Readiness { get; set; } = new();

        // Results
        public List<AuditResult> AllResults { get; set; } = new();
        public List<AuditResult> QueueA { get; set; } = new(); // Fix on test site
        public List<AuditResult> QueueB { get; set; } = new(); // Source/shared instability
        public List<FailureCluster> Clusters { get; set; } = new();
    }

    public class ReportHistoryEntry
    {
        public string SiteName { get; set; }
        public string AuditFolder { get; set; }
        public string HtmlFilePath { get; set; }
        public string HtmlFileName { get; set; }
        public string HtmlUrl { get; set; }
        public string ExecutiveHtmlPath { get; set; }
        public string ExecutiveHtmlUrl { get; set; }
        public string MainCsvPath { get; set; }
        public string MainCsvUrl { get; set; }
        public string XlsxPath { get; set; }
        public string XlsxUrl { get; set; }
        public string TimestampToken { get; set; }
        public DateTime GeneratedAt { get; set; }
        public int PassCount { get; set; }
        public int SoftPassCount { get; set; }
        public int ReviewCount { get; set; }
        public int FailCount { get; set; }
        public int RedirectCount { get; set; }

        public string GeneratedAtDisplay => GeneratedAt == default
            ? string.Empty
            : GeneratedAt.ToString("MMM d, yyyy h:mm tt");
    }

    public class ReportHistoryComparison
    {
        public int PassDelta { get; set; }
        public int ReviewDelta { get; set; }
        public int FailDelta { get; set; }

        public bool HasChanges => PassDelta != 0 || ReviewDelta != 0 || FailDelta != 0;
    }

    /// <summary>
    /// Test base variant (for instance scope discovery)
    /// </summary>
    public class TestBase
    {
        public string Url { get; set; }
        public string Label { get; set; }
    }
}
