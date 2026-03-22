using Microsoft.Extensions.Hosting;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Shared.Helper;

public static class ConfigHelper
{

    public static void UpdateSetting(
        IHostEnvironment hostingEnvironment,
        string keyPath,
        object value)
    {
        string basePath = hostingEnvironment.ContentRootPath;
        string environment = hostingEnvironment.EnvironmentName;
        UpdateSetting(basePath, environment, keyPath, value);
    }

    public static void UpdateSetting(
        string basePath,
        string keyPath,
        object value)
    {
        string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Development";
        UpdateSetting(basePath, environment, keyPath, value);
    }

    public static void UpdateSetting(
        string basePath,
        string environment,
        string keyPath,
        object value)
    {
        var fileName = $"appsettings.{environment}.json";
        var fullPath = Path.Combine(basePath, fileName);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Config file not found: {fullPath}");
        }

        var json = File.ReadAllText(fullPath);

        var jsonNode = JsonNode.Parse(json) as JsonObject 
                       ?? [];

        SetValue(jsonNode, keyPath.Split(':'), value);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        File.WriteAllText(fullPath, jsonNode.ToJsonString(options));
    }

    private static void SetValue(JsonObject root, string[] path, object value)
    {
        JsonObject current = root;

        for (int i = 0; i < path.Length - 1; i++)
        {
            var key = path[i];

            if (current[key] is not JsonObject next)
            {
                next = [];
                current[key] = next;
            }

            current = next;
        }

        var finalKey = path[^1];
        current[finalKey] = JsonValue.Create(value);
    }
}