using Indexer;
using Indexer.Models;
using Indexer.Services;
using ElmahCore;
using ElmahCore.Mvc;
using ElmahCore.Mvc.Logger;
using Serilog;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
builder.Logging.AddSerilog();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IConfigurationRoot>(builder.Configuration);
builder.Services.AddSingleton<Client.Client>();
builder.Services.AddSingleton<WorkerCollection>();
builder.Services.AddHostedService<IndexerService>();
builder.Services.AddHealthChecks()
    .AddCheck<WorkerHealthCheck>("WorkerHealthCheck");
builder.Services.AddElmah<XmlFileErrorLog>(Options =>
{
    Options.LogPath = builder.Configuration.GetValue<string>("EmbeddingsearchIndexer:Elmah:LogFolder") ?? "~/logs";
});

builder.Services.AddQuartz();

var app = builder.Build();
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

// app.UseHttpsRedirection();

app.MapControllers();

app.Run();
