using AuditApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AuditApp;

public static class AuditServiceCollectionExtensions
{
    public static IServiceCollection AddMigrationAuditServices(this IServiceCollection services)
    {
        services.AddHttpClient<UrlUtilityService>();
        services.AddScoped<AuditAnalysisService>();
        services.AddScoped<PageComparisonService>();
        services.AddScoped<ReportGenerationService>();
        services.AddScoped<AuditService>();
        return services;
    }
}