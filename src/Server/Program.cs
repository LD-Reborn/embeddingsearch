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

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter()
        );
    });

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
    .AddCheck<DatabaseHealthCheck>("DatabaseHealthCheck")
    .AddCheck<AIProviderHealthCheck>("AIProviderHealthCheck");

builder.Services.AddElmah<XmlFileErrorLog>(Options =>
{
    Options.LogPath = builder.Configuration.GetValue<string>("Embeddingsearch:Elmah:LogFolder") ?? "~/logs";
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

IConfigurationSection simpleAuthSection = builder.Configuration.GetSection("Embeddingsearch:SimpleAuth");
if (simpleAuthSection.Exists()) builder.Services.Configure<SimpleAuthOptions>(simpleAuthSection);

var app = builder.Build();

List<string>? allowedIps = builder.Configuration.GetSection("Embeddingsearch:Elmah:AllowedHosts")
    .Get<List<string>>();

app.Use(async (context, next) =>
{
    bool requestIsElmah = context.Request.Path.StartsWithSegments("/elmah");
    bool requestIsSwagger = context.Request.Path.StartsWithSegments("/swagger");
    
    if (requestIsElmah || requestIsSwagger)
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

app.UseElmah();

app.MapHealthChecks("/healthz");

bool IsDevelopment = app.Environment.IsDevelopment();
bool useSwagger = app.Configuration.GetValue<bool>("UseSwagger");
bool? UseMiddleware = app.Configuration.GetValue<bool?>("UseMiddleware");

// Configure the HTTP request pipeline.
if (IsDevelopment || useSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI();
    //app.UseElmahExceptionPage(); // Messes with JSON response for API calls. Leaving this here so I don't accidentally put this in again later on.
}
if (UseMiddleware == true && !IsDevelopment)
{
    app.UseMiddleware<Shared.ApiKeyMiddleware>();
}

// Add localization
var supportedCultures = new[] { "de", "de-DE", "en-US" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("de")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);
app.UseRequestLocalization(localizationOptions);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.UseStaticFiles();

app.Run();
