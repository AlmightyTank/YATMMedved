using BepInEx;
using BepInEx.Logging;
using YetAnotherTraderMod.Client.Services;

namespace YetAnotherTraderMod.Client;

[BepInDependency("xyz.drakia.bigbrain", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("me.sol.sain", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("com.morebotsapi.tacticaltoaster", BepInDependency.DependencyFlags.HardDependency)]
[BepInPlugin(ClientInfo.GUID, ClientInfo.PluginName, ClientInfo.Version)]
public class Plugin : BaseUnityPlugin
{
    public static ManualLogSource LogSource = null!;

    private void Awake()
    {
        LogSource = Logger;
        MedvedClientRuntime.Init(LogSource);
        LogSource.LogInfo("[YATM Medved] Client plugin loaded.");
    }
}
