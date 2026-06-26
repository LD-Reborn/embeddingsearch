using Indexer;
using Indexer.Models;
using Indexer.Services;
using ElmahCore;
using ElmahCore.Mvc;
using ElmahCore.Mvc.Logger;
using Serilog;
using Quartz;
using System.Configuration;
using Shared.Models;
using Shared.Services;
using Microsoft.AspNetCore.Localization;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.OpenApi;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Add localization services
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("de") };
    options.DefaultRequestCulture = new RequestCulture("en");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});
builder.Services.AddScoped<LocalizationService>();

// Add Localization

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter()
        );
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IConfigurationRoot>(builder.Configuration);

IConfigurationSection configurationSection = builder.Configuration.GetSection("Indexer");
IndexerOptions configuration = configurationSection.Get<IndexerOptions>() ?? throw new ConfigurationErrorsException("Unable to start server due to an invalid configration");
builder.Services.Configure<IndexerOptions>(configurationSection);
builder.Services.Configure<ServerOptions>(configurationSection.GetSection("Server"));
builder.Services.Configure<AiProviderCollectionOptions>(configurationSection);
builder.Services.Configure<ApiKeyOptions>(configurationSection);
builder.Services.Configure<LdapOptions>(configurationSection.GetSection("Ldap"));
builder.Services.AddSingleton<LdapAuthenticationService>();
builder.Services.AddSingleton<Client.Client>();
builder.Services.AddSingleton<WorkerManager>();
builder.Services.AddSingleton<AIProviderService>();
builder.Services.AddHostedService<IndexerService>();
builder.Services.AddHealthChecks()
    .AddCheck<WorkerHealthCheck>("WorkerHealthCheck");

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, _) =>
    {
        if (configuration.ApiKeys is null)
            return Task.CompletedTask;

        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["ApiKey"] =
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                Name = "X-API-KEY",
                In = ParameterLocation.Header,
                Description = "ApiKey must appear in header"
            };
        
        document.Security ??= [];

        // Apply globally
        document.Security?.Add(
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("ApiKey", document)] = []
            }
        );

        return Task.CompletedTask;
    });
});
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
builder.Logging.AddSerilog();
builder.Services.AddElmah<XmlFileErrorLog>(Options =>
{
    Options.OnPermissionCheck = context =>
        context.User.Claims.Any(claim =>
            claim.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            || claim.Value.Equals("Elmah", StringComparison.OrdinalIgnoreCase)
    );
    Options.LogPath = configuration.Elmah?.LogPath ?? "./logs";
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

builder.Services.AddQuartz();

var app = builder.Build();

// Add localization
var supportedCultures = new[] { "de", "de-DE", "en-US" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("de")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);
app.UseRequestLocalization(localizationOptions);

app.UseAuthentication();
app.UseAuthorization();

// Configure Elmah
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/elmah"))
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.Append(
                "Content-Security-Policy",
                "default-src 'self' 'unsafe-inline' 'unsafe-eval'"
            );
            return Task.CompletedTask;
        });
    }

    await next();
});
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/elmah"))
    {
        await next();
        return;
    }

    var originalBody = context.Response.Body;
    using var memStream = new MemoryStream();
    context.Response.Body = memStream;

    await next();

    memStream.Position = 0;
    var html = await new StreamReader(memStream).ReadToEndAsync();

    if (context.Response.ContentType?.Contains("text/html") == true)
    {
        html = html.Replace(
            "</head>",
            """
            <link rel="stylesheet" href="/elmah-ui/custom.css" />
            <script src="/elmah-ui/custom.js"></script>
            </head>
            """
        );
    }

    var bytes = Encoding.UTF8.GetBytes(html);
    context.Response.ContentLength = bytes.Length;
    await originalBody.WriteAsync(bytes);
    context.Response.Body = originalBody;
});
app.UseElmah();

app.MapHealthChecks("/healthz");

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

app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "API v1");
    options.RoutePrefix = "swagger";
    options.EnablePersistAuthorization();
    options.InjectStylesheet("/swagger-ui/custom.css");
    options.InjectJavascript("/swagger-ui/custom.js");
});
app.MapOpenApi("/openapi/v1.json");

//app.UseElmahExceptionPage(); // Messes with JSON response for API calls. Leaving this here so I don't accidentally put this in again later on.

if (configuration.ApiKeys is not null)
{
    app.UseWhen(context =>
    {
        RouteData routeData = context.GetRouteData();
        string controllerName = routeData.Values["controller"]?.ToString() ?? "StaticFile";
        if (controllerName == "Account" || controllerName == "Dashboard" || controllerName == "StaticFile")
        {
            return false;
        }
        return true;
    }, appBuilder =>
    {
        appBuilder.UseMiddleware<Shared.ApiKeyMiddleware>();    
    });
}

app.UseStaticFiles();

app.MapControllers();

app.Run();
