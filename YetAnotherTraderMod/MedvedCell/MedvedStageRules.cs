using System.Collections.Generic;

namespace YATMMedved.MedvedCell;

public static class MedvedStageRules
{
    public static string GetDifficultyForStage(int stage)
    {
        return stage switch
        {
            <= 1 => "easy",
            2 => "normal",
            3 => "normal",
            4 => "hard",
            5 => "hard",
            >= 6 => "impossible"
        };
    }

    public static Dictionary<string, string> GetSpawnZonesForStage(int stage)
    {
        return stage switch
        {
            // Stage 0
            // Difficulty: easy
            // Maps: Reserve only
            <= 0 => new Dictionary<string, string>
            {
                ["rezervbase"] = "ZoneRailStrorage,ZoneKnight,ZoneBunkerStorage"
            },

            // Stage 1
            // Difficulty: easy
            // Maps: Customs + Reserve
            1 => new Dictionary<string, string>
            {
                ["bigmap"] = "ZoneDormitory,ZoneGasStation,ZoneFactoryCenter",
                ["rezervbase"] = "ZoneRailStrorage,ZoneKnight,ZoneBunkerStorage"
            },

            // Stage 2
            // Difficulty: normal
            // Maps: Customs + Reserve
            2 => new Dictionary<string, string>
            {
                ["bigmap"] = "ZoneDormitory,ZoneGasStation,ZoneFactoryCenter",
                ["rezervbase"] = "ZoneRailStrorage,ZoneKnight,ZoneBunkerStorage"
            },

            // Stage 3
            // Difficulty: normal
            // Maps: Customs + Reserve + Interchange
            3 => new Dictionary<string, string>
            {
                ["bigmap"] = "ZoneDormitory,ZoneGasStation,ZoneFactoryCenter",
                ["rezervbase"] = "ZoneRailStrorage,ZoneKnight,ZoneBunkerStorage",
                ["interchange"] = "ZoneCenter,ZoneIDEA,ZoneOLI"
            },

            // Stage 4
            // Difficulty: hard
            // Maps: Customs + Reserve + Interchange + Streets
            4 => new Dictionary<string, string>
            {
                ["bigmap"] = "ZoneDormitory,ZoneGasStation,ZoneFactoryCenter",
                ["rezervbase"] = "ZoneRailStrorage,ZoneKnight,ZoneBunkerStorage",
                ["interchange"] = "ZoneCenter,ZoneIDEA,ZoneOLI",
                ["tarkovstreets"] = "ZoneCarShowroom,ZoneCinema,ZoneHotel_1"
            },

            // Stage 5
            // Difficulty: hard
            // Maps: Customs + Reserve + Interchange + Streets + Ground Zero
            5 => new Dictionary<string, string>
            {
                ["bigmap"] = "ZoneDormitory,ZoneGasStation,ZoneFactoryCenter",
                ["rezervbase"] = "ZoneRailStrorage,ZoneKnight,ZoneBunkerStorage",
                ["interchange"] = "ZoneCenter,ZoneIDEA,ZoneOLI",
                ["tarkovstreets"] = "ZoneCarShowroom,ZoneCinema,ZoneHotel_1",
                ["sandbox"] = "ZoneSandboxMain,ZoneSandboxExit"
            },

            // Stage 6
            // Difficulty: impossible
            // Maps: All Medved maps/zones
            >= 6 => new Dictionary<string, string>
            {
                ["bigmap"] = "ZoneDormitory,ZoneGasStation,ZoneFactoryCenter",
                ["rezervbase"] = "ZoneRailStrorage,ZoneKnight,ZoneBunkerStorage",
                ["interchange"] = "ZoneCenter,ZoneIDEA,ZoneOLI",
                ["tarkovstreets"] = "ZoneCarShowroom,ZoneCinema,ZoneHotel_1",
                ["sandbox"] = "ZoneSandboxMain,ZoneSandboxExit"
            }
        };
    }
}
