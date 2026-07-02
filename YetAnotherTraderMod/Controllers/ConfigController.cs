using SPTarkov.DI.Annotations;
using YATMMedved.Models;

namespace YATMMedved.Controllers;

[Injectable]
public class ConfigController
{
    public MainConfig ModConfig => YATMModPreload.ModConfig;
}
