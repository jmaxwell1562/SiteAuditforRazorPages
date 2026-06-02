# WSU Migration Audit Package

`WSU.MigrationAudit` packages the migration audit engine used by the dashboard host in this repository.

## Included

- `AuditConfig`, `AuditSummary`, and related result models
- `AuditService` orchestration for discovery, comparison, analysis, and report generation
- URL normalization, HTML comparison, readiness scoring, HTML/CSV/XLSX report output
- `AddMigrationAuditServices()` for DI registration in ASP.NET Core or any `IServiceCollection` host

## Usage

```csharp
using AuditApp;
using AuditApp.Models;
using AuditApp.Services;

builder.Services.AddMigrationAuditServices();

var auditService = app.Services.GetRequiredService<AuditService>();
var summary = await auditService.RunAuditAsync(new AuditConfig
{
    SiteName = "Example Site",
    SourceUrl = "https://example.wsu.edu",
    TestUrl = "https://w3-testing.asis.wsu.edu/example"
});
```

Reports are written to the `Audits` folder configured by the calling application.

The package is intentionally host-agnostic. A consuming web application should usually:

- map package output file paths to route-safe report URLs such as `/reports/<filename>`
- return host-owned DTOs from its API instead of exposing `AuditConfig` directly to the browser
- redirect successful runs from its runner page to a dedicated results page when that produces a cleaner UX