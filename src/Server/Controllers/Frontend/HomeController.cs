using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Helper;
using Shared.Models;
using Server.Exceptions;
using Server.Models;
namespace Server.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
[Route("[Controller]")]
public class HomeController : Controller
{
    private readonly ILogger<EntityController> _logger;
    private readonly SearchdomainManager _domainManager;

    public HomeController(ILogger<EntityController> logger, IConfiguration config, SearchdomainManager domainManager, SearchdomainHelper searchdomainHelper, DatabaseHelper databaseHelper)
    {
        _logger = logger;
        _domainManager = domainManager;
    }

    [HttpGet("/")]
    public IActionResult Root()
    {
        return Redirect("/Home/Index");
    }

    [Authorize]
    [HttpGet("Index")]
    public IActionResult Index()
    {
        return View();
    }

    [Authorize]
    [HttpGet("Searchdomains")]
    public async Task<ActionResult> Searchdomains()
    {
        HomeIndexViewModel viewModel = new()
        {
            Searchdomains = await _domainManager.ListSearchdomainsAsync()
        };
        return View(viewModel);
    }
}