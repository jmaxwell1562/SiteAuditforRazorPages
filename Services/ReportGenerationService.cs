using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AuditApp.Models;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace AuditApp.Services
{
    /// <summary>
    /// Service for generating audit reports (CSV, XLSX, HTML).
    /// </summary>
    public class ReportGenerationService
    {
        public async Task<string> WriteCsvAsync(AuditSummary summary)
        {
            var reportStamp = GetReportStamp(summary);
            var filePath = Path.Combine(summary.AuditFolder, $"{summary.SafeSiteName}_audit_report_{reportStamp}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("path,source_url,test_url,test_site,source_status,test_status,score,status,note,root_cause,section");

            foreach (var result in summary.AllResults)
            {
                var row = new[]
                {
                    EscapeCsv(result.Path),
                    EscapeCsv(result.SourceUrl),
                    EscapeCsv(result.TestUrl),
                    EscapeCsv(result.TestSite),
                    EscapeCsv(result.SourceStatus?.ToString() ?? string.Empty),
                    EscapeCsv(result.TestStatus?.ToString() ?? string.Empty),
                    EscapeCsv(result.Score.ToString("F4")),
                    EscapeCsv(NormalizeStatusLabel(result.Status)),
                    EscapeCsv(result.Note),
                    EscapeCsv(result.RootCause),
                    EscapeCsv(result.Section)
                };

                sb.AppendLine(string.Join(",", row));
            }

            WriteTextFileWithRetry(filePath, sb.ToString());
            PublishReportCopy(filePath);
            return await Task.FromResult(filePath);
        }

        public async Task<string> WriteClusterSummaryAsync(AuditSummary summary)
        {
            var reportStamp = GetReportStamp(summary);
            var filePath = Path.Combine(summary.AuditFolder, $"{summary.SafeSiteName}_failure_clusters_{reportStamp}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("failure_cluster,test_site,section,signature,failures_in_cluster,section_failures,sections_with_signature,total_failures_with_signature,systemic_breakage,systemic_reason");

            foreach (var cluster in summary.Clusters)
            {
                var row = new[]
                {
                    EscapeCsv(cluster.ClusterId),
                    EscapeCsv(cluster.TestSite),
                    EscapeCsv(cluster.Section),
                    EscapeCsv(cluster.Signature),
                    EscapeCsv(cluster.FailuresInCluster.ToString()),
                    EscapeCsv(cluster.SectionFailures.ToString()),
                    EscapeCsv(cluster.SectionsWithSignature.ToString()),
                    EscapeCsv(cluster.TotalFailuresWithSignature.ToString()),
                    EscapeCsv(cluster.SystemicBreakage.ToString()),
                    EscapeCsv(cluster.SystemicReason)
                };

                sb.AppendLine(string.Join(",", row));
            }

            WriteTextFileWithRetry(filePath, sb.ToString());
            PublishReportCopy(filePath);
            return await Task.FromResult(filePath);
        }

        public async Task<string> WriteReadinessSummaryAsync(AuditSummary summary)
        {
            var reportStamp = GetReportStamp(summary);
            var filePath = Path.Combine(summary.AuditFolder, $"{summary.SafeSiteName}_release_readiness_{reportStamp}.csv");
            var sectionRows = BuildSectionReadinessRows(summary);
            var overallRow = BuildOverallReadinessRow(summary);

            var sb = new StringBuilder();
            sb.AppendLine("section,test_site,total_pages,blocker_count,non_blocker_count,systemic_cluster_count,readiness_score,readiness_label,go_no_go,summary_reason");
            AppendReadinessCsvRow(sb, overallRow);

            foreach (var row in sectionRows)
            {
                AppendReadinessCsvRow(sb, row);
            }

            WriteTextFileWithRetry(filePath, sb.ToString());
            PublishReportCopy(filePath);
            return await Task.FromResult(filePath);
        }

        public async Task<string> WriteExcelAsync(AuditSummary summary)
        {
            var reportStamp = GetReportStamp(summary);
            var filePath = Path.Combine(summary.AuditFolder, $"{summary.SafeSiteName}_audit_report_{reportStamp}.xlsx");

            using var package = new ExcelPackage();
            WriteSummarySheet(package, summary);
            WriteSectionReadinessSheet(package, summary);
            WriteResultsSheet(package, summary);
            WriteQueueSheet(package, "Queue A", summary.QueueA.Take(10));
            WriteQueueSheet(package, "Queue B", summary.QueueB);

            await package.SaveAsAsync(new FileInfo(filePath));
            PublishReportCopy(filePath);
            return filePath;
        }

        public async Task<string> WriteHtmlAsync(AuditSummary summary)
        {
            var reportStamp = GetReportStamp(summary);
            var filePath = Path.Combine(summary.AuditFolder, $"{summary.SafeSiteName}_audit_report_{reportStamp}.html");
            var sectionRows = BuildSectionReadinessRows(summary);
            var detailRows = summary.AllResults
                .OrderBy(result => string.IsNullOrWhiteSpace(result.Section) ? "home" : result.Section)
                .ThenBy(result => GetDetailSortOrder(result.Status))
                .ThenBy(result => result.TestSite)
                .ThenBy(result => result.Path)
                .Take(800)
                .ToList();
            var readinessLabel = GetReadinessLabel(summary);
            var overallSummaryReason = BuildSummaryReason(
                summary.FailCount + summary.ErrorCount,
                summary.ReviewCount,
                summary.Clusters.Count(cluster => cluster.SystemicBreakage),
                summary.Readiness.NoGoReasons);

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"utf-8\" />");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            sb.AppendLine($"<title>{EscapeHtml(summary.SiteName)} Audit Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine(ReportStyles);
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<main>");
            sb.AppendLine("<header class=\"header\">");
            sb.AppendLine("<div class=\"brand-lockup\">");
            sb.AppendLine("<div class=\"brand-mark\" aria-hidden=\"true\">" + WsuMarkSvg + "</div>");
            sb.AppendLine("<div>");
            sb.AppendLine("<div class=\"brand-kicker\">Washington State University</div>");
            sb.AppendLine($"<h1>{EscapeHtml(summary.SiteName)} Audit Report</h1>");
            sb.AppendLine($"<p>Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
            sb.AppendLine($"<p>Run token: {EscapeHtml(summary.Timestamp)}</p>");
            sb.AppendLine("</div>");
            sb.AppendLine("</div>");
            sb.AppendLine("<p class=\"header-copy\">Interactive audit detail with clickable legend-pill filters for readiness and detail tables.</p>");
            sb.AppendLine("</header>");

            sb.AppendLine("<section class=\"stats\" aria-label=\"Summary counts\">");
            AppendStatCard(sb, "PASS", summary.PassCount, "status-pass");
            AppendStatCard(sb, "SOFT PASS", summary.SoftPassCount, "status-soft-pass");
            AppendStatCard(sb, "REVIEW", summary.ReviewCount, "status-review");
            AppendStatCard(sb, "FAIL", summary.FailCount, "status-fail");
            AppendStatCard(sb, "REDIRECT", summary.RedirectCount, "status-redirect");
            AppendStatCard(sb, "ERROR", summary.ErrorCount, "status-error");
            sb.AppendLine("</section>");

            sb.AppendLine("<section class=\"panel\">");
            sb.AppendLine("<h2>Release Overview</h2>");
            sb.AppendLine($"<p class=\"muted\">Overall status: <strong>{EscapeHtml(readinessLabel)}</strong>. Sections below are scored by blockers, non-blockers, and systemic cluster patterns.</p>");
            sb.AppendLine("<div class=\"summary-band\">");
            sb.AppendLine($"<div class=\"summary-chip\"><span>Overall readiness</span><strong>{EscapeHtml(readinessLabel)}</strong></div>");
            sb.AppendLine($"<div class=\"summary-chip\"><span>Pages audited</span><strong>{summary.AllResults.Count}</strong></div>");
            sb.AppendLine($"<div class=\"summary-chip\"><span>Priority Queue A</span><strong>{summary.QueueA.Count}</strong></div>");
            sb.AppendLine($"<div class=\"summary-chip\"><span>Systemic clusters</span><strong>{summary.Clusters.Count(cluster => cluster.SystemicBreakage)}</strong></div>");
            sb.AppendLine("</div>");

            sb.AppendLine("<div class=\"readiness-callout\">");
            sb.AppendLine($"<span class=\"status-pill {GetReadinessCssClass(readinessLabel)}\">{EscapeHtml(readinessLabel)}</span>");
            sb.AppendLine($"<p>{EscapeHtml(overallSummaryReason)}</p>");
            sb.AppendLine("</div>");
            sb.AppendLine("</section>");

            sb.AppendLine("<section class=\"panel\">");
            sb.AppendLine("<h2>Section Release Readiness</h2>");
            sb.AppendLine("<p class=\"muted\">This mirrors the older readiness view: one row per section and test site with blockers, non-blockers, and section-level release guidance.</p>");
            AppendReadinessLegend(sb);
            sb.AppendLine("<div class=\"table-wrap\">");
            sb.AppendLine("<table data-table-group=\"readiness\"><thead><tr><th>Section</th><th>Test Site</th><th>Total Pages</th><th>Blockers</th><th>Non-Blockers</th><th>Systemic Clusters</th><th>Readiness Score</th><th>Readiness Label</th><th>Go / No Go</th><th>Summary Reason</th></tr></thead><tbody>");

            foreach (var row in sectionRows)
            {
                sb.AppendLine($"<tr data-filter-values=\"{EscapeHtml(row.GoNoGo)}\">");
                sb.AppendLine($"<td class=\"section-name\">{EscapeHtml(row.Section)}</td>");
                sb.AppendLine($"<td>{EscapeHtml(row.TestSite)}</td>");
                sb.AppendLine($"<td>{row.TotalPages}</td>");
                sb.AppendLine($"<td>{row.BlockerCount}</td>");
                sb.AppendLine($"<td>{row.NonBlockerCount}</td>");
                sb.AppendLine($"<td>{row.SystemicClusterCount}</td>");
                sb.AppendLine($"<td class=\"score-cell\">{row.ReadinessScore:P0}</td>");
                sb.AppendLine($"<td>{EscapeHtml(row.ReadinessLabel)}</td>");
                sb.AppendLine($"<td><span class=\"status-pill {GetReadinessCssClass(row.GoNoGo)}\">{EscapeHtml(row.GoNoGo)}</span></td>");
                sb.AppendLine($"<td>{EscapeHtml(row.SummaryReason)}</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</div>");

            if (summary.Readiness.NoGoReasons.Any())
            {
                sb.AppendLine("<div class=\"readiness-reasons\">");
                sb.AppendLine("<h3>NO GO Reasons</h3>");
                sb.AppendLine("<ul class=\"reason-list\">");
                foreach (var reason in summary.Readiness.NoGoReasons)
                {
                    sb.AppendLine($"<li>{EscapeHtml(reason)}</li>");
                }
                sb.AppendLine("</ul>");
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</section>");

            sb.AppendLine("<section class=\"panel\">");
            sb.AppendLine("<h2>Detailed Rows</h2>");
            sb.AppendLine("<p class=\"muted\">Detailed rows are ordered by section and tagged for blockers, non-blockers, and systemic clusters so the older migration triage workflow is preserved.</p>");
            AppendStatusLegend(sb);
            sb.AppendLine("<div class=\"table-wrap\">");
            sb.AppendLine("<table data-table-group=\"status\"><thead><tr><th>Section</th><th>Path</th><th>Test Site</th><th>Status</th><th>Risk</th><th>Score</th><th>Root Cause</th><th>Failure Cluster</th><th>Source</th><th>Test</th></tr></thead><tbody>");
            foreach (var result in detailRows)
            {
                var normalizedStatus = NormalizeStatusLabel(result.Status);
                sb.AppendLine($"<tr data-filter-values=\"{EscapeHtml(BuildDetailFilterValues(result))}\">");
                sb.AppendLine($"<td class=\"section-name\">{EscapeHtml(string.IsNullOrWhiteSpace(result.Section) ? "home" : result.Section)}</td>");
                sb.AppendLine($"<td>{EscapeHtml(result.Path)}</td>");
                sb.AppendLine($"<td>{EscapeHtml(result.TestSite)}</td>");
                sb.AppendLine($"<td><span class=\"status-pill {GetStatusCssClass(result.Status)}\">{EscapeHtml(normalizedStatus)}</span></td>");
                sb.AppendLine("<td class=\"risk-cell\">");
                sb.AppendLine($"<span class=\"status-pill {GetSeverityCssClass(result.Status)}\">{EscapeHtml(GetSeverityLabel(result.Status))}</span>");
                if (result.SystemicBreakage)
                {
                    sb.AppendLine("<span class=\"status-pill status-systemic\">Systemic cluster</span>");
                }
                sb.AppendLine("</td>");
                sb.AppendLine($"<td class=\"score-cell\">{result.Score:P1}</td>");
                sb.AppendLine("<td>");
                sb.AppendLine($"<div>{EscapeHtml(result.RootCause)}</div>");
                if (!string.IsNullOrWhiteSpace(result.Note))
                {
                    sb.AppendLine($"<div class=\"muted detail-note\">{EscapeHtml(result.Note)}</div>");
                }
                sb.AppendLine("</td>");
                sb.AppendLine("<td>");
                sb.AppendLine($"<div>{EscapeHtml(string.IsNullOrWhiteSpace(result.FailureCluster) ? "None" : result.FailureCluster)}</div>");
                if (result.SystemicBreakage && !string.IsNullOrWhiteSpace(result.SystemicReason))
                {
                    sb.AppendLine($"<div class=\"muted detail-note\">{EscapeHtml(result.SystemicReason)}</div>");
                }
                sb.AppendLine("</td>");
                sb.AppendLine($"<td><a class=\"url-link\" href=\"{EscapeHtml(result.SourceUrl)}\" target=\"_blank\" rel=\"noreferrer\">Open source</a></td>");
                sb.AppendLine($"<td><a class=\"url-link\" href=\"{EscapeHtml(result.TestUrl)}\" target=\"_blank\" rel=\"noreferrer\">Open test</a></td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</div>");
            if (summary.AllResults.Count > detailRows.Count)
            {
                sb.AppendLine($"<p class=\"table-note muted\">Showing the first {detailRows.Count} rows in the HTML report. CSV and Excel outputs contain the full dataset.</p>");
            }
            sb.AppendLine("</section>");

            if (summary.QueueA.Any())
            {
                sb.AppendLine("<section class=\"panel\">");
                sb.AppendLine("<h2>Fix On Test Site</h2>");
                sb.AppendLine("<p class=\"muted\">Top 10 priority items from Queue A.</p>");
                sb.AppendLine("<table><thead><tr><th>Path</th><th>Status</th><th>Score</th><th>Root Cause</th></tr></thead><tbody>");
                foreach (var result in summary.QueueA.Take(10))
                {
                    sb.AppendLine("<tr>");
                    sb.AppendLine($"<td>{EscapeHtml(result.Path)}</td>");
                    sb.AppendLine($"<td><span class=\"status-pill {GetStatusCssClass(result.Status)}\">{EscapeHtml(NormalizeStatusLabel(result.Status))}</span></td>");
                    sb.AppendLine($"<td>{result.Score:P1}</td>");
                    sb.AppendLine($"<td>{EscapeHtml(result.RootCause)}</td>");
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</tbody></table>");
                sb.AppendLine("</section>");
            }

            sb.AppendLine("<script>");
            sb.AppendLine(FilterScript);
            sb.AppendLine("</script>");
            sb.AppendLine("</main>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            WriteTextFileWithRetry(filePath, sb.ToString());
            PublishReportCopy(filePath);
            return await Task.FromResult(filePath);
        }

        public async Task<string> WriteExecutiveHtmlAsync(AuditSummary summary)
        {
            var reportStamp = GetReportStamp(summary);
            var filePath = Path.Combine(summary.AuditFolder, $"{summary.SafeSiteName}_executive_view_{reportStamp}.html");

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            sb.AppendLine($"<title>{EscapeHtml(summary.SiteName)} Executive View</title>");
            sb.AppendLine("<style>body{font-family:Segoe UI,Tahoma,sans-serif;background:#f7f2ec;color:#1f2329;margin:0;padding:32px;}main{max-width:960px;margin:0 auto;background:#fff;padding:32px;border-radius:18px;box-shadow:0 16px 40px rgba(31,35,41,.12);}h1{margin-top:0;color:#981e32;}table{width:100%;border-collapse:collapse;margin-top:20px;}th,td{padding:12px;border-bottom:1px solid #ece5dd;text-align:left;}th{font-size:12px;text-transform:uppercase;letter-spacing:.08em;color:#5f6b76;}.pill{display:inline-block;padding:6px 10px;border-radius:999px;font-weight:700;background:#f4e3e6;color:#981e32;margin-right:8px;}</style></head><body><main>");
            sb.AppendLine($"<h1>{EscapeHtml(summary.SiteName)} Executive Summary</h1>");
            sb.AppendLine($"<p>Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}. Run token: {EscapeHtml(summary.Timestamp)}</p>");
            sb.AppendLine($"<p><span class=\"pill\">PASS {summary.PassCount}</span><span class=\"pill\">FAIL {summary.FailCount}</span><span class=\"pill\">REVIEW {summary.ReviewCount}</span></p>");
            sb.AppendLine("<h2>Top Queue A Items</h2><table><thead><tr><th>Path</th><th>Status</th><th>Score</th><th>Root Cause</th></tr></thead><tbody>");
            foreach (var result in summary.QueueA.Take(10))
            {
                sb.AppendLine($"<tr><td>{EscapeHtml(result.Path)}</td><td>{EscapeHtml(NormalizeStatusLabel(result.Status))}</td><td>{result.Score:P1}</td><td>{EscapeHtml(result.RootCause)}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");

            if (summary.Readiness.NoGoReasons.Any())
            {
                sb.AppendLine("<h2>No Go Reasons</h2><ul>");
                foreach (var reason in summary.Readiness.NoGoReasons)
                {
                    sb.AppendLine($"<li>{EscapeHtml(reason)}</li>");
                }
                sb.AppendLine("</ul>");
            }

            sb.AppendLine("</main></body></html>");
            WriteTextFileWithRetry(filePath, sb.ToString());
            PublishReportCopy(filePath);
            return await Task.FromResult(filePath);
        }

        private static void WriteSummarySheet(ExcelPackage package, AuditSummary summary)
        {
            var sheet = package.Workbook.Worksheets.Add("Summary");
            var readiness = GetReadinessLabel(summary);

            var rows = new (string Label, object Value)[]
            {
                ("Site Name", summary.SiteName),
                ("Run Time", summary.Timestamp),
                ("PASS", summary.PassCount),
                ("SOFT PASS", summary.SoftPassCount),
                ("REVIEW", summary.ReviewCount),
                ("FAIL", summary.FailCount),
                ("REDIRECT", summary.RedirectCount),
                ("GO", summary.Readiness.GoCount),
                ("CONDITIONAL GO", summary.Readiness.ConditionalGoCount),
                ("NO GO", summary.Readiness.NoGoCount),
                ("Readiness", readiness)
            };

            for (var index = 0; index < rows.Length; index++)
            {
                var row = index + 1;
                sheet.Cells[row, 1].Value = rows[index].Label;
                sheet.Cells[row, 2].Value = rows[index].Value;
            }

            if (summary.Readiness.NoGoReasons.Any())
            {
                var startRow = rows.Length + 3;
                sheet.Cells[startRow, 1].Value = "NO GO Reasons";
                for (var index = 0; index < summary.Readiness.NoGoReasons.Count; index++)
                {
                    sheet.Cells[startRow + index + 1, 1].Value = summary.Readiness.NoGoReasons[index];
                }
            }

            sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        }

        private static void WriteResultsSheet(ExcelPackage package, AuditSummary summary)
        {
            var sheet = package.Workbook.Worksheets.Add("Audit Detail");
            var headers = new[] { "Path", "Status", "Score", "Source URL", "Test URL", "Root Cause", "Note", "Section" };

            for (var index = 0; index < headers.Length; index++)
            {
                sheet.Cells[1, index + 1].Value = headers[index];
                sheet.Cells[1, index + 1].Style.Font.Bold = true;
                sheet.Cells[1, index + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                sheet.Cells[1, index + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(244, 241, 237));
            }

            var row = 2;
            foreach (var result in summary.AllResults)
            {
                sheet.Cells[row, 1].Value = result.Path;
                sheet.Cells[row, 2].Value = NormalizeStatusLabel(result.Status);
                sheet.Cells[row, 3].Value = result.Score;
                sheet.Cells[row, 3].Style.Numberformat.Format = "0.0%";
                sheet.Cells[row, 4].Value = result.SourceUrl;
                sheet.Cells[row, 5].Value = result.TestUrl;
                sheet.Cells[row, 6].Value = result.RootCause;
                sheet.Cells[row, 7].Value = result.Note;
                sheet.Cells[row, 8].Value = result.Section;
                ApplyStatusFill(sheet.Cells[row, 2], result.Status);
                row++;
            }

            sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        }

        private static void WriteSectionReadinessSheet(ExcelPackage package, AuditSummary summary)
        {
            var sheet = package.Workbook.Worksheets.Add("Section Readiness");
            var headers = new[]
            {
                "Section",
                "Test Site",
                "Total Pages",
                "Blocker Count",
                "Non-Blocker Count",
                "Systemic Cluster Count",
                "Readiness Score",
                "Readiness Label",
                "Go / No Go",
                "Summary Reason"
            };

            for (var index = 0; index < headers.Length; index++)
            {
                sheet.Cells[1, index + 1].Value = headers[index];
                sheet.Cells[1, index + 1].Style.Font.Bold = true;
                sheet.Cells[1, index + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                sheet.Cells[1, index + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(244, 241, 237));
            }

            var rows = new List<SectionReadinessRow> { BuildOverallReadinessRow(summary) };
            rows.AddRange(BuildSectionReadinessRows(summary));

            var rowNumber = 2;
            foreach (var row in rows)
            {
                sheet.Cells[rowNumber, 1].Value = row.Section;
                sheet.Cells[rowNumber, 2].Value = row.TestSite;
                sheet.Cells[rowNumber, 3].Value = row.TotalPages;
                sheet.Cells[rowNumber, 4].Value = row.BlockerCount;
                sheet.Cells[rowNumber, 5].Value = row.NonBlockerCount;
                sheet.Cells[rowNumber, 6].Value = row.SystemicClusterCount;
                sheet.Cells[rowNumber, 7].Value = row.ReadinessScore;
                sheet.Cells[rowNumber, 7].Style.Numberformat.Format = "0.0%";
                sheet.Cells[rowNumber, 8].Value = row.ReadinessLabel;
                sheet.Cells[rowNumber, 9].Value = row.GoNoGo;
                sheet.Cells[rowNumber, 10].Value = row.SummaryReason;
                ApplyStatusFill(sheet.Cells[rowNumber, 9], MapReadinessToStatus(row.GoNoGo));
                rowNumber++;
            }

            sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        }

        private static void WriteQueueSheet(ExcelPackage package, string sheetName, IEnumerable<AuditResult> results)
        {
            var sheet = package.Workbook.Worksheets.Add(sheetName);
            var headers = new[] { "Path", "Status", "Score", "Root Cause", "Test URL" };

            for (var index = 0; index < headers.Length; index++)
            {
                sheet.Cells[1, index + 1].Value = headers[index];
                sheet.Cells[1, index + 1].Style.Font.Bold = true;
            }

            var row = 2;
            foreach (var result in results)
            {
                sheet.Cells[row, 1].Value = result.Path;
                sheet.Cells[row, 2].Value = NormalizeStatusLabel(result.Status);
                sheet.Cells[row, 3].Value = result.Score;
                sheet.Cells[row, 3].Style.Numberformat.Format = "0.0%";
                sheet.Cells[row, 4].Value = result.RootCause;
                sheet.Cells[row, 5].Value = result.TestUrl;
                ApplyStatusFill(sheet.Cells[row, 2], result.Status);
                row++;
            }

            if (sheet.Dimension != null)
            {
                sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
            }
        }

        private static void ApplyStatusFill(ExcelRange cell, string status)
        {
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            switch (status)
            {
                case "PASS":
                    cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGreen);
                    break;
                case "SOFT PASS":
                    cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightSkyBlue);
                    break;
                case "REVIEW":
                    cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.Khaki);
                    break;
                case "FAIL":
                case "ERROR":
                    cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightCoral);
                    break;
                default:
                    cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.Gainsboro);
                    break;
            }
        }

        private static void AppendStatCard(StringBuilder sb, string label, int value, string tone)
        {
            sb.AppendLine($"<article class=\"stat\"><div class=\"stat-value {tone}\">{value}</div><div class=\"stat-label\">{EscapeHtml(label)}</div></article>");
        }

        private static void AppendReadinessLegend(StringBuilder sb)
        {
            sb.AppendLine("<div class=\"legend\" data-group=\"readiness\">");
            sb.AppendLine("<button type=\"button\" class=\"legend-pill active\" data-filter-group=\"readiness\" data-filter=\"all\">All Sections</button>");
            sb.AppendLine("<button type=\"button\" class=\"legend-pill\" data-filter-group=\"readiness\" data-filter=\"GO\">GO</button>");
            sb.AppendLine("<button type=\"button\" class=\"legend-pill\" data-filter-group=\"readiness\" data-filter=\"CONDITIONAL GO\">Conditional Go</button>");
            sb.AppendLine("<button type=\"button\" class=\"legend-pill\" data-filter-group=\"readiness\" data-filter=\"NO GO\">No Go</button>");
            sb.AppendLine("</div>");
        }

        private static void AppendStatusLegend(StringBuilder sb)
        {
            sb.AppendLine("<div class=\"legend\" data-group=\"status\">");
            sb.AppendLine("<button type=\"button\" class=\"legend-pill active\" data-filter-group=\"status\" data-filter=\"all\">All Rows</button>");
            sb.AppendLine("<button type=\"button\" class=\"legend-pill\" data-filter-group=\"status\" data-filter=\"BLOCKER\">Blocker</button>");
            sb.AppendLine("<button type=\"button\" class=\"legend-pill\" data-filter-group=\"status\" data-filter=\"NON-BLOCKER\">Non-Blocker</button>");
            sb.AppendLine("<button type=\"button\" class=\"legend-pill\" data-filter-group=\"status\" data-filter=\"SYSTEMIC\">Systemic Cluster</button>");
            sb.AppendLine("<button type=\"button\" class=\"legend-pill\" data-filter-group=\"status\" data-filter=\"PASS\">PASS</button>");
            sb.AppendLine("<button type=\"button\" class=\"legend-pill\" data-filter-group=\"status\" data-filter=\"SOFT PASS\">SOFT PASS</button>");
            sb.AppendLine("<button type=\"button\" class=\"legend-pill\" data-filter-group=\"status\" data-filter=\"REVIEW\">REVIEW</button>");
            sb.AppendLine("<button type=\"button\" class=\"legend-pill\" data-filter-group=\"status\" data-filter=\"FAIL\">FAIL</button>");
            sb.AppendLine("<button type=\"button\" class=\"legend-pill\" data-filter-group=\"status\" data-filter=\"REDIRECT\">REDIRECT</button>");
            sb.AppendLine("<button type=\"button\" class=\"legend-pill\" data-filter-group=\"status\" data-filter=\"ERROR\">ERROR</button>");
            sb.AppendLine("</div>");
        }

        private static void AppendReadinessCsvRow(StringBuilder sb, SectionReadinessRow row)
        {
            var values = new[]
            {
                EscapeCsv(row.Section),
                EscapeCsv(row.TestSite),
                EscapeCsv(row.TotalPages.ToString()),
                EscapeCsv(row.BlockerCount.ToString()),
                EscapeCsv(row.NonBlockerCount.ToString()),
                EscapeCsv(row.SystemicClusterCount.ToString()),
                EscapeCsv(row.ReadinessScore.ToString("F4")),
                EscapeCsv(row.ReadinessLabel),
                EscapeCsv(row.GoNoGo),
                EscapeCsv(row.SummaryReason)
            };

            sb.AppendLine(string.Join(",", values));
        }

        private static string GetReportStamp(AuditSummary summary)
        {
            return !string.IsNullOrWhiteSpace(summary.Timestamp)
                ? summary.Timestamp
                : summary.RunDate;
        }

        private void PublishReportCopy(string filePath)
        {
            var reportsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "reports");
            Directory.CreateDirectory(reportsRoot);
            var publishedPath = Path.Combine(reportsRoot, Path.GetFileName(filePath));
            ExecuteFileActionWithRetry(filePath, () =>
            {
                using var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var destinationStream = new FileStream(publishedPath, FileMode.Create, FileAccess.Write, FileShare.None);
                sourceStream.CopyTo(destinationStream);
            });
        }

        private static void WriteTextFileWithRetry(string filePath, string contents)
        {
            ExecuteFileActionWithRetry(filePath, () =>
            {
                using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                writer.Write(contents);
            });
        }

        private static void ExecuteFileActionWithRetry(string filePath, Action action)
        {
            const int maxAttempts = 5;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    Thread.Sleep(200 * attempt);
                }
                catch (UnauthorizedAccessException) when (attempt < maxAttempts)
                {
                    Thread.Sleep(200 * attempt);
                }
            }

            throw new IOException($"Unable to access report file '{filePath}' after multiple attempts.");
        }

        private static string EscapeCsv(string? value)
        {
            var safe = value ?? string.Empty;
            if (safe.Contains('"'))
            {
                safe = safe.Replace("\"", "\"\"");
            }

            if (safe.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            {
                safe = $"\"{safe}\"";
            }

            return safe;
        }

        private static string EscapeHtml(string? value)
        {
            return WebUtility.HtmlEncode(value ?? string.Empty);
        }

        private static string NormalizeStatusLabel(string status)
        {
            return status == "SKIP" ? "REDIRECT" : status ?? string.Empty;
        }

        private static string GetReadinessLabel(AuditSummary summary)
        {
            if (summary.Readiness.NoGoCount > 0)
            {
                return "NO GO";
            }

            if (summary.Readiness.ConditionalGoCount > 0)
            {
                return "CONDITIONAL GO";
            }

            return "GO";
        }

        private static string GetReadinessCssClass(string readiness)
        {
            return readiness switch
            {
                "GO" => "status-pass",
                "CONDITIONAL GO" => "status-review",
                _ => "status-fail"
            };
        }

        private static string GetStatusCssClass(string status)
        {
            return (status ?? string.Empty) switch
            {
                "PASS" => "status-pass",
                "SOFT PASS" => "status-soft-pass",
                "REVIEW" => "status-review",
                "FAIL" => "status-fail",
                "ERROR" => "status-error",
                "SKIP" => "status-redirect",
                _ => "status-redirect"
            };
        }

        private static List<SectionReadinessRow> BuildSectionReadinessRows(AuditSummary summary)
        {
            return summary.AllResults
                .GroupBy(result => new
                {
                    Section = string.IsNullOrWhiteSpace(result.Section) ? "home" : result.Section,
                    TestSite = string.IsNullOrWhiteSpace(result.TestSite) ? "root" : result.TestSite
                })
                .Select(group => BuildSectionReadinessRow(group.Key.Section, group.Key.TestSite, group.ToList()))
                .OrderBy(row => GetReadinessSortOrder(row.GoNoGo))
                .ThenByDescending(row => row.BlockerCount)
                .ThenByDescending(row => row.NonBlockerCount)
                .ThenBy(row => row.Section)
                .ThenBy(row => row.TestSite)
                .ToList();
        }

        private static SectionReadinessRow BuildOverallReadinessRow(AuditSummary summary)
        {
            return new SectionReadinessRow
            {
                Section = "overall",
                TestSite = "all",
                TotalPages = summary.AllResults.Count,
                BlockerCount = summary.FailCount + summary.ErrorCount,
                NonBlockerCount = summary.ReviewCount,
                SystemicClusterCount = summary.Clusters.Count(cluster => cluster.SystemicBreakage),
                ReadinessScore = CalculateReadinessScore(summary.AllResults),
                ReadinessLabel = GetSummaryReadinessLabel(GetReadinessLabel(summary)),
                GoNoGo = GetReadinessLabel(summary),
                SummaryReason = BuildSummaryReason(
                    summary.FailCount + summary.ErrorCount,
                    summary.ReviewCount,
                    summary.Clusters.Count(cluster => cluster.SystemicBreakage),
                    summary.Readiness.NoGoReasons)
            };
        }

        private static SectionReadinessRow BuildSectionReadinessRow(string section, string testSite, List<AuditResult> results)
        {
            var blockerCount = results.Count(result => IsBlockerStatus(result.Status));
            var nonBlockerCount = results.Count(result => string.Equals(result.Status, "REVIEW", StringComparison.OrdinalIgnoreCase));
            var systemicClusterCount = results
                .Where(result => result.SystemicBreakage && !string.IsNullOrWhiteSpace(result.FailureCluster))
                .Select(result => result.FailureCluster)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var goNoGo = GetSectionGoNoGo(blockerCount, nonBlockerCount, systemicClusterCount);

            return new SectionReadinessRow
            {
                Section = section,
                TestSite = testSite,
                TotalPages = results.Count,
                BlockerCount = blockerCount,
                NonBlockerCount = nonBlockerCount,
                SystemicClusterCount = systemicClusterCount,
                ReadinessScore = CalculateReadinessScore(results),
                ReadinessLabel = GetSummaryReadinessLabel(goNoGo),
                GoNoGo = goNoGo,
                SummaryReason = BuildSummaryReason(blockerCount, nonBlockerCount, systemicClusterCount)
            };
        }

        private static double CalculateReadinessScore(IEnumerable<AuditResult> results)
        {
            var items = results.ToList();
            if (items.Count == 0)
            {
                return 0.0;
            }

            var passLikeCount = items.Count(result => IsPassLikeStatus(result.Status));
            var reviewCount = items.Count(result => string.Equals(result.Status, "REVIEW", StringComparison.OrdinalIgnoreCase));
            var redirectCount = items.Count(result => string.Equals(result.Status, "SKIP", StringComparison.OrdinalIgnoreCase));

            return (passLikeCount + (reviewCount * 0.5) + (redirectCount * 0.25)) / items.Count;
        }

        private static bool IsPassLikeStatus(string status)
        {
            return string.Equals(status, "PASS", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "SOFT PASS", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBlockerStatus(string status)
        {
            return string.Equals(status, "FAIL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSectionGoNoGo(int blockerCount, int nonBlockerCount, int systemicClusterCount)
        {
            if (blockerCount > 0)
            {
                return "NO GO";
            }

            if (nonBlockerCount > 0 || systemicClusterCount > 0)
            {
                return "CONDITIONAL GO";
            }

            return "GO";
        }

        private static string GetSummaryReadinessLabel(string goNoGo)
        {
            return goNoGo switch
            {
                "GO" => "Ready",
                "CONDITIONAL GO" => "Needs Review",
                _ => "Blocked"
            };
        }

        private static string BuildSummaryReason(int blockerCount, int nonBlockerCount, int systemicClusterCount, IEnumerable<string>? extraReasons = null)
        {
            var parts = new List<string>();

            if (blockerCount > 0)
            {
                parts.Add($"{blockerCount} blocker{Pluralize(blockerCount)}");
            }

            if (nonBlockerCount > 0)
            {
                parts.Add($"{nonBlockerCount} non-blocker{Pluralize(nonBlockerCount)}");
            }

            if (systemicClusterCount > 0)
            {
                parts.Add($"{systemicClusterCount} systemic cluster{Pluralize(systemicClusterCount)}");
            }

            if (extraReasons != null)
            {
                parts.AddRange(extraReasons.Where(reason => !string.IsNullOrWhiteSpace(reason)).Select(reason => reason.Trim()));
            }

            return parts.Count > 0
                ? string.Join("; ", parts.Distinct(StringComparer.OrdinalIgnoreCase))
                : "No blocking issues detected";
        }

        private static string Pluralize(int value)
        {
            return value == 1 ? string.Empty : "s";
        }

        private static string BuildDetailFilterValues(AuditResult result)
        {
            var filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                NormalizeStatusLabel(result.Status)
            };

            if (IsBlockerStatus(result.Status))
            {
                filters.Add("BLOCKER");
            }
            else if (string.Equals(result.Status, "REVIEW", StringComparison.OrdinalIgnoreCase))
            {
                filters.Add("NON-BLOCKER");
            }

            if (result.SystemicBreakage)
            {
                filters.Add("SYSTEMIC");
            }

            return string.Join(",", filters);
        }

        private static string GetSeverityLabel(string status)
        {
            if (IsBlockerStatus(status))
            {
                return "Blocker";
            }

            if (string.Equals(status, "REVIEW", StringComparison.OrdinalIgnoreCase))
            {
                return "Non-blocker";
            }

            return "Monitor";
        }

        private static string GetSeverityCssClass(string status)
        {
            if (IsBlockerStatus(status))
            {
                return "status-fail";
            }

            if (string.Equals(status, "REVIEW", StringComparison.OrdinalIgnoreCase))
            {
                return "status-review";
            }

            return "status-redirect";
        }

        private static int GetDetailSortOrder(string status)
        {
            return (status ?? string.Empty) switch
            {
                "FAIL" => 0,
                "ERROR" => 1,
                "REVIEW" => 2,
                "SOFT PASS" => 3,
                "PASS" => 4,
                "SKIP" => 5,
                _ => 6
            };
        }

        private static int GetReadinessSortOrder(string goNoGo)
        {
            return goNoGo switch
            {
                "NO GO" => 0,
                "CONDITIONAL GO" => 1,
                _ => 2
            };
        }

        private static string MapReadinessToStatus(string goNoGo)
        {
            return goNoGo switch
            {
                "GO" => "PASS",
                "CONDITIONAL GO" => "REVIEW",
                _ => "FAIL"
            };
        }

        private sealed class SectionReadinessRow
        {
            public string Section { get; set; } = string.Empty;
            public string TestSite { get; set; } = string.Empty;
            public int TotalPages { get; set; }
            public int BlockerCount { get; set; }
            public int NonBlockerCount { get; set; }
            public int SystemicClusterCount { get; set; }
            public double ReadinessScore { get; set; }
            public string ReadinessLabel { get; set; } = string.Empty;
            public string GoNoGo { get; set; } = string.Empty;
            public string SummaryReason { get; set; } = string.Empty;
        }

        private const string FilterScript = """
document.querySelectorAll('.legend-pill').forEach(function(button) {
    button.addEventListener('click', function() {
        var group = button.getAttribute('data-filter-group');
        var filter = button.getAttribute('data-filter');
        document.querySelectorAll('.legend-pill[data-filter-group="' + group + '"]').forEach(function(item) {
            item.classList.remove('active');
        });
        button.classList.add('active');
        document.querySelectorAll('[data-table-group="' + group + '"] tbody tr').forEach(function(row) {
            var raw = row.getAttribute('data-filter-values') || row.getAttribute('data-filter-value') || '';
            var values = raw.split(',').map(function(value) {
                return value.trim();
            }).filter(function(value) {
                return value.length > 0;
            });
            var show = filter === 'all' || values.indexOf(filter) >= 0;
            row.classList.toggle('hidden', !show);
        });
    });
});
""";

        private const string WsuMarkSvg = @"<svg class=""wsu-g-header__logo-svg"" viewBox=""0 0 70.2 69.6"" aria-hidden=""true"" focusable=""false"" xmlns=""http://www.w3.org/2000/svg""><path class=""st0"" d=""m42.8 69.6s3.6-1.5 5.5-7.4c1 2.3 1.5 4.8 1.3 7.3-2.3 0.1-4.5 0.2-6.8 0.1zm14.9-11.8c-10.4 1.4-12.2-20.3-12.2-20.3s3.5 11.1 10.9 10.7c7.7-0.4 5.5-12.2 5.5-12.2s7.5 20.2-4.2 21.8zm-47.7-5c-3.3 1-6.6 1.8-10 2.4 0 0 5.9-4.5 10.3-18.3l4.3 3.9-0.8 2.6c1.1 1.5 1.9 3.1 2.5 4.9 1.6-3.6 1.5-7.7-0.3-11.2l-0.5 1.6-1.7-1.5-2.8-2.7c1.2-3.6 3-7 5.3-10.1l0.4 0.4 3.3 3.8-1 1.7c1.5 1.7 2.9 3.4 4.2 5.3 0.6-3.4 0.4-6.8-0.4-10.1l-1.5 1.4-3.6-4.2c4.5-4.7 10-8.2 16.2-10.3-0.4 0.4-0.7 0.8-1 1.3-2 2.9-4.1 8.1-2.4 16.4 0.3 1.3 0.7 3.2 1.1 5.1 0.9 3.8 1.9 8.2 2.2 10.9 0.7 5.7 0.1 9.4-1.8 11.4-1.3 1.4-3.5 2-6.4 1.9v-1.4c0-2.4-0.3-4.8-0.8-7.2l-0.8-2.7-1.2 2.6c-1.9 4.1-8.6 14.1-17.2 16.1 2.6-4.2 4.1-9 4.4-14zm25.4 16.4h-0.5-0.1-0.3c-0.5 0-0.8-0.1-1.2-0.1-0.7-0.1-1.6-0.2-2.6-0.4-6.4-1.1-12.9-1.7-19.4-2 6.2-3.6 10.4-9.9 12-12.5 0.2 1.3 0.3 2.5 0.3 3.8 0 0.8 0 1.6-0.1 2.2l-0.1 1.1 1.1 0.1c0.7 0.1 1.3 0.1 1.9 0.1 3.3 0 5.7-0.8 7.3-2.5 2.4-2.5 3.1-6.7 2.3-13.1-0.4-2.9-1.4-7.4-2.3-11.1-0.5-2-0.9-3.8-1.1-5-1.5-7.6 0.3-12.3 2.1-14.8 1.3-1.9 3.2-3.4 5.5-4.2h0.1l3.3-10.8h1.1l-2.3 10.3c0.7-0.1 1.2-0.2 1.7-0.3l3-9.5h1.1l-2 9.2c2.9-0.4 6.1-0.7 10.3-1 0.8 0.4 1.4 1.1 1.8 1.9l9.8-3 0.4 1.1-9.5 3.7c0.1 0.2 0.2 0.4 0.2 0.6l10.3-1.3 0.2 1.1-10 2c0 0.2 0.1 0.4 0.1 0.6l10.4 0.3v1.1l-10.2 0.4c0 1.2-0.2 2.4-0.5 3.6 0.6 2.1 0.7 4.3 0.3 6.5-1.7-3.9-3-5.1-3-5.1-1.4-0.7-2.9-1-4.5-1-2.5 0-4.8 1.1-6.4 2.9-2.3 2.6-3.5 6-3.3 9.5 0.2 2.4 0.7 5.1 1.4 8.6 0.7 3.6 1.6 8.1 2.3 13.5 0.6 4.1-0.1 7.3-1.8 9.7-1.6 2.1-4 3.4-6.5 3.8h-0.1-0.2-0.1-2.2zm10.4-51.5c-2.1-0.1-4.2 0.4-6 1.3-1.5 1-2.5 2.7-2.6 4.5-0.1 0.6-0.1 1.2 0 1.8 0.8-1.5 2-2.8 3.3-3.9 2.4-1.5 5.1-2.3 7.9-2.4h0.9 0.3c0.4 0 0.7-0.1 0.8-0.2 0-0.1-0.1-0.3-0.4-0.4-1.3-0.5-2.7-0.8-4.2-0.7z"" /></svg>";

        private const string ReportStyles = @"
:root {
  --wsu-crimson: #981e32;
  --wsu-dark: #2a3038;
  --wsu-stone: #f4f1ed;
  --wsu-border: #d9d0c7;
  --wsu-ink: #1f2329;
  --wsu-go: #1f7a3a;
  --wsu-warn: #b36a00;
  --wsu-fail: #b11e3a;
  --wsu-info: #3a5f8a;
}
* { box-sizing: border-box; }
body {
  margin: 0;
  font-family: Segoe UI, Tahoma, sans-serif;
  color: var(--wsu-ink);
  background: radial-gradient(circle at top right, rgba(152,30,50,.14), transparent 24%), linear-gradient(180deg, #f8f3ee 0%, #ffffff 260px);
}
main { max-width: 1280px; margin: 0 auto; padding: 32px 24px 56px; }
.header {
    background: #ffffff;
    color: #4f5963;
  padding: 28px;
  border-radius: 20px;
  box-shadow: 0 24px 48px rgba(31,35,41,.16);
    border: 1px solid rgba(31,35,41,.1);
}
.brand-lockup { display: flex; align-items: center; gap: 16px; }
.brand-mark {
  width: 72px;
  height: 72px;
  border-radius: 18px;
    background: rgba(152,30,50,.08);
  display: grid;
  place-items: center;
    color: var(--wsu-crimson);
    box-shadow: inset 0 0 0 1px rgba(152,30,50,.14);
}
.brand-mark svg { width: 46px; height: 46px; }
.brand-mark .st0 { fill: currentColor; }
.brand-kicker { font-size: 12px; letter-spacing: .16em; text-transform: uppercase; opacity: .78; }
.header h1 { margin: 4px 0 8px; font-size: 32px; }
.header p { margin: 4px 0; color: #5c6773; }
.header-copy { margin-top: 14px; max-width: 72ch; }
.stats { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: 16px; margin: 24px 0; }
.stat { background: white; padding: 20px; border-radius: 16px; border: 1px solid var(--wsu-border); box-shadow: 0 12px 28px rgba(42,48,56,.08); }
.stat-value { font-size: 34px; font-weight: 700; }
.stat-label { color: #5f6b76; margin-top: 10px; text-transform: uppercase; letter-spacing: .08em; font-size: 12px; }
.panel { background: white; border-radius: 18px; padding: 24px; border: 1px solid var(--wsu-border); box-shadow: 0 12px 28px rgba(42,48,56,.08); margin: 24px 0; }
.panel h2 { margin: 0 0 8px; font-size: 24px; }
.summary-band { display: flex; flex-wrap: wrap; gap: 12px; margin-top: 18px; }
.summary-chip { min-width: 170px; padding: 14px 16px; border-radius: 14px; background: #faf6f1; border: 1px solid #ece5dd; }
.summary-chip span { display: block; color: #5f6b76; font-size: 12px; text-transform: uppercase; letter-spacing: .08em; }
.summary-chip strong { display: block; margin-top: 6px; font-size: 24px; color: var(--wsu-dark); }
.readiness-callout { display: flex; align-items: center; gap: 14px; margin-top: 18px; padding: 16px 18px; border-radius: 16px; background: #faf6f1; border: 1px solid #ece5dd; }
.readiness-callout p { margin: 0; color: #4f5963; }
.readiness-reasons { margin-top: 18px; padding-top: 18px; border-top: 1px solid #ece5dd; }
.readiness-reasons h3 { margin: 0 0 10px; font-size: 16px; }
.reason-list { margin: 0; padding-left: 20px; color: #5f6b76; }
.reason-list li + li { margin-top: 6px; }
.legend { display: flex; flex-wrap: wrap; gap: 10px; margin: 16px 0 20px; }
.legend-pill { border: 1px solid var(--wsu-border); background: #fffaf7; color: var(--wsu-ink); border-radius: 999px; padding: 10px 16px; font-weight: 600; cursor: pointer; }
.legend-pill.active { background: var(--wsu-crimson); color: white; border-color: var(--wsu-crimson); }
.table-wrap { overflow-x: auto; }
table { min-width: 980px; }
table { width: 100%; border-collapse: collapse; }
thead th { text-align: left; font-size: 12px; letter-spacing: .08em; text-transform: uppercase; color: #5f6b76; padding: 12px; border-bottom: 2px solid var(--wsu-border); }
tbody td { padding: 14px 12px; border-bottom: 1px solid #ece5dd; vertical-align: top; }
tbody tr.hidden { display: none; }
tbody tr:hover { background: #faf5f1; }
.status-pill { display: inline-flex; align-items: center; border-radius: 999px; padding: 6px 10px; font-size: 12px; font-weight: 700; letter-spacing: .05em; }
.status-pass { background: rgba(31,122,58,.14); color: var(--wsu-go); }
.status-soft-pass { background: rgba(58,95,138,.14); color: var(--wsu-info); }
.status-review { background: rgba(179,106,0,.14); color: var(--wsu-warn); }
.status-fail, .status-error { background: rgba(177,30,58,.14); color: var(--wsu-fail); }
.status-redirect { background: rgba(95,107,118,.14); color: #4f5963; }
.status-systemic { background: rgba(121,82,43,.16); color: #79522b; }
.section-name { font-weight: 700; color: var(--wsu-dark); }
.risk-cell { display: flex; flex-wrap: wrap; gap: 8px; }
.score-cell { font-weight: 700; color: var(--wsu-dark); }
.detail-note { margin-top: 6px; }
.url-link { color: var(--wsu-crimson); font-weight: 700; text-decoration: none; }
.url-link:hover { text-decoration: underline; }
.table-note { margin: 14px 0 0; }
.muted { color: #5f6b76; }
@media (max-width: 1200px) { .stats { grid-template-columns: repeat(3, minmax(0, 1fr)); } }
@media (max-width: 960px) { .stats { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (max-width: 640px) {
  main { padding: 20px 16px 40px; }
  .stats { grid-template-columns: 1fr; }
  .brand-lockup { align-items: flex-start; }
  .header h1 { font-size: 26px; }
    .readiness-callout { flex-direction: column; align-items: flex-start; }
}
";
    }
}
