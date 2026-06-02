# Offline Backup Checklist

Create a zip that includes these exact files and folders:

- `Pages/`
- `Services/`
- `Models/`
- `Audits/` if present
- `wwwroot/reports/` if present
- `IMPLEMENTATION_GUIDE.md`
- `INTEGRATION_CHECKLIST.md`
- `PYTHON_TO_CSHARP_MAPPING.md`
- `QUICKSTART.md`
- `README_CSHARP_PORT.md`
- `STARTUP_CONFIGURATION.txt`
- `OFFLINE_BACKUP_CHECKLIST.md`
- `bootstrap-dashboard.ps1`

Recommended archive name:

- `PostMigrationUmbraco_SiteAudit_backup_YYYYMMDD_HHMMSS.zip`

Verification before zipping:

- Confirm the latest audit folder exists under `Audits/`
- Confirm the latest published report files exist under `wwwroot/reports/`
- Confirm the Razor page and service files open without editor errors
