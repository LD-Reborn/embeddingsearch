using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Server.Models;

namespace Server.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
[Route("[Controller]")]
public class AccountController : Controller
{
    private readonly SimpleAuthOptions _options;

    public AccountController(IOptions<EmbeddingSearchOptions> options)
    {
        _options = options.Value.SimpleAuth;
    }

    [HttpGet("Login")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost("Login")]
    public async Task<IActionResult> Login(
        string username,
        string password,
        string? returnUrl = null)
    {
        var user = _options.Users.SingleOrDefault(u =>
            u.Username == username && u.Password == password);

        if (user == null)
        {
            ModelState.AddModelError("", "Invalid credentials");
            return View();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username)
        };

        claims.AddRange(user.Roles.Select(r =>
            new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(
            claims, "AppCookie");

        await HttpContext.SignInAsync(
            "AppCookie",
            new ClaimsPrincipal(identity));

        return Redirect(returnUrl ?? "/");
    }

    [HttpGet("Logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("AppCookie");
        return RedirectToAction("Login");
    }
}
