using EFT;

namespace YetAnotherTraderMod.Client;

public static class WildSpawnTypeExtensions
{
    public const int BossMedvedSokolValue = 660010;
    public const int FollowerMedvedBuranValue = 660011;
    public const int FollowerMedvedKedrValue = 660012;
    public const int FollowerMedvedValue = 660013;

    public static bool IsMedved(this WildSpawnType role)
    {
        var value = (int)role;
        return value == BossMedvedSokolValue
            || value == FollowerMedvedBuranValue
            || value == FollowerMedvedKedrValue
            || value == FollowerMedvedValue;
    }
}
