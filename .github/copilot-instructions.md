# WSU Migration Verification Dashboard - Copilot Instructions

## Dashboard Status: BUILD OR REFRESH AUTOMATICALLY

The dashboard host already exists in this workspace as an ASP.NET Core Razor Pages app. Refresh it in place, keep it runnable, and confirm the working local URL before audit or triage work continues.

## Prompt Intent

One prompt should be enough to:
1. refresh the existing dashboard host,
2. restore or build the app if needed,
3. start it locally and return the working dashboard URL,
4. run or triage audit results in the same pass.

The expected result is the current production dashboard behavior, not a partial scaffold. That includes the explicit subdomain-to-subpath control, `New Report` and `Previous Report`, HTML and executive preview links, an openable Excel workbook link, no separate CSV button in the dashboard UI, section-based HTML report output, and a plain-English file-lock message when an Excel workbook is open.

## Current Host

- Host type: ASP.NET Core Razor Pages
- Entry point: `Program.cs`
- Primary route: `/Audit`
- Current dev defaults from launch settings: `http://localhost:5219/Audit` and `https://localhost:7073/Audit`
- Reports are published to `wwwroot/reports` and linked through app routes such as `/reports/<filename>`

## Dashboard Features

1. Form controls
   - Full-width audit configuration panel
   - Site Name input
   - Source URL textbox
   - Required Test URL dropdown with preset environments and a Custom URL option
   - Single Site vs Entire Umbraco Instance mode selector
   - Explicit subdomain-to-subpath control for source-subdomain to test-subpath migrations
   - Optional max paths, max tabs, redirect overrides, and Test Allowlist / Test Allowlist File inputs that are shown for instance coverage runs
   - Instance coverage should let one audit compare the same source site against sibling test sites that live in the same Umbraco instance
   - Source path discovery comes from the source sitemap and source-host links, not from test-site links or Umbraco backoffice structure
   - Full allowlist target URLs with path prefixes such as `https://w3-testing.asis.wsu.edu/aea/camp/` are valid and must preserve that path prefix during audit URL construction
   - If users enter a source or custom test hostname without `https://`, normalize it automatically before the audit runs
   - Allowlist files should support one label or full URL per line, plus blank lines and `#` comments
   - The redirect field should be presented to users as Intentional Redirect Paths; it is only for paths that are intentionally redirected on the test site and does not remap one URL to another
   - Localhost audits supported for target sites running on port 7019

2. Interactive results display
   - Start Audit status button with Ready, Running, Complete, and Error states
   - Compact run feedback near the action button instead of a developer-facing status log
   - Duration notice for quick versus full runs
   - Error state when preflight stops before fresh report generation
   - Per-site report history refresh when Site Name changes or loses focus
   - Instance coverage support that keeps the run feedback compact and publishes one report containing sibling test-site comparisons

3. Report management
   - Generated Reports below the configuration form
   - Two-column desktop layout with `New Report` wider than `Previous Report`
   - New Report exposes HTML, executive preview, and Excel workbook links when available
   - Previous Report exposes the prior HTML report, prior executive preview when available, and improvement deltas
   - Report history matching tolerates spaces, underscores, hyphens, and case differences
   - HTML reports preserve section-based triage with clickable legend-pill filters, Section Release Readiness, and Detailed Rows

4. Branding
   - WSU top-left cougar logo/header lockup
   - Student Affairs / Traffic Cop-inspired shell where practical
   - Preserve provided SVG/logo markup instead of substituting placeholders

## Agent Instructions

1. Actually build or refresh the dashboard automatically before audit or triage work; do not stop at describing requirements.
2. Update existing dashboard files in place instead of creating parallel hosts.
3. Prefer `dotnet restore`, `dotnet build`, and `dotnet run` or the running host already present in the environment.
4. If the build output is locked because the app is already running, treat that as an environment condition rather than a code defect; confirm the active dashboard URL and continue with the running host when possible.
5. Ensure the dashboard ends with a confirmed local URL unless the environment is genuinely blocked.
6. Ensure fresh HTML, CSV, and XLSX artifacts are generated before marking a run successful.
7. Keep report links route-based through `/reports/<filename>` and do not use `file://` links.
8. Keep the dashboard surface intentionally narrow: no summary counts, release readiness, or Queue B panels on the main page.
9. Keep the prompts synchronized with the current dashboard behavior: `Migration_Verification_Prompt.md`, `Migration_Verification_Prompt_Quick.md`, and this file should be updated together when behavior, layout, controls, or labels change.

## Execution Notes

- Prefer the dashboard form or `StartAuditApi` handler for the C# dashboard workflow.
- Quick runs map to Max Paths = 80.
- Full runs leave Max Paths unrestricted.
- Localhost browser checks should tolerate local HTTPS certificate issues while still requiring the target app to be reachable.
- Full-site runs must use sitemap-backed discovery when available, with source-link crawling fallback.