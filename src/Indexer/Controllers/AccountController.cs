using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Indexer.Models;
using Shared.Services;

namespace Indexer.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
[Route("[Controller]")]
public class AccountController : Controller
{
    private readonly SimpleAuthOptions _options;
    private readonly LdapAuthenticationService _ldapAuth;

    public AccountController(
        IOptions<IndexerOptions> options,
        LdapAuthenticationService ldapAuth)
    {
        _options = options.Value.SimpleAuth ?? new();
        _ldapAuth = ldapAuth;
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
        var ldapResult = await _ldapAuth.AuthenticateAsync(username, password);

        string displayName;
        List<string> roles;

        if (ldapResult is not null)
        {
            displayName = ldapResult.DisplayName;
            roles = ldapResult.Roles;
        }
        else
        {
            var user = _options.Users.SingleOrDefault(u =>
                u.Username == username && u.Password == password);

            if (user == null)
            {
                ModelState.AddModelError("", "Invalid credentials");
                return View();
            }

            displayName = user.Username;
            roles = user.Roles.ToList();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, displayName)
        };

        claims.AddRange(roles.Select(r =>
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
