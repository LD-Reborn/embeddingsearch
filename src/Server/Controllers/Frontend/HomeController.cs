using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Helper;
using Shared.Models;
using Server.Exceptions;
using Server.Models;
namespace Server.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
[Route("/")]
public class HomeController : Controller
{
    private readonly ILogger<EntityController> _logger;
    private readonly SearchdomainManager _domainManager;

    public HomeController(ILogger<EntityController> logger, IConfiguration config, SearchdomainManager domainManager, SearchdomainHelper searchdomainHelper, DatabaseHelper databaseHelper)
    {
        _logger = logger;
        _domainManager = domainManager;
    }

    [Authorize]
    [HttpGet("/")]
    public IActionResult Index()
    {
        HomeIndexViewModel viewModel = new()
        {
            Searchdomains = _domainManager.ListSearchdomains()
        };
        return View(viewModel);
    }
}