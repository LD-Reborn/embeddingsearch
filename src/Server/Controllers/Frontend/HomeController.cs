using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Helper;
using Shared.Models;
using Server.Exceptions;
namespace Server.Controllers;

[ApiController]
[Route("/")]
public class HomeController : Controller
{
    private readonly ILogger<EntityController> _logger;
    public HomeController(ILogger<EntityController> logger, IConfiguration config, SearchdomainManager domainManager, SearchdomainHelper searchdomainHelper, DatabaseHelper databaseHelper)
    {
        _logger = logger;
    }

    [Authorize]
    [HttpGet("/")]
    public IActionResult Index()
    {
        return View();
    }
}