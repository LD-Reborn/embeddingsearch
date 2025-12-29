namespace Server.Controllers;

using System.Text.Json;
using ElmahCore;
using Microsoft.AspNetCore.Mvc;
using Server.Exceptions;
using Server.Helper;
using Shared.Models;

[ApiController]
[Route("[controller]")]
public class ServerController : ControllerBase
{
    private readonly ILogger<ServerController> _logger;
    private readonly IConfiguration _config;
    private AIProvider _aIProvider;

    public ServerController(ILogger<ServerController> logger, IConfiguration config, AIProvider aIProvider)
    {
        _logger = logger;
        _config = config;
        _aIProvider = aIProvider;
    }

    /// <summary>
    /// Lists the models available to the server
    /// </summary>
    /// <remarks>
    /// Returns ALL models available to the server - not only the embedding models.
    /// </remarks>
    [HttpGet("Models")]
    public ActionResult<ServerGetModelsResult> GetModels()
    {
        try
        {
            string[] models = _aIProvider.GetModels();
            return new ServerGetModelsResult() { Models = models, Success = true };
        } catch (Exception ex)
        {
            _logger.LogError("Unable to get models due to exception {ex.Message} - {ex.StackTrace}", [ex.Message, ex.StackTrace]);
            return new ServerGetModelsResult() { Success = false, Message = ex.Message};
        }
    }
}
