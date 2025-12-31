using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Shared.Models;

namespace Shared;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ApiKeyOptions _configuration;

    public ApiKeyMiddleware(RequestDelegate next, IOptions<ApiKeyOptions> configuration)
    {
        _next = next;
        _configuration = configuration.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!(context.User.Identity?.IsAuthenticated ?? false))
        {
            if (!context.Request.Headers.TryGetValue("X-API-KEY", out StringValues extractedApiKey))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("API Key is missing.");
                return;
            }

            string[]? validApiKeys = _configuration.ApiKeys;
            if (validApiKeys == null || !validApiKeys.ToList().Contains(extractedApiKey))
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("Invalid API Key.");
                return;
            }
        }

        await _next(context);
    }
}