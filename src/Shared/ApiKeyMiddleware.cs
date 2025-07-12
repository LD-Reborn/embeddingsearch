using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Shared;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("X-API-KEY", out StringValues extractedApiKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("API Key is missing.");
            return;
        }

        var validApiKeys = _configuration.GetSection("Embeddingsearch").GetSection("ApiKeys").Get<List<string>>();
#pragma warning disable CS8604
        if (validApiKeys == null || !validApiKeys.Contains(extractedApiKey)) // CS8604 extractedApiKey is not null here, but the compiler still thinks that it might be.
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync("Invalid API Key.");
            return;
        }
#pragma warning restore CS8604

        await _next(context);
    }
}