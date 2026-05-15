# Integration Checklist

## Pre-Integration (Preparation)

- [ ] Review the README_CSHARP_PORT.md overview
- [ ] Review the QUICKSTART.md guide
- [ ] Read through IMPLEMENTATION_GUIDE.md for architecture
- [ ] Check PYTHON_TO_CSHARP_MAPPING.md if you need to understand equivalents

## Project Setup

- [ ] Have an ASP.NET Core Razor Pages project ready
- [ ] Project targets .NET 6.0 or later (or .NET Framework 4.7.2+)
- [ ] Git repo initialized (optional but recommended)
- [ ] Branch created for this feature

## File Creation

- [ ] Created `Models/AuditModels.cs`
  - [ ] Contains all 6 model classes
  - [ ] No compilation errors

- [ ] Created `Services/UrlUtilityService.cs`
  - [ ] 200+ lines
  - [ ] No compilation errors
  - [ ] HttpClient injected correctly

- [ ] Created `Services/AuditAnalysisService.cs`
  - [ ] 350+ lines
  - [ ] No compilation errors
  - [ ] All scoring constants defined

- [ ] Created `Services/PageComparisonService.cs`
  - [ ] 150+ lines
  - [ ] HtmlAgilityPack usage correct
  - [ ] No compilation errors

- [ ] Created `Services/ReportGenerationService.cs`
  - [ ] 400+ lines
  - [ ] EPPlus usage correct
  - [ ] All report methods present
  - [ ] No compilation errors

- [ ] Created `Services/AuditService.cs`
  - [ ] 450+ lines
  - [ ] Main orchestrator logic complete
  - [ ] All service dependencies injected
  - [ ] No compilation errors

- [ ] Created `Pages/Audit.cshtml.cs`
  - [ ] PageModel inherits correctly
  - [ ] Form binding works
  - [ ] Async handler methods present
  - [ ] No compilation errors

- [ ] Created `Pages/Audit.cshtml`
  - [ ] Form elements match model properties
  - [ ] CSS styling present
  - [ ] Report display section exists
  - [ ] No compilation errors

## NuGet Packages

- [ ] `HtmlAgilityPack` installed
  - [ ] Command: `dotnet add package HtmlAgilityPack`
  - [ ] Version 1.11.46 or later recommended

- [ ] `EPPlus` installed
  - [ ] Command: `dotnet add package EPPlus`
  - [ ] Version 6.1+ recommended (includes NonCommercial license context)

- [ ] All packages restore successfully
  - [ ] No unresolved references

## Startup Configuration

- [ ] Dependency Injection setup complete in Program.cs or Startup.cs
  - [ ] `builder.Services.AddHttpClient()`
  - [ ] `builder.Services.AddScoped<UrlUtilityService>()`
  - [ ] `builder.Services.AddScoped<AuditAnalysisService>()`
  - [ ] `builder.Services.AddScoped<PageComparisonService>()`
  - [ ] `builder.Services.AddScoped<ReportGenerationService>()`
  - [ ] `builder.Services.AddScoped<AuditService>()`

- [ ] EPPlus license configured
  - [ ] `EPPlus.LicenseContext.SetLicense(LicenseContext.NonCommercial)` added

- [ ] Static file middleware enabled
  - [ ] `app.UseStaticFiles()` present

- [ ] Route to /audit page working
  - [ ] `app.MapRazorPages()` configured

## Output Folders

- [ ] Created `Audits/` folder in project root
  - [ ] Folder permissions allow write

- [ ] Updated file serving route to handle `/reports/` path
  - [ ] Reports middleware added to Program.cs

## Build & Compile

- [ ] Clean solution
  - [ ] Command: `dotnet clean`

- [ ] Build solution
  - [ ] Command: `dotnet build`
  - [ ] ✓ No errors
  - [ ] ✓ No critical warnings

- [ ] All projects build successfully

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
  - [ ] Click "Start Audit"

- [ ] Audit runs successfully
  - [ ] Status messages appear
  - [ ] Progress shown during execution
  - [ ] No exceptions thrown

- [ ] Results display
  - [ ] Summary counts show (PASS, FAIL, REVIEW)
  - [ ] Release readiness shown
  - [ ] Report links present

- [ ] Reports generated
  - [ ] CSV file created
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
