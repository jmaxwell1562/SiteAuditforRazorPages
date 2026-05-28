# SiteAudit for Razor Pages

This repo gives you two things:

- a working ASP.NET Core Razor Pages dashboard for running migration audits
- a reusable C# package project you can consume from another app

## Prerequisites

- .NET 8 SDK
- Git
- access to the source and test URLs you want to compare

## Clone To Running App

1. Clone the repository.
2. Open the repo root in VS Code or Visual Studio.
3. Restore packages:

```powershell
dotnet restore
```

4. Build to a separate output folder:

```powershell
dotnet build .\PostMigrationUmbraco_SiteAudit.csproj -o .\.artifacts\first-build /p:UseAppHost=false
```

5. Run the dashboard:

```powershell
dotnet run --project .\PostMigrationUmbraco_SiteAudit.csproj
```

6. Open the dashboard:

- `http://localhost:5219/Audit`
- `https://localhost:7073/Audit`

## First Smoke Test

Enter these values:

- Site Name: `Test Site`
- Source URL: `https://example.com`
- Test URL: `https://example.com`
- Max Paths: `80`
- Test Scope: `Single Site`

Click `Start Audit`.

Expected result:

- the button state changes from Ready to Running to Complete
- a new folder appears under `Audits/`
- report links appear in the `New Report` card
- generated reports are reachable through `/reports/<filename>`

## Using The Package In Another C# Project

Pack the reusable library:

```powershell
dotnet pack .\AuditApp.Package\AuditApp.Package.csproj -c Release
```

Generated package:

- `AuditApp.Package\bin\Release\WSU.MigrationAudit.0.1.0.nupkg`

During development you can also reference the package project directly:

```powershell
dotnet add reference <path-to-AuditApp.Package.csproj>
```

Register services in your host app:

```csharp
using AuditApp;
using OfficeOpenXml;

var builder = WebApplication.CreateBuilder(args);

ExcelPackage.License.SetNonCommercialOrganization("Washington State University");

builder.Services.AddRazorPages();
builder.Services.AddMigrationAuditServices();
```

## Project Layout

- `AuditApp.Package/`: reusable package project
- `Models/`: audit and report models
- `Services/`: audit engine and report generation
- `Pages/`: sample Razor Pages dashboard UI
- `Audits/`: generated runtime output
- `wwwroot/reports/`: route-served report artifacts

## Common Problems

### The build says a file is locked

The app is probably already running. Use an alternate build output:

```powershell
dotnet build .\PostMigrationUmbraco_SiteAudit.csproj -o .\.artifacts\validate /p:UseAppHost=false
```

### The Excel report will not generate

Close the workbook if it is open in Excel, then rerun the audit.

### My target app uses localhost with HTTPS

The audit service already tolerates local HTTPS certificate issues for localhost targets.

## Recovery And Handoff

If the chat history is lost or someone new needs to pick up the work, start with `SESSION_SUMMARY.md`.

That file records:

- the GitHub repo and branch
- the latest pushed commits
- the main implementation changes
- the validation commands that passed
- the local-only files that were intentionally not pushed

## Read Next

- `QUICKSTART.md`
- `IMPLEMENTATION_GUIDE.md`
- `README_CSHARP_PORT.md`
- `INTEGRATION_CHECKLIST.md`
- `SESSION_SUMMARY.md`