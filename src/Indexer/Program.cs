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

builder.Services.AddControllersWithViews();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
builder.Logging.AddSerilog();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IConfigurationRoot>(builder.Configuration);

IConfigurationSection configurationSection = builder.Configuration.GetSection("Indexer");
IndexerOptions configuration = configurationSection.Get<IndexerOptions>() ?? throw new ConfigurationErrorsException("Unable to start server due to an invalid configration");
builder.Services.Configure<IndexerOptions>(configurationSection);
builder.Services.Configure<ServerOptions>(configurationSection.GetSection("Server"));
builder.Services.Configure<AiProviderCollectionOptions>(configurationSection);
builder.Services.Configure<ApiKeyOptions>(configurationSection);
builder.Services.AddSingleton<Client.Client>();
builder.Services.AddSingleton<WorkerManager>();
builder.Services.AddSingleton<AIProviderService>();
builder.Services.AddHostedService<IndexerService>();
builder.Services.AddHealthChecks()
    .AddCheck<WorkerHealthCheck>("WorkerHealthCheck");
builder.Services.AddElmah<XmlFileErrorLog>(Options =>
{
    Options.LogPath = builder.Configuration.GetValue<string>("EmbeddingsearchIndexer:Elmah:LogFolder") ?? "~/logs";
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

List<string>? allowedIps = builder.Configuration.GetSection("EmbeddingsearchIndexer:Elmah:AllowedHosts")
    .Get<List<string>>();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/elmah"))
    {
        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        bool blockRequest = allowedIps is null
            || remoteIp is null
            || !allowedIps.Contains(remoteIp);
        if (blockRequest)
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync("Forbidden");
            return;
        }
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.UseElmah();

app.MapHealthChecks("/healthz");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseElmahExceptionPage();
}
else
{
    app.UseMiddleware<Shared.ApiKeyMiddleware>();
}

app.UseStaticFiles();

app.MapControllers();

app.Run();
