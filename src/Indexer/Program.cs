using Indexer;
using Indexer.Models;
using Indexer.Services;
using ElmahCore;
using ElmahCore.Mvc;
using Server;
using ElmahCore.Mvc.Logger;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
Log.Logger = new LoggerConfiguration()
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)  // Output files with daily rolling
    .CreateLogger();
builder.Logging.AddSerilog();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<Client.Client>();
builder.Services.AddSingleton<WorkerCollection>();
builder.Services.AddHostedService<IndexerService>();
builder.Services.AddHealthChecks()
    .AddCheck<WorkerHealthCheck>("WorkerHealthCheck");

builder.Services.AddElmah<XmlFileErrorLog>(Options =>
{
    Options.LogPath = "~/logs";
});

var app = builder.Build();
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
    app.UseMiddleware<ApiKeyMiddleware>();
}

// app.UseHttpsRedirection();

app.Run();
