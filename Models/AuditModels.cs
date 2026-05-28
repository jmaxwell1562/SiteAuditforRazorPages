using System;
using System.Collections.Generic;

namespace AuditApp.Models
{
    /// <summary>
    /// Configuration for an audit run
    /// </summary>
    public class AuditConfig
    {
        public string SiteName { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string TestUrl { get; set; } = string.Empty;
        public bool SourceIsSubdomain { get; set; }
        public string TestSlug { get; set; } = "/";
        public string TestScope { get; set; } = "single"; // single, instance
        public string TestAllowlist { get; set; } = "";
        public string TestAllowlistFile { get; set; } = "";
        public string BatchSiteMappings { get; set; } = ""; // legacy compatibility
        public string BatchSiteMappingsFile { get; set; } = ""; // legacy compatibility
        public string RedirectOverridePaths { get; set; } = "";
        public int MaxTabs { get; set; } = 5;
        public int MaxPaths { get; set; } = 0; // 0 = all paths
    }

    /// <summary>
    /// Result of a single page audit
    /// </summary>
    public class AuditResult
    {
        public string Path { get; set; } = string.Empty;
        public string? SourceUrl { get; set; }
        public string TestUrl { get; set; } = string.Empty;
        public string TestSite { get; set; } = string.Empty;
        public int? SourceStatus { get; set; }
        public int? TestStatus { get; set; }
        public double Score { get; set; } // 0.0 to 1.0 similarity
        public string Status { get; set; } = string.Empty; // PASS, SOFT PASS, REVIEW, FAIL, SKIP, ERROR
        public string? Note { get; set; }
        public string? RootCause { get; set; }
        public bool IsRedirect { get; set; }
        public string Section { get; set; } = string.Empty;
        public string? FailureCluster { get; set; }
        public bool SystemicBreakage { get; set; }
        public string? SystemicReason { get; set; }
    }

    /// <summary>
    /// Failure cluster summary
    /// </summary>
    public class FailureCluster
    {
        public string ClusterId { get; set; } = string.Empty;
        public string TestSite { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public int FailuresInCluster { get; set; }
        public int SectionFailures { get; set; }
        public int SectionsWithSignature { get; set; }
        public int TotalFailuresWithSignature { get; set; }
        public bool SystemicBreakage { get; set; }
        public string SystemicReason { get; set; } = string.Empty;
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
        public string SiteName { get; set; } = string.Empty;
        public string SafeSiteName { get; set; } = string.Empty;
        public string RunDate { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string AuditFolder { get; set; } = string.Empty;

        // Count results
        public int PassCount { get; set; }
        public int SoftPassCount { get; set; }
        public int ReviewCount { get; set; }
        public int FailCount { get; set; }
        public int RedirectCount { get; set; }
        public int ErrorCount { get; set; }

        // Files generated
        public string MainCsvPath { get; set; } = string.Empty;
        public string ClusterCsvPath { get; set; } = string.Empty;
        public string ReadinessCsvPath { get; set; } = string.Empty;
        public string HtmlReportPath { get; set; } = string.Empty;
        public string ExecutiveHtmlPath { get; set; } = string.Empty;
        public string XlsxPath { get; set; } = string.Empty;

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
        public string SiteName { get; set; } = string.Empty;
        public string AuditFolder { get; set; } = string.Empty;
        public string HtmlFilePath { get; set; } = string.Empty;
        public string HtmlFileName { get; set; } = string.Empty;
        public string HtmlUrl { get; set; } = string.Empty;
        public string? ExecutiveHtmlPath { get; set; }
        public string? ExecutiveHtmlUrl { get; set; }
        public string? MainCsvPath { get; set; }
        public string? MainCsvUrl { get; set; }
        public string? XlsxPath { get; set; }
        public string? XlsxUrl { get; set; }
        public string TimestampToken { get; set; } = string.Empty;
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
        public string Url { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}
