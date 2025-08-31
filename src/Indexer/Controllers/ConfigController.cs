using ElmahCore;
using Microsoft.AspNetCore.Mvc;
using Indexer.Models;

namespace Indexer.Controllers;


[ApiController]
[Route("[controller]")]
public class ConfigController : ControllerBase
{
    private readonly ILogger<WorkerController> _logger;
    private readonly IConfiguration _config;
    private readonly IConfigurationRoot _configurationRoot;
    private readonly WorkerManager _workerCollection;

    public ConfigController(ILogger<WorkerController> logger, IConfiguration config, IConfigurationRoot configurationRoot, WorkerManager workerCollection)
    {
        _logger = logger;
        _config = config;
        _configurationRoot = configurationRoot;
        _workerCollection = workerCollection;
    }

    [HttpGet("Reload")]
    public ActionResult<ConfigReloadResult> Reload()
    {
        try
        {
            _logger.LogInformation("Reloading configuration");
            _configurationRoot.Reload();
            _logger.LogInformation("Reloaded configuration");
            _logger.LogInformation("Destroying workers");
            foreach (KeyValuePair<string, Worker> workerKVPair in _workerCollection.Workers)
            {
                Worker worker = workerKVPair.Value;
                foreach (ICall call in worker.Calls)
                {
                    call.Disable();
                    call.Dispose();
                }
                worker.Calls.Clear();

                _workerCollection.Workers.Remove(workerKVPair.Key);
                _logger.LogInformation("Destroyed worker {workerKVPair.Key}", [workerKVPair.Key]);
            }
            _logger.LogInformation("Destroyed workers");
            _workerCollection.InitializeWorkers();
            return new ConfigReloadResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError("Exception {ex.Message} happened while trying to reload the worker configuration.", [ex.Message]);
            HttpContext.RaiseError(ex);
            return new ConfigReloadResult { Success = false };
        }
    }
}