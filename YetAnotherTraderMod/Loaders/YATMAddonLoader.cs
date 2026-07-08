using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using System.Reflection;
using System.Threading.Tasks;
using YetAnotherTraderMod.src.Features.CustomConsumables;
using YetAnotherTraderMod.src.Services;

namespace YATMMedved.Loaders;

[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostDBModLoader + 10)]
public sealed class MyTonyAddonLoader(
    YATMTraderOfferFeedService yatmFeed,
    CustomConsumablesLoader customConsumablesLoader) : IOnLoad
{
    public async Task OnLoad()
    {
        var assembly = Assembly.GetExecutingAssembly();
        await yatmFeed.CreateTonyTraderOffers(assembly);
        await customConsumablesLoader.CreateCustomConsumables(assembly, Path.Join("db", "CustomConsumables"));
    }
}