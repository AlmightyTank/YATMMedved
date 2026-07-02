using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using YATMMedved.Models;

namespace YATMMedved.Services;

[Injectable(InjectionType.Singleton)]
public class MedvedQuestStateService(ProfileHelper profileHelper, YATMLogger logger)
{
    private readonly ProfileHelper _profileHelper = profileHelper;
    private readonly YATMLogger _logger = logger;

    public List<MedvedQuestProgressionStage> GetUnlockedStages(string? sessionId, MedvedQuestProgressionConfig? progressionConfig)
    {
        var unlockedStages = new List<MedvedQuestProgressionStage>();

        if (progressionConfig == null || !progressionConfig.Enabled || string.IsNullOrWhiteSpace(sessionId))
        {
            return unlockedStages;
        }

        var completedQuestIds = GetCompletedQuestIds(sessionId);
        if (completedQuestIds.Count == 0)
        {
            return unlockedStages;
        }

        foreach (var stage in progressionConfig.Stages ?? new List<MedvedQuestProgressionStage>())
        {
            if (IsStageUnlocked(stage, completedQuestIds))
            {
                unlockedStages.Add(stage);
            }
        }

        return unlockedStages;
    }

    public bool IsLegacyProgressionQuestCompleted(string? sessionId, MedvedQuestProgressionConfig? progressionConfig)
    {
        if (progressionConfig == null || !progressionConfig.Enabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(progressionConfig.QuestId) || string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        return GetCompletedQuestIds(sessionId).Contains(progressionConfig.QuestId);
    }

    private HashSet<string> GetCompletedQuestIds(string sessionId)
    {
        var completedQuestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var pmcProfile = _profileHelper.GetPmcProfile(sessionId);
            if (pmcProfile == null)
            {
                return completedQuestIds;
            }

            var quests = GetPropertyValue(pmcProfile, "Quests", "quests");
            if (quests is not IEnumerable questEnumerable)
            {
                return completedQuestIds;
            }

            foreach (var quest in questEnumerable)
            {
                var questId = Convert.ToString(GetPropertyValue(quest, "Qid", "qid", "QId", "Id", "id"));
                if (string.IsNullOrWhiteSpace(questId))
                {
                    continue;
                }

                if (IsQuestStatusComplete(quest))
                {
                    completedQuestIds.Add(questId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not read quest progression for Medved Cell: {ex.Message}");
        }

        return completedQuestIds;
    }

    private static bool IsStageUnlocked(MedvedQuestProgressionStage stage, HashSet<string> completedQuestIds)
    {
        var requiredQuestIds = (stage.RequiredQuestIds ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (requiredQuestIds.Count == 0)
        {
            return false;
        }

        return stage.RequireAllQuestIds
            ? requiredQuestIds.All(completedQuestIds.Contains)
            : requiredQuestIds.Any(completedQuestIds.Contains);
    }

    private static bool IsQuestStatusComplete(object quest)
    {
        var status = Convert.ToString(GetPropertyValue(quest, "Status", "status")) ?? string.Empty;
        var statusTimers = Convert.ToString(GetPropertyValue(quest, "StatusTimers", "statusTimers")) ?? string.Empty;

        // SPT profiles usually store completed quests as Success. The extra checks make
        // this tolerant if the enum/string name changes casing or serializes differently.
        return status.Contains("Success", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Completed", StringComparison.OrdinalIgnoreCase)
            || statusTimers.Contains("Success", StringComparison.OrdinalIgnoreCase)
            || statusTimers.Contains("Completed", StringComparison.OrdinalIgnoreCase);
    }

    private static object? GetPropertyValue(object? source, params string[] names)
    {
        if (source == null)
        {
            return null;
        }

        var type = source.GetType();
        foreach (var name in names)
        {
            var property = type.GetProperty(name);
            if (property != null)
            {
                return property.GetValue(source);
            }

            var field = type.GetField(name);
            if (field != null)
            {
                return field.GetValue(source);
            }
        }

        return null;
    }
}
