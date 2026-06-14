using System.Reflection;
using Microsoft.Extensions.Localization;

namespace Shared.Services;

public class LocalizationService
{
    private readonly IStringLocalizer _localizer;

    public LocalizationService(IStringLocalizerFactory factory)
    {
        var assemblyName = Assembly.GetEntryAssembly()!.GetName().Name!;
        _localizer = factory.Create("SharedResources", assemblyName);
    }

    public string Get(string key) => _localizer[key];

    public string this[string key] => _localizer[key];
    public string this[string key, params object[] args] => _localizer[key, args];
}