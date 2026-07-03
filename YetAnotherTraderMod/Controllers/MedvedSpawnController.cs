using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils.Json;
using System;
using System.Collections.Generic;
using System.Linq;
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
            var effectiveSpawnChances = GetEffectiveSpawnChances(config, unlockedStages, legacyQuestCompleted);

            if (unlockedStages.Count > 0)
            {
                _logger.Info($"Medved progression active. Unlocked stage(s): {string.Join(", ", unlockedStages.Select(x => x.Name))}.");
            }

            if (legacyQuestCompleted)
            {
                _logger.Info($"Medved legacy progression quest {config.QuestProgression.QuestId} completed. Applying legacy boosted spawn table.");
            }

            var tables = _databaseService.GetTables();
            var locations = _databaseService.GetLocations();
            var tableLocations = tables.Locations.GetDictionary();
            var locationDictionary = locations.GetDictionary();

            foreach (var mapEntry in effectiveSpawnChances)
            {
                var map = mapEntry.Key;
                var chance = config.DebugForceSpawn ? 100 : Math.Clamp(mapEntry.Value, 0, 100);

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

                var zone = GetMedvedBossZones(map, config, spawns, unlockedStages, legacyQuestCompleted);
                if (string.IsNullOrWhiteSpace(zone))
                {
                    _logger.Warning($"{map} has no configured or discoverable BossZone. Spawn may not work.");
                }

                spawns.Add(GenerateMedvedWave(chance, zone));
                _logger.Info($"Added Medved Cell to {map}: chance={chance}, zone={zone}.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error adjusting Medved spawns: {ex.Message}");
            throw;
        }
    }

    private static BossLocationSpawn GenerateMedvedWave(int chance, string zone)
    {
        return new BossLocationSpawn
        {
            BossChance = chance,
            BossDifficulty = "normal",

            // Main escort = Buran
            BossEscortAmount = "1",
            BossEscortDifficulty = "normal",
            BossEscortType = Buran,

            BossName = Sokol,
            IsBossPlayer = false,
            BossZone = zone,

            ForceSpawn = false,
            IgnoreMaxBots = true,
            IsRandomTimeSpawn = false,
            SpawnMode = new[] { "regular", "pve" },

            // Extra support = Kedr only.
            // Do NOT add Buran here or you can get two Burans.
            Supports = new List<BossSupport>
        {
            new BossSupport
            {
                BossEscortAmount = "1",
                BossEscortDifficulty = new ListOrT<string>(new List<string> { "normal" }, null),
                BossEscortType = Kedr
            }
        },

            Time = -1,
            TriggerId = string.Empty,
            TriggerName = string.Empty
        };
    }

    private string GetMedvedBossZones(
        string map,
        MedvedCellConfig config,
        List<BossLocationSpawn> spawns,
        List<MedvedQuestProgressionStage> unlockedStages,
        bool legacyQuestCompleted)
    {
        var configuredZones = GetConfiguredBossZones(map, config, unlockedStages, legacyQuestCompleted);

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

    private static List<string> GetConfiguredBossZones(string map, MedvedCellConfig config, List<MedvedQuestProgressionStage> unlockedStages, bool legacyQuestCompleted)
    {
        var effectiveZones = GetEffectiveSpawnZones(config, unlockedStages, legacyQuestCompleted);

        if (!effectiveZones.TryGetValue(map, out var zoneString) || string.IsNullOrWhiteSpace(zoneString))
        {
            return new List<string>();
        }

        return SplitBossZones(zoneString).ToList();
    }

    private static Dictionary<string, int> GetEffectiveSpawnChances(MedvedCellConfig config, List<MedvedQuestProgressionStage> unlockedStages, bool legacyQuestCompleted)
    {
        var effectiveSpawnChances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (config.SpawnChances != null)
        {
            foreach (var entry in config.SpawnChances)
            {
                effectiveSpawnChances[entry.Key] = entry.Value;
            }
        }

        foreach (var stage in unlockedStages)
        {
            if (stage.SpawnChances == null)
            {
                continue;
            }

            foreach (var entry in stage.SpawnChances)
            {
                effectiveSpawnChances[entry.Key] = entry.Value;
            }
        }

        if (legacyQuestCompleted && config.QuestProgression?.CompletedSpawnChances != null)
        {
            foreach (var entry in config.QuestProgression.CompletedSpawnChances)
            {
                effectiveSpawnChances[entry.Key] = entry.Value;
            }
        }

        return effectiveSpawnChances;
    }

    private static Dictionary<string, string> GetEffectiveSpawnZones(MedvedCellConfig config, List<MedvedQuestProgressionStage> unlockedStages, bool legacyQuestCompleted)
    {
        var effectiveZones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (config.SpawnZones != null)
        {
            foreach (var entry in config.SpawnZones)
            {
                effectiveZones[entry.Key] = entry.Value;
            }
        }

        foreach (var stage in unlockedStages)
        {
            if (stage.SpawnZones == null)
            {
                continue;
            }

            foreach (var entry in stage.SpawnZones)
            {
                effectiveZones[entry.Key] = entry.Value;
            }
        }

        if (legacyQuestCompleted && config.QuestProgression?.CompletedSpawnZones != null)
        {
            foreach (var entry in config.QuestProgression.CompletedSpawnZones)
            {
                effectiveZones[entry.Key] = entry.Value;
            }
        }

        return effectiveZones;
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
            || string.Equals(role, Kedr, StringComparison.OrdinalIgnoreCase);
    }
}
