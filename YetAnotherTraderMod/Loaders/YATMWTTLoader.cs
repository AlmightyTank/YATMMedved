using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace YATMMedved.Loaders;

[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostDBModLoader + 5)]
public sealed class YATMWTTLoader(
    WTTServerCommonLib.WTTServerCommonLib wttCommon,
    YATMLogger logger) : IOnLoad
{
    private readonly WTTServerCommonLib.WTTServerCommonLib _wttCommon = wttCommon;
    private readonly YATMLogger _logger = logger;

    public Task OnLoad()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();

            _wttCommon.CustomLocaleService.CreateCustomLocales(assembly);

            _logger.Info("Loading WTT Medved custom quest zones...");
            _wttCommon.CustomQuestZoneService.CreateCustomQuestZones(assembly);

            _logger.Info("Loading WTT Medved custom quests...");
            _wttCommon.CustomQuestService.CreateCustomQuests(assembly);

            _logger.Info("WTT Medved quest loader finished.");
        }
        catch (Exception ex)
        {
            _logger.Error($"WTT Medved quest loader failed: {ex.Message}");
            throw;
        }

        return Task.CompletedTask;
    }
}
