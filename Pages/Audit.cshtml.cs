using System;
using System.Collections.Generic;
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
        private readonly AuditService _auditService;

        [BindProperty]
        public AuditConfig Config { get; set; } = new();

        [BindProperty]
        public bool IsRunning { get; set; }

        public AuditSummary LastSummary { get; set; }
        public List<string> StatusMessages { get; set; } = new();

        public AuditModel(AuditService auditService)
        {
            _auditService = auditService;
        }

        public void OnGet()
        {
            // Initialize default values
            Config.MaxTabs = 5;
            Config.MaxPaths = 0;
            Config.TestScope = "ask";
        }

        public async Task<IActionResult> OnPostStartAuditAsync()
        {
            if (string.IsNullOrEmpty(Config.SiteName) || 
                string.IsNullOrEmpty(Config.SourceUrl) || 
                string.IsNullOrEmpty(Config.TestUrl))
            {
                ModelState.AddModelError("", "Site Name, Source URL, and Test URL are required");
                return Page();
            }

            IsRunning = true;

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    StatusMessages.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
                });

                LastSummary = await _auditService.RunAuditAsync(Config, progress);

                IsRunning = false;
                StatusMessages.Add("✓ Audit complete!");
            }
            catch (Exception ex)
            {
                IsRunning = false;
                StatusMessages.Add($"✗ Error: {ex.Message}");
                ModelState.AddModelError("", ex.Message);
            }

            return Page();
        }

        /// <summary>
        /// API endpoint for AJAX status updates
        /// </summary>
        public async Task<IActionResult> OnPostStartAuditAsyncApi()
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var messages = new List<string>();
            var progress = new Progress<string>(msg =>
            {
                messages.Add(msg);
            });

            try
            {
                var summary = await _auditService.RunAuditAsync(Config, progress);
                return new JsonResult(new
                {
                    success = true,
                    messages,
                    summary = new
                    {
                        summary.SiteName,
                        summary.RunDate,
                        summary.PassCount,
                        summary.FailCount,
                        summary.ReviewCount,
                        summary.AuditFolder,
                        summary.MainCsvPath,
                        summary.HtmlReportPath
                    }
                });
            }
            catch (Exception ex)
            {
                messages.Add($"Error: {ex.Message}");
                return new JsonResult(new
                {
                    success = false,
                    messages,
                    error = ex.Message
                });
            }
        }
    }
}
