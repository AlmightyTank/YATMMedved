using BepInEx.Logging;
using Comfort.Common;
using EFT;
using MoreBotsAPI.Components;
using System.Collections.Generic;

namespace YetAnotherTraderMod.Client.Services;

public static class MedvedClientRuntime
{
    private static readonly List<WildSpawnType> MedvedRoles = new List<int>
    {
        WildSpawnTypeExtensions.BossMedvedSokolValue,
        WildSpawnTypeExtensions.FollowerMedvedBuranValue,
        WildSpawnTypeExtensions.FollowerMedvedKedrValue
    }.ConvertAll(x => (WildSpawnType)x);

    public static void Init(ManualLogSource logger)
    {
        var huntManager = MonoBehaviourSingleton<HuntManager>.Instance;
        if (huntManager == null)
        {
            logger.LogWarning("[YATM Medved] HuntManager was not available.");
            return;
        }

        huntManager.AddHuntRoles(
            MedvedRoles,
            new List<WildSpawnType>
            {
                WildSpawnType.bossBully,
                WildSpawnType.bossKilla,
                WildSpawnType.bossGluhar,
                WildSpawnType.bossSanitar,
                WildSpawnType.bossKnight,
                WildSpawnType.followerBigPipe,
                WildSpawnType.followerBirdEye,
                WildSpawnType.sectantPriest
            });

        huntManager.AddHuntSides(
            MedvedRoles,
            new List<EPlayerSide>
            {
                EPlayerSide.Usec,
                EPlayerSide.Bear
            });

        huntManager.OnBotHuntInit += manager =>
        {
            if (manager?.botOwner?.Profile?.Info?.Settings == null)
            {
                return;
            }

            if (MedvedRoles.Contains(manager.botOwner.Profile.Info.Settings.Role))
            {
                logger.LogInfo($"[YATM Medved] Hunt behavior initialized for {manager.botOwner.Profile.Info.Nickname}");
            }
        };

        logger.LogInfo("[YATM Medved] Runtime hooks loaded.");
    }
}
