using ElmahCore;
using ElmahCore.Mvc;
using Serilog;
using Server;

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
builder.Services.AddSingleton<SearchdomainManager>();

builder.Services.AddElmah<XmlFileErrorLog>(Options =>
{
    Options.LogPath = builder.Configuration.GetValue<string>("Embeddingsearch:Elmah:LogFolder") ?? "~/logs";
});

var app = builder.Build();

List<string>? allowedIps = builder.Configuration.GetSection("Embeddingsearch:Elmah:AllowedHosts")
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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    //app.UseElmahExceptionPage(); // Messes with JSON response for API calls. Leaving this here so I don't accidentally put this in again later on.
}
else
{
    app.UseMiddleware<ApiKeyMiddleware>();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
