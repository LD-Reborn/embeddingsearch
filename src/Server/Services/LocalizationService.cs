using Microsoft.Extensions.Localization;

namespace Server.Services;

public class LocalizationService
{
    private readonly IStringLocalizer _localizer;

    public LocalizationService(IStringLocalizerFactory factory)
    {
        _localizer = factory.Create("SharedResources", "Server");
    }

    public string Get(string key) => _localizer[key];

    public string this[string key] => _localizer[key];
    public string this[string key, params object[] args] => _localizer[key, args];
}