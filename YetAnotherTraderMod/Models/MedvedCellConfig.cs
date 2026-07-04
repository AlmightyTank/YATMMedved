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

    // Stages are checked in order. The highest completed stage becomes the active
    // Medved stage. Stage data should only identify quest requirements.
    public List<MedvedQuestProgressionStage> Stages { get; set; } = new()
    {
        new MedvedQuestProgressionStage
        {
            Name = "Stage 1 - Medved Attention",
            RequiredQuestIds = new List<string> { "yatm_medved_bear_tracks" },
            RequireAllQuestIds = true
        },
        new MedvedQuestProgressionStage
        {
            Name = "Stage 2 - Kedr Quieted",
            RequiredQuestIds = new List<string> { "yatm_medved_voices_in_the_static" },
            RequireAllQuestIds = true
        },
        new MedvedQuestProgressionStage
        {
            Name = "Stage 3 - Buran Broken",
            RequiredQuestIds = new List<string> { "yatm_medved_the_heavy_one" },
            RequireAllQuestIds = true
        },
        new MedvedQuestProgressionStage
        {
            Name = "Stage 4 - Sokol Exposed",
            RequiredQuestIds = new List<string> { "yatm_medved_cut_the_falcon" },
            RequireAllQuestIds = true
        },
        new MedvedQuestProgressionStage
        {
            Name = "Stage 5 - No Loose Ends",
            RequiredQuestIds = new List<string> { "yatm_medved_no_loose_ends" },
            RequireAllQuestIds = true
        },
        new MedvedQuestProgressionStage
        {
            Name = "Stage 6 - Volkov's Warning",
            RequiredQuestIds = new List<string> { "yatm_medved_volkovs_warning" },
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
