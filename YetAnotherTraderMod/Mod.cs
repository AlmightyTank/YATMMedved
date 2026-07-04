using MoreBotsServer.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Services;
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
    MoreBotsServer.MoreBotsAPI moreBotsLib,
    MoreBotsCustomBotTypeService customBotTypeService,
    MoreBotsCustomBotConfigService customBotConfigService,
    FactionService factionService,
    LoadoutService loadoutService,
    DatabaseService databaseService,
    MedvedSpawnController medvedSpawnController,
    YATMLogger logger) : IOnLoad
{
    public async Task OnLoad()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var types = MedvedBotTypes.TypeList;

        // Create all four Medved bot types from db/bots/sharedTypes/medved.json.
        await moreBotsLib.LoadBotsShared(assembly, "medved", types);

        // MoreBotsAPI needs this mapping before faction reverse lookups can resolve custom enum values.
        customBotTypeService.AddCustomWildSpawnTypeNames(MedvedBotTypes.TypeDictionary);

        // Apply per-role sharedTypes/bossMedvedSokol.json, followerMedvedBuran.json, etc.
        await customBotTypeService.LoadBotTypeReplaceByTypes(assembly, types);

        // Apply loadout pools.
        await loadoutService.LoadLoadouts(assembly);

        // Apply db/bots/config/*.jsonc.
        await customBotConfigService.LoadCustomBotConfigs(assembly);

        if (!AllMedvedTypesReady(databaseService))
        {
            logger.Warning("Medved bot types did not finish loading cleanly; skipping faction enemy wiring to avoid a MoreBotsAPI null reference.");
            return;
        }

        // Medved should fight hostile PvE factions + USEC. Keep BEAR out unless you want BEAR hostility too.
        factionService.AddEnemyByFaction(types, "savage");
        factionService.AddEnemyByFaction(types, "rogues");
        factionService.AddEnemyByFaction(types, "usec");
        factionService.AddEnemyByFaction(types, "cultists");
        factionService.AddEnemyByFaction(types, "infected");

        // Make those factions recognize Medved as hostile. This requires MedvedFactionRegistration.cs.
        factionService.AddEnemyByFaction("savage", "medved");
        factionService.AddEnemyByFaction("rogues", "medved");
        factionService.AddEnemyByFaction("usec", "medved");
        factionService.AddEnemyByFaction("cultists", "medved");
        factionService.AddEnemyByFaction("infected", "medved");

        factionService.AddFriendlyByFaction(types, "medved");
        factionService.AddRevengeByFaction(types, "medved");

        medvedSpawnController.AdjustAllMedvedSpawns();
    }

    private static bool AllMedvedTypesReady(DatabaseService databaseService)
    {
        var botTypes = databaseService.GetTables().Bots.Types;

        foreach (var typeName in MedvedBotTypes.TypeList.Select(x => x.ToLowerInvariant()))
        {
            if (!botTypes.TryGetValue(typeName, out BotType? botType))
            {
                return false;
            }

            if (botType.BotDifficulty == null ||
                !botType.BotDifficulty.ContainsKey("easy") ||
                !botType.BotDifficulty.ContainsKey("normal") ||
                !botType.BotDifficulty.ContainsKey("hard") ||
                !botType.BotDifficulty.ContainsKey("impossible") ||
                botType.BotDifficulty["normal"]?.Mind == null)
            {
                return false;
            }

            if (botType.BotInventory == null)
            {
                return false;
            }
        }

        return true;
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
                (WildSpawnType)660012,
                (WildSpawnType)660013
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
