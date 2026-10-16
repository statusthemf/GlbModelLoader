using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace ULTRADROID.Models
{
    /// <summary>
    /// Loads the Models API assembly as a BepInEx plugin. Model loading is requested
    /// by consumer mods through ModelAPI.LoadGLB; this plugin does not spawn a model
    /// automatically during startup.
    /// </summary>
    [BepInPlugin(PluginGuid, "ULTRADROID Models API", PluginVersion)]
    public sealed class ULTRADROIDModelsPlugin : BasePlugin
    {
        public const string PluginGuid = "com.statusthemf.ultradroid.models";
        public const string PluginVersion = "0.1.0-prototype";

        public override void Load()
        {
            Log.LogInfo("[ULTRADROID Models API] Loaded. GLB loading is available to dependent mods.");
            Log.LogWarning("[ULTRADROID Models API] Prototype: compile against the exact target-game DLLs and test in-game before release.");
        }
    }
}
