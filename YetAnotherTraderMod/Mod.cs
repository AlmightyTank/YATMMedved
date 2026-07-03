using MoreBotsServer.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using System.Reflection;
using WTTServerCommonLib;
using YATMMedved.Controllers;
using YATMMedved.Models;

namespace YATMMedved;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.almightytank.yatm.medved";
    public override string Name { get; init; } = "YATM Medved Cell";
    public override string Author { get; init; } = "AlmightyTank";
    public override List<string>? Contributors { get; init; } = [];
    public override SemanticVersioning.Version Version { get; init; } = new("0.1.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.13");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
    {
        { "com.morebotsapi.tacticaltoaster", new SemanticVersioning.Range(">=2.0.1") },
        { "com.wtt.commonlib", new SemanticVersioning.Range(">=2.0.20") }
    };
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; }
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PreSptModLoader + 10)]
public class YATMModPreload(ModHelper modHelper, YATMLogger logger) : IOnLoad
{
    public static MainConfig ModConfig { get; private set; } = new();

    public Task OnLoad()
    {
        var pathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        ModConfig = modHelper.GetJsonDataFromFile<MainConfig>(pathToMod, "config.jsonc") ?? new MainConfig();
        logger.Info("Loaded config.jsonc.");
        return Task.CompletedTask;
    }
}

[Injectable(InjectionType = InjectionType.Singleton, TypePriority = MoreBotsServer.MoreBotsLoadOrder.LoadBots)]
public class YATMMedvedBots(
    MoreBotsCustomBotTypeService customBotTypeService,
    MoreBotsCustomBotConfigService customBotConfigService,
    FactionService factionService,
    LoadoutService loadoutService,
    WTTServerCommonLib.WTTServerCommonLib commonLib,
    MedvedSpawnController medvedSpawnController,
    YATMLogger logger
) : IOnLoad
{
    public async Task OnLoad()
    {
        var typeList = new List<string>
        {
            "bossMedvedSokol",
            "followerMedvedBuran",
            "followerMedvedKedr"
        };

        var assembly = Assembly.GetExecutingAssembly();

        logger.Info("Loading MoreBotsAPI custom bot types.");
        await customBotTypeService.CreateCustomBotTypes(assembly);

        logger.Info("Loading shared bot config: db/bots/sharedConfig/medved.jsonc.");
        await customBotConfigService.LoadCustomBotConfigsShared(assembly, "medved", typeList);

        logger.Info("Loading Medved loadouts.");
        await loadoutService.LoadLoadouts(assembly);

        logger.Info("Applying Medved shared type replacements.");
        await customBotTypeService.LoadBotTypeReplace(assembly, "medved_names", typeList);

        // Keep relationship arrays empty in bot type JSON. Use faction service for relationships.
        factionService.AddEnemyByFaction(typeList, "criminals");
        factionService.AddEnemyByFaction(typeList, "cultists");
        factionService.AddEnemyByFaction(typeList, "infected");
        factionService.AddEnemyByFaction(typeList, "pmcs");

        factionService.AddEnemyByFaction("criminals", "medved");
        factionService.AddEnemyByFaction("cultists", "medved");
        factionService.AddEnemyByFaction("infected", "medved");
        factionService.AddEnemyByFaction("pmcs", "medved");

        factionService.AddRevengeByFaction(typeList, "medved");

        await commonLib.CustomLocaleService.CreateCustomLocales(assembly);

        medvedSpawnController.AdjustAllMedvedSpawns();

        await Task.CompletedTask;
    }
}

[Injectable(InjectionType = InjectionType.Singleton, TypePriority = MoreBotsServer.MoreBotsLoadOrder.LoadFactions)]
public class YATMMedvedFaction(FactionService factionService) : IOnLoad
{
    public Task OnLoad()
    {
        factionService.Factions.Add("medved", new Faction
        {
            Name = "medved",
            BotTypes =
            {
                (WildSpawnType)660010,
                (WildSpawnType)660011,
                (WildSpawnType)660012
            },
            RevengeAfterRaids = true,
            RevengeRaidAmount = 3
        });

        return Task.CompletedTask;
    }
}

[Injectable]
public class YATMMedvedStaticRouter : StaticRouter
{
    private static MedvedSpawnController _medvedSpawnController = null!;

    public YATMMedvedStaticRouter(
        MedvedSpawnController medvedSpawnController,
        JsonUtil jsonUtil,
        HttpResponseUtil httpResponseUtil) : base(jsonUtil, GetCustomRoutes())
    {
        _medvedSpawnController = medvedSpawnController;
    }

    private static List<RouteAction> GetCustomRoutes()
    {
        return
        [
            new RouteAction(
                "/client/locations",
                async (
                    url,
                    info,
                    sessionID,
                    output
                ) =>
                {
                    // This route has the active profile sessionID. Update the spawn table here
                    // so quest-completion progression is applied before raid location data is used.
                    _medvedSpawnController.AdjustAllMedvedSpawns(sessionID);
                    return await new ValueTask<object>(output ?? string.Empty);
                }
            ),
            new RouteAction(
                "/client/match/local/end",
                async (
                    url,
                    info,
                    sessionID,
                    output
                ) =>
                {
                    _medvedSpawnController.AdjustAllMedvedSpawns(sessionID);
                    return await new ValueTask<object>(output ?? string.Empty);
                }
            )
        ];
    }
}
