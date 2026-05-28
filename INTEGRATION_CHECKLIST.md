# Integration Checklist

This version assumes you are consuming the reusable package project instead of manually copying every service file.

## Pre-Integration (Preparation)

- [ ] Review the README_CSHARP_PORT.md overview
- [ ] Review the QUICKSTART.md guide
- [ ] Read through IMPLEMENTATION_GUIDE.md for architecture
- [ ] Check PYTHON_TO_CSHARP_MAPPING.md if you need to understand equivalents

## Project Setup

- [ ] Have an ASP.NET Core Razor Pages project ready
- [ ] Project targets .NET 8.0 or later recommended
- [ ] Git repo initialized (optional but recommended)
- [ ] Branch created for this feature

## Package Setup

- [ ] From this repo, build the reusable package
  - [ ] Command: `dotnet pack .\AuditApp.Package\AuditApp.Package.csproj -c Release`
  - [ ] File created: `AuditApp.Package\bin\Release\WSU.MigrationAudit.0.1.0.nupkg`

- [ ] In your target app, add one of these references
  - [ ] Local development reference: `dotnet add reference <path-to-AuditApp.Package.csproj>`
  - [ ] NuGet package reference: `dotnet add package WSU.MigrationAudit --source <feed>`

- [ ] EPPlus license call present in startup
  - [ ] `ExcelPackage.License.SetNonCommercialOrganization(...)`

## File Creation

- [ ] If you want the sample dashboard, copy or adapt the UI files from this repo
  - [ ] `Pages/Audit.cshtml`
  - [ ] `Pages/Audit.cshtml.cs`
  - [ ] Program.cs route configuration

- [ ] If you do not want the sample dashboard, create your own entry point
  - [ ] Razor Page, API controller, background worker, or console host
  - [ ] Inject `AuditService`
  - [ ] Build an `AuditConfig`
  - [ ] Call `RunAuditAsync()`

## NuGet Packages

- [ ] Package restore succeeds for your target app
- [ ] No unresolved references

## Startup Configuration

- [ ] Dependency Injection setup complete in Program.cs or Startup.cs
  - [ ] `builder.Services.AddRazorPages()` if using Razor Pages
  - [ ] `builder.Services.AddMigrationAuditServices()`

- [ ] EPPlus license configured
  - [ ] `ExcelPackage.License.SetNonCommercialOrganization(...)` added

- [ ] Static file middleware enabled
  - [ ] `app.UseStaticFiles()` present

- [ ] Route to /audit page working
  - [ ] `app.MapRazorPages()` configured

## Output Folders

- [ ] Created `Audits/` folder in project root
  - [ ] Folder permissions allow write

- [ ] Created `wwwroot/reports/` folder in project root
  - [ ] Generated reports are reachable through `/reports/<filename>` links

- [ ] Report publishing path verified
  - [ ] HTML link opens in browser
  - [ ] Excel link downloads

## Build & Compile

- [ ] Clean solution
  - [ ] Command: `dotnet clean`

- [ ] Build solution
  - [ ] Command: `dotnet build`
  - [ ] ✓ No errors
  - [ ] ✓ No critical warnings

- [ ] All projects build successfully

- [ ] If the app is already running, build to a separate output folder when validating
  - [ ] Example: `dotnet build -o .\.artifacts\host-validate /p:UseAppHost=false`

## Initial Testing

- [ ] Run project locally
  - [ ] Command: `dotnet run` or use Visual Studio debugger

- [ ] Navigate to `/audit` page
  - [ ] Page loads without errors
  - [ ] Form displays correctly
  - [ ] All form fields present

- [ ] Fill in test form
  - [ ] Site Name: "Test"
  - [ ] Source URL: "https://example.com"
  - [ ] Test URL: "https://example.com" (same for testing)
  - [ ] Toggle subdomain-to-subpath mode when the source is a subdomain and the test URL uses a site path prefix
  - [ ] Click "Start Audit"

- [ ] Audit runs successfully
  - [ ] Status messages appear
  - [ ] Progress shown during execution
  - [ ] No exceptions thrown

- [ ] Results display
  - [ ] New Report and Previous Report cards render correctly
  - [ ] Executive preview link appears when executive HTML is generated
  - [ ] HTML and Excel report links present

- [ ] Reports generated
  - [ ] CSV file created
  - [ ] Executive HTML file created
  - [ ] HTML file created
  - [ ] Excel file created

