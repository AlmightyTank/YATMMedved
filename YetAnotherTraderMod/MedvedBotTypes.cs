using System.Collections.Generic;

namespace YATMMedved;

public static class MedvedBotTypes
{
    public const int BossMedvedSokolValue = 660010;
    public const int FollowerMedvedBuranValue = 660011;
    public const int FollowerMedvedKedrValue = 660012;
    public const int FollowerMedvedValue = 660013;

    public const string BossMedvedSokol = "bossMedvedSokol";
    public const string FollowerMedvedBuran = "followerMedvedBuran";
    public const string FollowerMedvedKedr = "followerMedvedKedr";
    public const string FollowerMedved = "followerMedved";

    public static readonly List<string> TypeList = new()
    {
        BossMedvedSokol,
        FollowerMedvedBuran,
        FollowerMedvedKedr,
        FollowerMedved
    };

    public static readonly Dictionary<int, string> TypeDictionary = new()
    {
        { BossMedvedSokolValue, BossMedvedSokol },
        { FollowerMedvedBuranValue, FollowerMedvedBuran },
        { FollowerMedvedKedrValue, FollowerMedvedKedr },
        { FollowerMedvedValue, FollowerMedved }
    };
}
