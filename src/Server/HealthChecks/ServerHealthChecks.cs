using Microsoft.Extensions.Diagnostics.HealthChecks;
using Server.Migrations;

namespace Server.HealthChecks;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly SearchdomainManager _searchdomainManager;
    private readonly ILogger<DatabaseHealthCheck> _logger;
    public DatabaseHealthCheck(SearchdomainManager searchdomainManager, ILogger<DatabaseHealthCheck> logger)
    {
        _searchdomainManager = searchdomainManager;
        _logger = logger;
    }
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            DatabaseMigrations.DatabaseGetVersion(_searchdomainManager.helper);
        }
        catch (Exception ex)
        {
            _logger.LogCritical("DatabaseHealthCheck - Exception occurred when retrieving and parsing database version: {ex}", ex.Message);
            return Task.FromResult(
                HealthCheckResult.Unhealthy());
        }

        try
        {
            _searchdomainManager.helper.ExecuteSQLNonQuery("INSERT INTO settings (name, value) VALUES ('test', 'x');", []);
            _searchdomainManager.helper.ExecuteSQLNonQuery("DELETE FROM settings WHERE name = 'test';", []);
        }
        catch (Exception ex)
        {
            _logger.LogCritical("DatabaseHealthCheck - Exception occurred when executing INSERT/DELETE query: {ex}", ex.Message);
            return Task.FromResult(
                HealthCheckResult.Unhealthy());
        }

        return Task.FromResult(
            HealthCheckResult.Healthy());
    }
}
