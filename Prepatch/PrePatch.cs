using BepInEx;
using YetAnotherTraderMod;

namespace Prepatch;

[BepInDependency("com.morebotsapiprepatch.tacticaltoaster", BepInDependency.DependencyFlags.HardDependency)]
[BepInPlugin(ClientInfo.PreLoadGUID, ClientInfo.PreLoadName, ClientInfo.Version)]
public class YATMMedvedPrePatch : BaseUnityPlugin
{
    public static YATMMedvedPrePatch Instance = null!;

    public void Awake()
    {
        Instance = this;
    }
}
