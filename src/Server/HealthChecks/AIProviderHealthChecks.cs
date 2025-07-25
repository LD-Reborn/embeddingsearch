using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Server.HealthChecks;

public class AIProviderHealthCheck : IHealthCheck
{
    private readonly SearchdomainManager _searchdomainManager;
    private readonly ILogger<DatabaseHealthCheck> _logger;
    public AIProviderHealthCheck(SearchdomainManager searchdomainManager, ILogger<DatabaseHealthCheck> logger)
    {
        _searchdomainManager = searchdomainManager;
        _logger = logger;
    }
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            //var _ = _searchdomainManager.client.ListLocalModelsAsync(cancellationToken).Result; // TODO reimplement this
        }
        catch (Exception ex)
        {
            _logger.LogCritical("AIProviderHealthCheck - Exception occurred when listing local models: {ex}", ex.Message);
            return Task.FromResult(
                HealthCheckResult.Unhealthy());
        }
        return Task.FromResult(
            HealthCheckResult.Healthy());
    }
}
