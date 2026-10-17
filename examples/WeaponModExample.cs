
using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using ULTRADROID.Models;

[BepInPlugin("com.statusthemf.weaponmodexample", "Weapon Mod Example", "1.0.0")]
[BepInDependency("com.statusthemf.ultradroid.models", BepInDependency.DependencyFlags.HardDependency)]
public sealed class WeaponModExample : BasePlugin
{
    public override void Load()
    {
        Log.LogInfo("[WeaponModExample] Loaded.");
        AddComponent<WeaponModelRunner>();
    }
}

public sealed class WeaponModelRunner : MonoBehaviour
{
    private const float RetryInterval = 5f;

    private sealed class WeaponConfig
    {
        public string TargetName;
        public Vector3 LocalPosition;
        public Vector3 LocalScale;
        public bool FlipUV;
        public bool Loaded;
        public float NextAttemptTime;

        public WeaponConfig(string targetName, Vector3 pos, Vector3 scale, bool flipUV)
        {
            TargetName = targetName;
            LocalPosition = pos;
            LocalScale = scale;
            FlipUV = flipUV;
        }
    }

    private readonly WeaponConfig[] _configs =
    {
        new WeaponConfig(
            "Knife(Clone)",
            new Vector3(0f, -0.65f, 1f),
            new Vector3(0.03f, 0.03f, 0.03f),
            true),

        new WeaponConfig(
            "AWP(Clone)",
            new Vector3(-0.35f, -0.4f, 1.2f),
            new Vector3(0.005f, 0.005f, 0.005f),
            false),

        // Add more weapon configurations here.
    };

    public WeaponModelRunner(IntPtr ptr) : base(ptr)
    {
    }

    private void Update()
    {
        for (int i = 0; i < _configs.Length; i++)
        {
            WeaponConfig cfg = _configs[i];

            if (cfg.Loaded)
                continue;

            if (Time.unscaledTime < cfg.NextAttemptTime)
                continue;

            GameObject weapon = FindWeapon(cfg.TargetName);

            // Keep checking until the target weapon exists.
            if (weapon == null)
                continue;

            cfg.NextAttemptTime = Time.unscaledTime + RetryInterval;

            try
            {
                bool ok = LoadModelIntoWeapon(
                    weapon,
                    ResolveModelPath(cfg.TargetName),
                    cfg.LocalPosition,
                    cfg.LocalScale,
                    cfg.FlipUV);

                if (ok)
                {
                    cfg.Loaded = true;
                    Debug.Log(
                        "[ULTRADROID] Model attached: " + cfg.TargetName);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "[ULTRADROID] Model load failed for " +
                    cfg.TargetName + ": " + ex);
            }
        }
    }

    private static GameObject FindWeapon(string targetName)
    {
        GameObject[] all =
            Resources.FindObjectsOfTypeAll<GameObject>();

        for (int i = 0; i < all.Length; i++)
        {
            GameObject obj = all[i];

            if (obj != null &&
                obj.activeInHierarchy &&
                obj.name == targetName)
            {
                return obj;
            }
        }

        return null;
    }

    private static string ResolveModelPath(string targetName)
    {
        string fileName;

        if (targetName == "Knife(Clone)")
            fileName = "blades_of_chaos_god.glb";
        else if (targetName == "AWP(Clone)")
            fileName = "awp.glb";
        else
            throw new ArgumentException(
                "No model path configured for " + targetName);

        return System.IO.Path.Combine(
            Paths.PluginPath,
            "ULTRADROID.BladesOfChaos",
            fileName);
    }

    // PLACEHOLDER: connect this method to the actual Models API.
    private static bool LoadModelIntoWeapon(
        GameObject weapon,
        string modelPath,
        Vector3 localPosition,
        Vector3 localScale,
        bool flipUV)
    {
        // The exact ModelAPI.LoadGLB call depends on your API's
        // ModelLoadOptions and how it attaches models to a parent.
        // Returning false means this example does not load anything yet.
        return false;
    }
}
