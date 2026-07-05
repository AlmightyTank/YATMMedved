using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using YATMMedved.MedvedCell;
using YATMMedved.Models;
using YATMMedved.Services;

namespace YATMMedved.Controllers;

[Injectable(InjectionType.Singleton)]
public class MedvedSpawnController(
    ConfigController configController,
    DatabaseService databaseService,
    MedvedQuestStateService questStateService,
    YATMLogger logger)
{
    private const string Sokol = "bossMedvedSokol";
    private const string Buran = "followerMedvedBuran";
    private const string Kedr = "followerMedvedKedr";
    private const string Medved = "followerMedved";

    private readonly ConfigController _configController = configController;
    private readonly DatabaseService _databaseService = databaseService;
    private readonly MedvedQuestStateService _questStateService = questStateService;
    private readonly YATMLogger _logger = logger;

    public void AdjustAllMedvedSpawns(string? sessionId = null)
    {
        try
        {
            var config = _configController.ModConfig.MedvedCell ?? new MedvedCellConfig();
            if (!config.Enabled)
            {
                _logger.Info("Medved Cell spawns disabled in config.");
                return;
            }

            var unlockedStages = _questStateService.GetUnlockedStages(sessionId, config.QuestProgression);
            var legacyQuestCompleted = _questStateService.IsLegacyProgressionQuestCompleted(sessionId, config.QuestProgression);
            var medvedStage = ResolveMedvedStage(config, unlockedStages, legacyQuestCompleted);
            var difficulty = MedvedStageRules.GetDifficultyForStage(medvedStage);
            var stageSpawnZones = MedvedStageRules.GetSpawnZonesForStage(medvedStage);

            if (unlockedStages.Count > 0)
            {
                _logger.Info($"Medved progression active. Unlocked stage(s): {string.Join(", ", unlockedStages.Select(x => x.Name))}.");
            }

            if (legacyQuestCompleted)
            {
                _logger.Info($"Medved legacy progression quest {config.QuestProgression.QuestId} completed. Applying Stage 6 rules.");
            }

            _logger.Debug(
                $"Medved Cell stage resolved: Stage {medvedStage}, difficulty={difficulty}, " +
                $"maps={string.Join(", ", stageSpawnZones.Keys)}."
            );

            var tables = _databaseService.GetTables();
            var locations = _databaseService.GetLocations();
            var tableLocations = tables.Locations.GetDictionary();
            var locationDictionary = locations.GetDictionary();

            foreach (var mapEntry in stageSpawnZones)
            {
                var map = mapEntry.Key;
                var chance = GetSpawnChanceForMap(map, config);

                if (config.DebugForceSpawn)
                {
                    chance = 100;
                }

                chance = Math.Clamp(chance, 0, 100);

                if (chance <= 0 && !config.DebugForceSpawn)
                {
                    continue;
                }

                var mappedKey = locations.GetMappedKey(map);
                if (!tableLocations.ContainsKey(mappedKey) || !locationDictionary.ContainsKey(mappedKey))
                {
                    _logger.Warning($"No location data found for {map} / {mappedKey}. Skipping.");
                    continue;
                }

                var location = locationDictionary[mappedKey].Base;
                var spawns = location.BossLocationSpawn;

                if (spawns == null)
                {
                    _logger.Warning($"{map} has no BossLocationSpawn list. Skipping.");
                    continue;
                }

                var removed = spawns.RemoveAll(IsMedvedWave);
                if (removed > 0)
                {
                    _logger.Info($"Removed {removed} previous Medved wave(s) from {map}.");
                }

                var zone = GetMedvedBossZones(map, mapEntry.Value, config, spawns);
                if (string.IsNullOrWhiteSpace(zone))
                {
                    _logger.Warning($"{map} has no configured or discoverable BossZone. Spawn may not work.");
                }

                var wave = GenerateMedvedWave(chance, zone, difficulty);
                var supportText = wave.Supports == null || !wave.Supports.Any()
                    ? "none"
                    : string.Join(", ", wave.Supports.Select(x => $"{x.BossEscortType} x{x.BossEscortAmount}"));

                _logger.Info(
                    $"Medved wave built for {map}: boss={wave.BossName}, " +
                    $"difficulty={wave.BossDifficulty}, " +
                    $"escort={wave.BossEscortType} x{wave.BossEscortAmount}, " +
                    $"supports={supportText}, " +
                    $"zone={wave.BossZone}."
                );

                spawns.Add(wave);
                _logger.Info($"Added Medved Cell to {map}: chance={chance}, difficulty={difficulty}, zone={zone}.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error adjusting Medved spawns: {ex.Message}");
            throw;
        }
    }

    private static BossLocationSpawn GenerateMedvedWave(int chance, string zone, string difficulty)
    {
        return new BossLocationSpawn
        {
            BossChance = chance,
            BossDifficulty = difficulty,

            // One regular Medved escort with Sokol.
            // Quest progression must not change the boss group makeup.
            BossEscortAmount = "1",
            BossEscortDifficulty = difficulty,
            BossEscortType = Medved,

            BossName = Sokol,
            IsBossPlayer = false,
            BossZone = zone,

            ForceSpawn = false,
            IgnoreMaxBots = true,
            IsRandomTimeSpawn = false,
            SpawnMode = ["regular", "pve"],

            // Named supports stay the same. Only their difficulty follows the stage.
            Supports =
            [
                new BossSupport
                {
                    BossEscortAmount = "1",
                    BossEscortDifficulty = new ListOrT<string>(new List<string> { difficulty }, null),
                    BossEscortType = Kedr
                },
                new BossSupport
                {
                    BossEscortAmount = "1",
                    BossEscortDifficulty = new ListOrT<string>(new List<string> { difficulty }, null),
                    BossEscortType = Buran
                }
            ],

            Time = -1,
            TriggerId = string.Empty,
            TriggerName = string.Empty
        };
    }

    private string GetMedvedBossZones(
        string map,
        string stageZoneString,
        MedvedCellConfig config,
        List<BossLocationSpawn> spawns)
    {
        var configuredZones = SplitBossZones(stageZoneString).ToList();

        if (configuredZones.Count == 0)
        {
            var existingZone = TryGetExistingBossZone(spawns);
            return existingZone ?? string.Empty;
        }

        if (!config.AvoidTakenBossZones)
        {
            var selectedZone = PickOneBossZone(configuredZones);
            _logger.Info($"{map}: Medved selected boss zone: {selectedZone}.");
            return selectedZone;
        }

        var takenZones = GetTakenBossZones(spawns);
        var freeZones = configuredZones
            .Where(zone => !takenZones.Contains(zone))
            .ToList();

        if (freeZones.Count > 0)
        {
            if (freeZones.Count != configuredZones.Count)
            {
                _logger.Info($"{map}: Medved avoiding taken boss zone(s): {string.Join(",", configuredZones.Except(freeZones, StringComparer.OrdinalIgnoreCase))}.");
            }

            var selectedZone = PickOneBossZone(freeZones);
            _logger.Info($"{map}: Medved selected free boss zone: {selectedZone}.");
            return selectedZone;
        }

        var fallbackZone = PickOneBossZone(configuredZones);
        _logger.Warning($"{map}: all configured Medved boss zones are already used by active boss waves. Falling back to one configured zone: {fallbackZone}.");
        return fallbackZone;
    }

    private static int ResolveMedvedStage(
        MedvedCellConfig config,
        List<MedvedQuestProgressionStage> unlockedStages,
        bool legacyQuestCompleted)
    {
        if (legacyQuestCompleted)
        {
            return 6;
        }

        var configuredStages = config.QuestProgression?.Stages ?? new List<MedvedQuestProgressionStage>();
        var highestStage = 0;

        for (var index = 0; index < configuredStages.Count; index++)
        {
            var configuredStage = configuredStages[index];
            var unlocked = unlockedStages.Any(stage =>
                ReferenceEquals(stage, configuredStage)
                || string.Equals(stage.Name, configuredStage.Name, StringComparison.OrdinalIgnoreCase));

            if (unlocked)
            {
                highestStage = index + 1;
            }
        }

        return Math.Clamp(highestStage, 0, 6);
    }

    private static int GetSpawnChanceForMap(string map, MedvedCellConfig config)
    {
        if (config.SpawnChances != null && config.SpawnChances.TryGetValue(map, out var chance))
        {
            return chance;
        }

        return 0;
    }

    private static string PickOneBossZone(List<string> zones)
    {
        if (zones.Count == 0)
        {
            return string.Empty;
        }

        if (zones.Count == 1)
        {
            return zones[0];
        }

        return zones[Random.Shared.Next(zones.Count)];
    }

    private static HashSet<string> GetTakenBossZones(List<BossLocationSpawn> spawns)
    {
        var takenZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var spawn in spawns)
        {
            if (IsMedvedWave(spawn)
                || string.IsNullOrWhiteSpace(spawn.BossName)
                || (spawn.BossChance ?? 0d) <= 0d)
            {
                continue;
            }

            foreach (var zone in SplitBossZones(spawn.BossZone))
            {
                takenZones.Add(zone);
            }
        }

        return takenZones;
    }

    private static IEnumerable<string> SplitBossZones(string? zoneString)
    {
        return (zoneString ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(zone => !string.IsNullOrWhiteSpace(zone));
    }

    private static string? TryGetExistingBossZone(List<BossLocationSpawn> spawns)
    {
        return spawns.Select(x => x.BossZone).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }

    private static bool IsMedvedWave(BossLocationSpawn spawn)
    {
        return IsMedvedRole(spawn.BossName)
            || IsMedvedRole(spawn.BossEscortType)
            || (spawn.Supports?.Any(x => IsMedvedRole(x.BossEscortType)) ?? false);
    }

    private static bool IsMedvedRole(string? role)
    {
        return string.Equals(role, Sokol, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, Buran, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, Kedr, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, Medved, StringComparison.OrdinalIgnoreCase);
    }
}
