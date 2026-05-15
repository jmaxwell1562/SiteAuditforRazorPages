using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuditApp.Models;
using OfficeOpenXml;

namespace AuditApp.Services
{
    /// <summary>
    /// Service for generating audit reports (CSV, XLSX, HTML)
    /// </summary>
    public class ReportGenerationService
    {
        /// <summary>
        /// Write results to CSV
        /// </summary>
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
                    EscapeCsv(result.TestUrl),
                    EscapeCsv(result.TestSite),
                    result.SourceStatus?.ToString() ?? "",
                    result.TestStatus?.ToString() ?? "",
                    result.Score.ToString("F4"),
                    EscapeCsv(result.Status),
                    EscapeCsv(result.Note),
                    EscapeCsv(result.RootCause),
                    EscapeCsv(result.Section)
                };
                sb.AppendLine(string.Join(",", row));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            return await Task.FromResult(filePath);
        }

        /// <summary>
        /// Write failure clusters to CSV
        /// </summary>
        public async Task<string> WriteClusterSummaryAsync(AuditSummary summary)
        {
            var filePath = Path.Combine(
                summary.AuditFolder,
                $"{summary.SafeSiteName}_failure_clusters_{summary.RunDate}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("failure_cluster,test_site,section,signature,failures_in_cluster," +
                "section_failures,sections_with_signature,total_failures_with_signature," +
                "systemic_breakage,systemic_reason");

            foreach (var cluster in summary.Clusters)
            {
                var row = new[]
                {
                    EscapeCsv(cluster.ClusterId),
                    EscapeCsv(cluster.TestSite),
                    EscapeCsv(cluster.Section),
                    EscapeCsv(cluster.Signature),
                    cluster.FailuresInCluster.ToString(),
                    cluster.SectionFailures.ToString(),
                    cluster.SectionsWithSignature.ToString(),
                    cluster.TotalFailuresWithSignature.ToString(),
                    cluster.SystemicBreakage.ToString(),
                    EscapeCsv(cluster.SystemicReason)
                };
                sb.AppendLine(string.Join(",", row));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            return await Task.FromResult(filePath);
        }

        /// <summary>
        /// Write readiness summary to CSV
        /// </summary>
        public async Task<string> WriteReadinessSummaryAsync(AuditSummary summary)
        {
            var filePath = Path.Combine(
                summary.AuditFolder,
                $"{summary.SafeSiteName}_release_readiness_{summary.RunDate}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("metric,count");
            sb.AppendLine($"PASS,{summary.PassCount}");
            sb.AppendLine($"SOFT PASS,{summary.SoftPassCount}");
            sb.AppendLine($"REVIEW,{summary.ReviewCount}");
            sb.AppendLine($"FAIL,{summary.FailCount}");
            sb.AppendLine($"REDIRECT,{summary.RedirectCount}");
            sb.AppendLine($"ERROR,{summary.ErrorCount}");
            sb.AppendLine($"GO,{summary.Readiness.GoCount}");
            sb.AppendLine($"CONDITIONAL GO,{summary.Readiness.ConditionalGoCount}");
            sb.AppendLine($"NO GO,{summary.Readiness.NoGoCount}");

            if (summary.Readiness.NoGoReasons.Any())
            {
                sb.AppendLine("");
                sb.AppendLine("NO GO Reasons:");
                foreach (var reason in summary.Readiness.NoGoReasons)
                {
                    sb.AppendLine($"\"{reason}\"");
                }
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            return await Task.FromResult(filePath);
        }

        /// <summary>
        /// Write results to Excel
        /// </summary>
        public async Task<string> WriteExcelAsync(AuditSummary summary)
        {
            EPPlus.LicenseContext.SetLicense(LicenseContext.NonCommercial);

            var filePath = Path.Combine(
                summary.AuditFolder,
                $"{summary.SafeSiteName}_audit_report_{summary.RunDate}.xlsx");

            using (var package = new ExcelPackage())
            {
                // Summary sheet
                var summarySheet = package.Workbook.Worksheets.Add("Summary");
                int row = 1;
                summarySheet.Cells[row, 1].Value = "Site Name";
                summarySheet.Cells[row, 2].Value = summary.SiteName;
                row++;

                summarySheet.Cells[row, 1].Value = "Run Date";
                summarySheet.Cells[row, 2].Value = summary.RunDate;
                row++;

                summarySheet.Cells[row, 1].Value = "PASS";
                summarySheet.Cells[row, 2].Value = summary.PassCount;
                row++;

                summarySheet.Cells[row, 1].Value = "SOFT PASS";
                summarySheet.Cells[row, 2].Value = summary.SoftPassCount;
                row++;

                summarySheet.Cells[row, 1].Value = "REVIEW";
                summarySheet.Cells[row, 2].Value = summary.ReviewCount;
                row++;

                summarySheet.Cells[row, 1].Value = "FAIL";
                summarySheet.Cells[row, 2].Value = summary.FailCount;
                row++;

                summarySheet.Cells[row, 1].Value = "Readiness";
                string readiness = summary.Readiness.GoCount > 0 ? "GO" : 
                                   summary.Readiness.ConditionalGoCount > 0 ? "CONDITIONAL GO" : "NO GO";
                summarySheet.Cells[row, 2].Value = readiness;
                row++;

                summarySheet.Columns[1].Width = 20;
                summarySheet.Columns[2].Width = 30;

                // Results sheet
                var resultsSheet = package.Workbook.Worksheets.Add("Results");
                var headers = new[] { "Path", "Test Site", "Status", "Score", "Source URL", "Test URL", "Root Cause" };
                for (int i = 0; i < headers.Length; i++)
                {
                    resultsSheet.Cells[1, i + 1].Value = headers[i];
                    resultsSheet.Cells[1, i + 1].Style.Font.Bold = true;
                }

                row = 2;
                foreach (var result in summary.AllResults.Take(500)) // Limit to 500 rows
                {
                    resultsSheet.Cells[row, 1].Value = result.Path;
                    resultsSheet.Cells[row, 2].Value = result.TestSite;
                    resultsSheet.Cells[row, 3].Value = result.Status;
                    resultsSheet.Cells[row, 4].Value = result.Score.ToString("P1");
                    resultsSheet.Cells[row, 5].Value = result.SourceUrl;
                    resultsSheet.Cells[row, 6].Value = result.TestUrl;
                    resultsSheet.Cells[row, 7].Value = result.RootCause;

                    // Color by status
                    switch (result.Status)
                    {
                        case "PASS":
                            resultsSheet.Cells[row, 3].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                            resultsSheet.Cells[row, 3].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGreen);
                            break;
                        case "FAIL":
                            resultsSheet.Cells[row, 3].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                            resultsSheet.Cells[row, 3].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightCoral);
                            break;
                        case "REVIEW":
                            resultsSheet.Cells[row, 3].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                            resultsSheet.Cells[row, 3].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightYellow);
                            break;
                    }

                    row++;
                }

                resultsSheet.Columns[1].Width = 30;
                resultsSheet.Columns[2].Width = 15;
                resultsSheet.Columns[3].Width = 12;
                resultsSheet.Columns[4].Width = 10;
                resultsSheet.Columns[5].Width = 40;
                resultsSheet.Columns[6].Width = 40;
                resultsSheet.Columns[7].Width = 40;

                package.SaveAs(new FileInfo(filePath));
            }

            return await Task.FromResult(filePath);
        }

        /// <summary>
        /// Write results to HTML
        /// </summary>
        public async Task<string> WriteHtmlAsync(AuditSummary summary)
        {
            var filePath = Path.Combine(
                summary.AuditFolder,
                $"{summary.SafeSiteName}_audit_report_{summary.RunDate}.html");

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine($"<title>{summary.SiteName} Audit Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }");
            sb.AppendLine(".header { background: #333; color: white; padding: 20px; border-radius: 5px; }");
            sb.AppendLine(".summary { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 20px; margin: 20px 0; }");
            sb.AppendLine(".stat { background: white; padding: 20px; border-radius: 5px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
            sb.AppendLine(".stat-value { font-size: 32px; font-weight: bold; color: #333; }");
            sb.AppendLine(".stat-label { color: #666; margin-top: 10px; }");
            sb.AppendLine("table { width: 100%; border-collapse: collapse; background: white; margin: 20px 0; }");
            sb.AppendLine("th { background: #f0f0f0; padding: 12px; text-align: left; font-weight: bold; border-bottom: 2px solid #ddd; }");
            sb.AppendLine("td { padding: 12px; border-bottom: 1px solid #ddd; }");
            sb.AppendLine("tr:hover { background: #f9f9f9; }");
            sb.AppendLine(".pass { color: green; font-weight: bold; }");
            sb.AppendLine(".fail { color: red; font-weight: bold; }");
            sb.AppendLine(".review { color: orange; font-weight: bold; }");
            sb.AppendLine(".queue { margin: 30px 0; }");
            sb.AppendLine(".queue h2 { color: #333; border-bottom: 2px solid #333; padding-bottom: 10px; }");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");

            // Header
            sb.AppendLine("<div class=\"header\">");
            sb.AppendLine($"<h1>{summary.SiteName} Audit Report</h1>");
            sb.AppendLine($"<p>Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
            sb.AppendLine("</div>");

            // Summary stats
            sb.AppendLine("<div class=\"summary\">");
            sb.AppendLine($"<div class=\"stat\"><div class=\"stat-value\">{summary.PassCount}</div><div class=\"stat-label\">PASS</div></div>");
            sb.AppendLine($"<div class=\"stat\"><div class=\"stat-value\">{summary.FailCount}</div><div class=\"stat-label\">FAIL</div></div>");
            sb.AppendLine($"<div class=\"stat\"><div class=\"stat-value\">{summary.ReviewCount}</div><div class=\"stat-label\">REVIEW</div></div>");
            sb.AppendLine("</div>");

            // Readiness
            string readiness = summary.Readiness.GoCount > 0 ? "GO" :
                               summary.Readiness.ConditionalGoCount > 0 ? "CONDITIONAL GO" : "NO GO";
            sb.AppendLine($"<div style=\"background: white; padding: 20px; border-radius: 5px; margin: 20px 0;\">");
            sb.AppendLine($"<h2>Release Readiness: <strong>{readiness}</strong></h2>");
            if (summary.Readiness.NoGoReasons.Any())
            {
                sb.AppendLine("<p><strong>Issues:</strong></p>");
                sb.AppendLine("<ul>");
                foreach (var reason in summary.Readiness.NoGoReasons)
                {
                    sb.AppendLine($"<li>{reason}</li>");
                }
                sb.AppendLine("</ul>");
            }
            sb.AppendLine("</div>");

            // Queue A (Top failures)
            if (summary.QueueA.Any())
            {
                sb.AppendLine("<div class=\"queue\">");
                sb.AppendLine("<h2>Queue A: Fix on Test Site (Top 10)</h2>");
                sb.AppendLine("<table>");
                sb.AppendLine("<thead><tr><th>Path</th><th>Status</th><th>Score</th><th>Root Cause</th></tr></thead>");
                sb.AppendLine("<tbody>");

                foreach (var result in summary.QueueA.Take(10))
                {
                    var statusClass = result.Status == "FAIL" ? "fail" : (result.Status == "REVIEW" ? "review" : "pass");
                    sb.AppendLine($"<tr>");
                    sb.AppendLine($"<td>{EscapeHtml(result.Path)}</td>");
                    sb.AppendLine($"<td class=\"{statusClass}\">{result.Status}</td>");
                    sb.AppendLine($"<td>{result.Score:P1}</td>");
                    sb.AppendLine($"<td>{EscapeHtml(result.RootCause)}</td>");
                    sb.AppendLine($"</tr>");
                }

                sb.AppendLine("</tbody>");
                sb.AppendLine("</table>");
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            return await Task.FromResult(filePath);
        }

        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";

            return value;
        }

        private string EscapeHtml(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return System.Net.WebUtility.HtmlEncode(value);
        }
    }
}
