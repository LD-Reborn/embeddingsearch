using ElmahCore;
using ElmahCore.Mvc;
using Serilog;
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Server;
using Server.HealthChecks;
using Server.Helper;
using Server.Models;
using Server.Services;
using System.Text.Json.Serialization;
using System.Reflection;
using System.Configuration;
using Microsoft.OpenApi.Models;
using Shared.Models;
using Microsoft.AspNetCore.ResponseCompression;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// Add Controllers with views & string conversion for enums
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter()
        );
    });

// Add Configuration
IConfigurationSection configurationSection = builder.Configuration.GetSection("Embeddingsearch");
EmbeddingSearchOptions configuration = configurationSection.Get<EmbeddingSearchOptions>() ?? throw new ConfigurationErrorsException("Unable to start server due to an invalid configration");

builder.Services.Configure<EmbeddingSearchOptions>(configurationSection);
builder.Services.Configure<ApiKeyOptions>(configurationSection);

// Add Localization
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("de") };
    options.DefaultRequestCulture = new RequestCulture("en");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});

// Add LocalizationService
builder.Services.AddScoped<LocalizationService>();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);
    if (configuration.ApiKeys is not null)
    {
        c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
        {
            Description = "ApiKey must appear in header",
            Type = SecuritySchemeType.ApiKey,
            Name = "X-API-KEY",
            In = ParameterLocation.Header,
            Scheme = "ApiKeyScheme"
        });
        var key = new OpenApiSecurityScheme()
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "ApiKey"
            },
            In = ParameterLocation.Header
        };
        var requirement = new OpenApiSecurityRequirement
        {
            { key, []}
        };
        c.AddSecurityRequirement(requirement);
    }
});
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
builder.Logging.AddSerilog();
builder.Services.AddSingleton<DatabaseHelper>();
builder.Services.AddSingleton<SearchdomainHelper>();
builder.Services.AddSingleton<SearchdomainManager>();
builder.Services.AddSingleton<AIProvider>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("DatabaseHealthCheck", tags: ["Database"])
    .AddCheck<AIProviderHealthCheck>("AIProviderHealthCheck", tags: ["AIProvider"]);

builder.Services.AddElmah<XmlFileErrorLog>(Options =>
{
    Options.OnPermissionCheck = context =>
        context.User.Claims.Any(claim =>
            claim.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            || claim.Value.Equals("Elmah", StringComparison.OrdinalIgnoreCase)
    );
    Options.LogPath = configuration.Elmah?.LogPath ?? "~/logs";
});

builder.Services
    .AddAuthentication("AppCookie")
    .AddCookie("AppCookie", options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Denied";
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly",
        policy => policy.RequireRole("Admin"));
});

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<GzipCompressionProvider>();
    options.Providers.Add<BrotliCompressionProvider>();
    options.MimeTypes =
    [
        "text/plain",
        "text/css",
        "application/javascript",
        "text/javascript",
        "text/html",
        "application/xml",
        "text/xml",
        "application/json",
        "image/svg+xml"
    ];
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseElmah();

app.MapHealthChecks("/healthz");
app.MapHealthChecks("/healthz/Database", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = c => c.Name.Contains("Database")
});

app.MapHealthChecks("/healthz/AIProvider", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = c => c.Name.Contains("AIProvider")
});

bool IsDevelopment = app.Environment.IsDevelopment();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/swagger"))
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            context.Response.Redirect($"/Account/Login?ReturnUrl={WebUtility.UrlEncode("/swagger")}");
            return;
        }

        if (!context.User.IsInRole("Admin"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
    }

    await next();
});

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.EnablePersistAuthorization();
});
//app.UseElmahExceptionPage(); // Messes with JSON response for API calls. Leaving this here so I don't accidentally put this in again later on.

if (configuration.ApiKeys is not null)
{
    app.UseWhen(context =>
    {
        RouteData routeData = context.GetRouteData();
        string controllerName = routeData.Values["controller"]?.ToString() ?? "StaticFile";
        if (controllerName == "Account" || controllerName == "Home" || controllerName == "StaticFile")
        {
            return false;
        }
        return true;
    }, appBuilder =>
    {
        appBuilder.UseMiddleware<Shared.ApiKeyMiddleware>();    
    });
}

app.UseResponseCompression();

// Add localization
var supportedCultures = new[] { "de", "de-DE", "en-US" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("de")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);
app.UseRequestLocalization(localizationOptions);

app.MapControllers();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        string requestPath = ctx.Context.Request.Path.ToString();
        string[] cachedSuffixes = [".css", ".js", ".png", ".ico", ".woff2"];
        if (cachedSuffixes.Any(suffix => requestPath.EndsWith(suffix)))
        {
            ctx.Context.Response.GetTypedHeaders().CacheControl =
                new Microsoft.Net.Http.Headers.CacheControlHeaderValue()
                {
                    Public = true,
                    MaxAge = TimeSpan.FromDays(365)
                };
        }
    }
});

app.Run();
