using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using System.Reflection;
using System.Threading.Tasks;
using YetAnotherTraderMod.src;
using YetAnotherTraderMod.src.Features.CustomConsumables;
using YetAnotherTraderMod.src.Services;

namespace YATMMedved.Loaders;

[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostDBModLoader + 3)]
public sealed class YATMAddonLoader(
    YATMCommonLib yatmCommon) : IOnLoad
{
    public async Task OnLoad()
    {
        var assembly = Assembly.GetExecutingAssembly();
        await yatmCommon.CustomTraderOfferServiceExtended.CreateCustomTraderOffers(assembly, Path.Join("db", "CustomTraderOffers"));
        await yatmCommon.CustomConsumablesServiceExtended.CreateCustomConsumables(assembly, Path.Join("db", "CustomConsumables"));
    }
}