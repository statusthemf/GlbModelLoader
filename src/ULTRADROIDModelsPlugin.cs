
using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace ULTRADROID.Models
{
    [BepInPlugin(PluginGuid, "ULTRADROID Models API", PluginVersion)]
    public sealed class ULTRADROIDModelsPlugin : BasePlugin
    {
        public const string PluginGuid =
            "com.statusthemf.ultradroid.models";

        public const string PluginVersion = "0.1.4";

        public override void Load()
        {
            Log.LogInfo(
                "[ULTRADROID Models API] Version 0.1.4 loaded."
            );

            Log.LogInfo(
                "[ULTRADROID Models API] GLB texture diagnostics build."
            );
        }
    }
}
