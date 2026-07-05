using System;
using System.Collections.Generic;

namespace YATMMedved.Models;

public class MedvedCellConfig
{
    public bool Enabled { get; set; } = true;
    public bool IsDebugEnabled { get; set; } = false;
    public bool IsRealDebugEnabled { get; set; } = false;
    public bool DebugForceSpawn { get; set; } = false;

    // Does not block other bosses or Goons. It only removes configured Medved zones
    // that are already used by active boss waves on the same map.
    public bool AvoidTakenBossZones { get; set; } = true;

    // Spawn chance is config-driven and does not change from quest progression.
    // Quest progression only controls allowed maps/zones and difficulty.
    public Dictionary<string, int> SpawnChances { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bigmap"] = 3,
        ["rezervbase"] = 4,
        ["tarkovstreets"] = 2,
        ["sandbox"] = 2,
        ["interchange"] = 3
    };

    // Fallback/default zone list. Stage-specific allowed zones are controlled by
    // MedvedCell/MedvedStageRules.cs so quest progression stays code-defined.
    public Dictionary<string, string> SpawnZones { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bigmap"] = "ZoneDormitory,ZoneGasStation,ZoneFactoryCenter",
        ["rezervbase"] = "ZoneRailStrorage,ZoneKnight,ZoneBunkerStorage",
        ["tarkovstreets"] = "ZoneCarShowroom,ZoneCinema,ZoneHotel_1",
        ["sandbox"] = "ZoneSandboxMain,ZoneSandboxExit",
        ["interchange"] = "ZoneCenter,ZoneIDEA,ZoneOLI"
    };

    // WTT quest completion controls which Medved stage is active.
    // The stage changes only difficulty and allowed spawn maps/zones.
    public MedvedQuestProgressionConfig QuestProgression { get; set; } = new();
}

public class MedvedQuestProgressionConfig
{
    public bool Enabled { get; set; } = true;

    // Tony main story completion controls Medved stage 1-6.
    // YATMMedved only adds one WTT quest: yatm_medved_the_last_cell.
    // That final quest should require Tony Quest 30 / Stage 6, but it does not
    // control the boss spawn ramp itself.
    public List<MedvedQuestProgressionStage> Stages { get; set; } = new()
    {
        new MedvedQuestProgressionStage
        {
            Name = "Stage 1 - First Medved Movement / Tony Quest 15",
            RequiredQuestIds = new List<string> { "66aa0000000000000000000f" },
            RequireAllQuestIds = true
        },
        new MedvedQuestProgressionStage
        {
            Name = "Stage 2 - Medved Pressure / Tony Quest 19",
            RequiredQuestIds = new List<string> { "66aa00000000000000000013" },
            RequireAllQuestIds = true
        },
        new MedvedQuestProgressionStage
        {
            Name = "Stage 3 - Medved Expansion / Tony Quest 23",
            RequiredQuestIds = new List<string> { "66aa00000000000000000017" },
            RequireAllQuestIds = true
        },
        new MedvedQuestProgressionStage
        {
            Name = "Stage 4 - Medved Hard Hunt / Tony Quest 28",
            RequiredQuestIds = new List<string> { "66aa0000000000000000001c" },
            RequireAllQuestIds = true
        },
        new MedvedQuestProgressionStage
        {
            Name = "Stage 5 - Last Warning / Tony Quest 29",
            RequiredQuestIds = new List<string> { "66aa0000000000000000001d" },
            RequireAllQuestIds = true
        },
        new MedvedQuestProgressionStage
        {
            Name = "Stage 6 - Full Cell Active / Tony Quest 30",
            RequiredQuestIds = new List<string> { "66aa0000000000000000001e" },
            RequireAllQuestIds = true
        }
    };

    // Legacy single-quest fields are kept so older configs do not break.
    // If QuestId is set and completed, MedvedSpawnController applies Stage 6 rules.
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

    // Kept for backward config compatibility only. The current spawn controller does
    // not use these for progression because progression should only change difficulty
    // and allowed map/zones from MedvedStageRules.
    public Dictionary<string, int> SpawnChances { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> SpawnZones { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
