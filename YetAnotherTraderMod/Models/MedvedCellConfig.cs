using System;
using System.Collections.Generic;

namespace YATMMedved.Models;

public class MedvedCellConfig
{
    public bool Enabled { get; set; } = true;
    public bool DebugForceSpawn { get; set; } = false;

    // Does not block other bosses or Goons. It only removes configured Medved zones
    // that are already used by active boss waves on the same map.
    public bool AvoidTakenBossZones { get; set; } = true;

    // Base spawn table before progression stages are unlocked.
    public Dictionary<string, int> SpawnChances { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bigmap"] = 3,
        ["rezervbase"] = 6,
        ["tarkovstreets"] = 2,
        ["sandbox"] = 2,
        ["interchange"] = 3
    };

    // Base spawn zones before progression stages are unlocked.
    public Dictionary<string, string> SpawnZones { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bigmap"] = "ZoneDormitory,ZoneGasStation,ZoneFactoryCenter",
        ["rezervbase"] = "ZoneRailStrorage,ZoneKnight,ZoneBunkerStorage",
        ["tarkovstreets"] = "ZoneCarShowroom,ZoneCinema,ZoneHotel_1",
        ["sandbox"] = "ZoneSandboxMain,ZoneSandboxExit",
        ["interchange"] = "ZoneCenter,ZoneIDEA,ZoneOLI"
    };

    // Optional progression rules. Stages are applied in order. Each unlocked stage
    // overlays SpawnChances and SpawnZones. This allows small incremental changes
    // across several quests and several maps.
    public MedvedQuestProgressionConfig QuestProgression { get; set; } = new();
}

public class MedvedQuestProgressionConfig
{
    public bool Enabled { get; set; } = true;

    // Stages are applied in order:
    // base -> stage 1 -> stage 2 -> stage 3 -> stage 4 -> stage 5 -> stage 6.
    public List<MedvedQuestProgressionStage> Stages { get; set; } = new()
    {
        new MedvedQuestProgressionStage
        {
            Name = "Stage 1 - Medved Attention",
            RequiredQuestIds = new List<string> { "6a300025d2999bc7b9bba916" },
            RequireAllQuestIds = true,
            SpawnChances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["bigmap"] = 5,
                ["rezervbase"] = 7,
                ["tarkovstreets"] = 4,
                ["sandbox"] = 3,
                ["interchange"] = 4
            }
        },
        new MedvedQuestProgressionStage
        {
            Name = "Stage 2 - Medved Moves Woods",
            RequiredQuestIds = new List<string> { "PUT_QUEST_ID_HERE_STAGE_2" },
            RequireAllQuestIds = true,
            SpawnChances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["bigmap"] = 6,
                ["rezervbase"] = 8,
                ["tarkovstreets"] = 5,
                ["sandbox"] = 4,
                ["interchange"] = 5,
                ["woods"] = 4
            },
            SpawnZones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["woods"] = "ZoneWoodCutter,ZoneScavBase2,ZoneHighPine"
            }
        },
        new MedvedQuestProgressionStage
        {
            Name = "Stage 3 - Medved Shoreline Contact",
            RequiredQuestIds = new List<string> { "PUT_QUEST_ID_HERE_STAGE_3" },
            RequireAllQuestIds = true,
            SpawnChances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["bigmap"] = 7,
                ["rezervbase"] = 9,
                ["tarkovstreets"] = 6,
                ["sandbox"] = 4,
                ["interchange"] = 6,
                ["woods"] = 5,
                ["shoreline"] = 4
            },
            SpawnZones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["shoreline"] = "ZoneSanatorium1,ZoneSanatorium2,ZonePort"
            }
        },
        new MedvedQuestProgressionStage
        {
            Name = "Stage 4 - Medved Lighthouse Route",
            RequiredQuestIds = new List<string> { "PUT_QUEST_ID_HERE_STAGE_4" },
            RequireAllQuestIds = true,
            SpawnChances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["bigmap"] = 8,
                ["rezervbase"] = 10,
                ["tarkovstreets"] = 7,
                ["sandbox"] = 5,
                ["interchange"] = 7,
                ["woods"] = 6,
                ["shoreline"] = 5,
                ["lighthouse"] = 4
            },
            SpawnZones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["lighthouse"] = "Zone_Chalet,Zone_RoofContainers,Zone_TreatmentRocks"
            }
        },
        new MedvedQuestProgressionStage
        {
            Name = "Stage 5 - Medved Network Active",
            RequiredQuestIds = new List<string> { "PUT_QUEST_ID_HERE_STAGE_5" },
            RequireAllQuestIds = true,
            SpawnChances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["bigmap"] = 10,
                ["rezervbase"] = 12,
                ["tarkovstreets"] = 9,
                ["sandbox"] = 6,
                ["interchange"] = 9,
                ["woods"] = 7,
                ["shoreline"] = 6,
                ["lighthouse"] = 5
            }
        },
        new MedvedQuestProgressionStage
        {
            Name = "Stage 6 - Medved Cell Fully Active",
            RequiredQuestIds = new List<string> { "PUT_QUEST_ID_HERE_STAGE_6" },
            RequireAllQuestIds = true,
            SpawnChances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["bigmap"] = 12,
                ["rezervbase"] = 14,
                ["tarkovstreets"] = 11,
                ["sandbox"] = 7,
                ["interchange"] = 11,
                ["woods"] = 9,
                ["shoreline"] = 8,
                ["lighthouse"] = 7
            }
        }
    };

    // Legacy single-quest fields are kept so older configs do not break.
    // If QuestId is set and completed, CompletedSpawnChances/CompletedSpawnZones are applied
    // after the stage list.
    public string QuestId { get; set; } = string.Empty;
    public Dictionary<string, int> CompletedSpawnChances { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> CompletedSpawnZones { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class MedvedQuestProgressionStage
{
    public string Name { get; set; } = string.Empty;

    // Use one quest or several. If RequireAllQuestIds is true, all IDs must be completed.
    // If false, any one completed quest unlocks this stage.
    public List<string> RequiredQuestIds { get; set; } = new();
    public bool RequireAllQuestIds { get; set; } = true;

    // These override/add to the current effective spawn chances when this stage is unlocked.
    // Any new map added here becomes active once the stage unlocks.
    public Dictionary<string, int> SpawnChances { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // These override/add to the current effective spawn zones when this stage is unlocked.
    public Dictionary<string, string> SpawnZones { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