- [ ] Files accessible
  - [ ] Reports can be downloaded
  - [ ] HTML report opens in browser
  - [ ] CSV can be opened in Excel

## Real-World Testing

- [ ] Test with real WSU URLs
  - [ ] Source: https://cougarcard.wsu.edu
  - [ ] Test: https://w2-testing.asis.wsu.edu/cougarcard
  - [ ] Verify results make sense

- [ ] Test various configurations
  - [ ] MaxPaths = 0 (all paths)
  - [ ] MaxPaths = 80 (quick test)
  - [ ] Different test environments

- [ ] Test edge cases
  - [ ] Localhost URL (https://localhost:7019)
  - [ ] Empty source URL
  - [ ] Invalid URLs

## Performance Verification

- [ ] Audit completes in reasonable time
  - [ ] Quick run (80 paths): < 5 minutes
  - [ ] Full run (all paths): < 30 minutes

- [ ] No memory leaks
  - [ ] Task completion clean
  - [ ] Disposables properly handled

- [ ] Concurrent operations work
  - [ ] MaxTabs = 5 working
  - [ ] MaxTabs = 10 working
  - [ ] No race conditions

## Documentation

- [ ] README_CSHARP_PORT.md reviewed
- [ ] IMPLEMENTATION_GUIDE.md saved in project
- [ ] QUICKSTART.md saved in project
- [ ] PYTHON_TO_CSHARP_MAPPING.md saved for reference
- [ ] STARTUP_CONFIGURATION.txt kept for reference

- [ ] A junior developer can answer these questions from the docs alone
  - [ ] How do I install or reference the package?
  - [ ] Where do I register services?
  - [ ] What folders must exist before the first run?
  - [ ] How do I run a quick smoke test?
  - [ ] Where do the generated files appear?

## Code Quality

- [ ] No compiler warnings
- [ ] No null reference warnings (with nullable enabled)
- [ ] Comments present in complex sections
- [ ] Service methods documented
- [ ] Naming conventions consistent

## Integration with Existing UI

- [ ] Audit page styled to match site theme
  - [ ] Colors updated if needed
  - [ ] Logo updated if needed
  - [ ] Navigation links added

- [ ] Audit page accessible from main menu
  - [ ] Link in navigation
  - [ ] Breadcrumb correct

- [ ] Report folder served correctly
  - [ ] HTTP routes working
  - [ ] Download links functional

## Optional Enhancements

- [ ] Add database persistence
  - [ ] Entity Framework configured
  - [ ] Audit history saved

- [ ] Add authentication
  - [ ] Audit page protected
  - [ ] Admin role required

- [ ] Add scheduled audits
  - [ ] Hangfire configured
  - [ ] Background job scheduling

- [ ] Add email notifications
  - [ ] When audit completes
  - [ ] Status changes

- [ ] Add dashboard/analytics
  - [ ] Historical trend charts
  - [ ] Site comparison

## Deployment Preparation

- [ ] Test on server/staging environment
- [ ] Verify folder permissions on server
- [ ] Test report generation on server
- [ ] Verify HTTP client can reach test URLs
- [ ] SSL certificate setup correct (if needed)
- [ ] Firewall rules allow outbound HTTP

## Final Checklist

- [ ] All required files present and compiling
- [ ] All dependencies installed
- [ ] Service registration complete
- [ ] Local testing successful
- [ ] Real-world testing successful
- [ ] Documentation complete
- [ ] Ready for deployment

## Rollback Plan

If issues occur:

1. [ ] Commit current working state to git
2. [ ] Can revert to previous version if needed
3. [ ] Python audit.py still available as backup
4. [ ] Know where to restore from

## Success Criteria

✅ The audit page is accessible at `/audit`
✅ Forms accept configuration input
✅ Audits complete without errors
✅ Reports are generated correctly
✅ Results are actionable
✅ Performance is acceptable
✅ No unhandled exceptions
✅ Logging shows audit progress

---

## Questions During Integration?

Refer to:
1. **QUICKSTART.md** - Step-by-step integration
2. **IMPLEMENTATION_GUIDE.md** - API documentation
3. **PYTHON_TO_CSHARP_MAPPING.md** - How Python translates to C#
4. **Service code comments** - Inline documentation

Good luck! 🚀
