// Example only: this belongs in a separate weapon-mod project that references
// ULTRADROID.Models.dll and declares a hard dependency on the Models API plugin.
// Call from a valid Unity/gameplay callback after Unity has initialized.

using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using ULTRADROID.Models;
using UnityEngine;

[BepInPlugin("com.statusthemf.ultradroid.bladesofchaos", "ULTRADROID Blades of Chaos", "0.1.0")]
[BepInDependency(ULTRADROIDModelsPlugin.PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
public sealed class BladesOfChaosExample : BasePlugin
{
    public override void Load()
    {
        // Do not load a model directly inside BasePlugin.Load if the target build
        // has not initialized its Unity scene yet. Schedule this from a runtime
        // MonoBehaviour or a scene-loaded callback in the actual weapon mod.
        Log.LogInfo("Example weapon plugin loaded; model spawning must be deferred to gameplay.");
    }

    // Call this from the weapon mod's safe Unity runtime callback:
    private GameObject SpawnModel(Transform weaponParent, string modelPath)
    {
        return ModelAPI.LoadGLB(modelPath, weaponParent, new ModelLoadOptions
        {
            Name = "BladesOfChaos_Model",
            Position = Vector3.zero,
            RotationEuler = Vector3.zero,
            Scale = Vector3.one,
            LoadTextures = true,
            CreateColliders = false
        });
    }
}
