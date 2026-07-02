using Mono.Cecil;
using MoreBotsAPI;
using System.Collections.Generic;

namespace Prepatch.Prepatches;

public static class CustomTypesPatch
{
    public const int BossMedvedSokolValue = 660010;
    public const int FollowerMedvedBuranValue = 660011;
    public const int FollowerMedvedKedrValue = 660012;

    private const int VanillaKnightValue = 25;
    private const int VanillaBigPipeValue = 26;
    private const int VanillaBirdEyeValue = 27;

    public static IEnumerable<string> TargetDLLs { get; } = new[] { "Assembly-CSharp.dll" };

    public static void Patch(ref AssemblyDefinition assembly)
    {
        RegisterSokol(assembly);
        RegisterBuran(assembly);
        RegisterKedr(assembly);

        CustomWildSpawnTypeManager.AddSuitableGroup(new List<int>
        {
            BossMedvedSokolValue,
            FollowerMedvedBuranValue,
            FollowerMedvedKedrValue
        });

        CustomWildSpawnTypeManager.AddSuitableGroup(new List<int>
        {
            BossMedvedSokolValue,
            FollowerMedvedBuranValue
        });

        CustomWildSpawnTypeManager.AddSuitableGroup(new List<int>
        {
            BossMedvedSokolValue,
            FollowerMedvedKedrValue
        });
    }

    private static void RegisterSokol(AssemblyDefinition assembly)
    {
        var bot = new CustomWildSpawnType(
            BossMedvedSokolValue,
            "bossMedvedSokol",
            "Medved",
            VanillaKnightValue,
            true,
            true,
            false);

        bot.SetCountAsBossForStatistics(true);
        bot.SetShouldUseFenceNoBossAttack(false, false);
        bot.SetExcludedDifficulties(new List<int> { 0, 2, 3 });

        bot.SetSAINSettings(new SAINSettings(bot.WildSpawnTypeValue)
        {
            Name = "Medved Sokol",
            Description = "Leader of the Medved Cell. Uses Knight/Goon boss brain behavior.",
            Section = "Medved",
            BaseBrain = "Knight",
            BrainsToApply = new List<string> { "Knight" },
            LayersToRemove = new List<string>(),
            DifficultyModifier = 0.75f
        });

        CustomWildSpawnTypeManager.RegisterWildSpawnType(bot, assembly);
    }

    private static void RegisterBuran(AssemblyDefinition assembly)
    {
        var bot = new CustomWildSpawnType(
            FollowerMedvedBuranValue,
            "followerMedvedBuran",
            "Medved",
            VanillaBigPipeValue,
            true,
            true,
            false);

        bot.SetCountAsBossForStatistics(false);
        bot.SetShouldUseFenceNoBossAttack(false, false);
        bot.SetExcludedDifficulties(new List<int> { 0, 2, 3 });

        bot.SetSAINSettings(new SAINSettings(bot.WildSpawnTypeValue)
        {
            Name = "Medved Buran",
            Description = "Medved Cell heavy gunner. Uses Big Pipe/Goon follower brain behavior.",
            Section = "Medved",
            BaseBrain = "BigPipe",
            BrainsToApply = new List<string> { "BigPipe" },
            LayersToRemove = new List<string>(),
            DifficultyModifier = 0.65f
        });

        CustomWildSpawnTypeManager.RegisterWildSpawnType(bot, assembly);
    }

    private static void RegisterKedr(AssemblyDefinition assembly)
    {
        var bot = new CustomWildSpawnType(
            FollowerMedvedKedrValue,
            "followerMedvedKedr",
            "Medved",
            VanillaBirdEyeValue,
            true,
            true,
            false);

        bot.SetCountAsBossForStatistics(false);
        bot.SetShouldUseFenceNoBossAttack(false, false);
        bot.SetExcludedDifficulties(new List<int> { 0, 2, 3 });

        bot.SetSAINSettings(new SAINSettings(bot.WildSpawnTypeValue)
        {
            Name = "Medved Kedr",
            Description = "Medved Cell marksman. Uses BirdEye/Goon follower brain behavior.",
            Section = "Medved",
            BaseBrain = "BirdEye",
            BrainsToApply = new List<string> { "BirdEye" },
            LayersToRemove = new List<string>(),
            DifficultyModifier = 0.65f
        });

        CustomWildSpawnTypeManager.RegisterWildSpawnType(bot, assembly);
    }
}
