
using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.SceneManagement;
using ULTRADROID.Models;

[BepInPlugin(
    "com.statusthemf.ultradroid.bladesofchaos",
    "ULTRADROID Blades of Chaos",
    "0.5.0")]
[BepInDependency(
    "com.statusthemf.ultradroid.models",
    BepInDependency.DependencyFlags.HardDependency)]
public sealed class BladesOfChaosPlugin : BasePlugin
{
    internal static ManualLogSource DiagLog;

    public override void Load()
    {
        DiagLog = Log;

        try
        {
            AddComponent<BladesOfChaosRunner>();
            Log.LogInfo("[BladesOfChaos] Plugin loaded.");
        }
        catch (Exception ex)
        {
            Log.LogError("[BladesOfChaos] Plugin initialization failed:");
            Log.LogError(ex.ToString());
        }
    }
}

public sealed class BladesOfChaosRunner : MonoBehaviour
{
    private float nextSceneCheck;
    private float nextModelAttempt = -1f;
    private string lastSceneName;
    private bool modelLoaded;

    public BladesOfChaosRunner(IntPtr ptr) : base(ptr)
    {
    }

    public void Start()
    {
        try
        {
            CheckScene(true);
        }
        catch (Exception ex)
        {
            LogError("[BladesOfChaos] Initial scene check failed:");
            LogError(ex.ToString());
        }
    }

    public void Update()
    {
        float now = Time.unscaledTime;

        if (now >= nextSceneCheck)
        {
            nextSceneCheck = now + 1f;

            try
            {
                CheckScene(false);
            }
            catch (Exception ex)
            {
                LogError("[BladesOfChaos] Scene polling failed:");
                LogError(ex.ToString());
            }
        }

        if (nextModelAttempt >= 0f &&
            now >= nextModelAttempt &&
            !modelLoaded)
        {
            nextModelAttempt = -1f;

            if (TryLoadModel())
            {
                modelLoaded = true;
            }
            else
            {
                nextModelAttempt = Time.unscaledTime + 5f;
            }
        }
    }

    private void CheckScene(bool forceCheck)
    {
        Scene scene = SceneManager.GetActiveScene();
        string sceneName = scene.name ?? "";

        bool changed = !string.Equals(
            lastSceneName,
            sceneName,
            StringComparison.Ordinal);

        if (!forceCheck && !changed)
            return;

        lastSceneName = sceneName;
        nextModelAttempt = -1f;

        if (changed)
            modelLoaded = false;

        if (!IsGameplayScene(sceneName, scene.isLoaded))
            return;

        if (modelLoaded)
            return;

        nextModelAttempt = Time.unscaledTime + 5f;
    }

    private bool IsGameplayScene(string sceneName, bool isLoaded)
    {
        if (!isLoaded || string.IsNullOrEmpty(sceneName))
            return false;

        string lower = sceneName.ToLowerInvariant();

        string[] excluded =
        {
            "intro", "bootstrap", "mainmenu", "main_menu",
            "menu", "loading", "loader", "credits",
            "options", "settings", "title"
        };

        foreach (string word in excluded)
        {
            if (lower.Contains(word))
                return false;
        }

        if (lower.Contains("uk_construct"))
            return true;

        string upper = sceneName.ToUpperInvariant();

        if (upper.Contains("P-1") || upper.Contains("P-2"))
            return true;

        for (int i = 0; i + 2 < upper.Length; i++)
        {
            if (char.IsDigit(upper[i]) &&
                upper[i + 1] == '-' &&
                char.IsDigit(upper[i + 2]))
                return true;
        }

        return false;
    }

    private bool TryLoadModel()
    {
        try
        {
            Scene scene = SceneManager.GetActiveScene();

            if (!IsGameplayScene(scene.name ?? "", scene.isLoaded))
                return false;

            Camera camera = Camera.main;

            if (camera == null)
            {
                LogWarning("[BladesOfChaos] No camera found.");
                return false;
            }

            string folder = Path.Combine(
                Paths.PluginPath,
                "ULTRADROID.BladesOfChaos");

            string path = Path.Combine(
                folder,
                "blades_of_chaos_god.glb");

            if (!File.Exists(path))
            {
                LogWarning("[BladesOfChaos] GLB file was not found: " + path);
                return false;
            }

            ModelLoadOptions options = new ModelLoadOptions
            {
                Name = "ULTRADROID_BladesOfChaos",
                Position = new Vector3(0.05f, -0.65f, 1f),
                RotationEuler = Vector3.zero,
                Scale = new Vector3(0.03f, 0.03f, 0.03f),
                LoadTextures = true,
                CreateColliders = false
            };

            GameObject result = ModelAPI.LoadGLB(
                path,
                camera.transform,
                options);

            if (result == null)
            {
                LogError("[BladesOfChaos] LoadGLB returned null.");
                return false;
            }

            Log("[BladesOfChaos] Model loaded: " + result.name);
            return true;
        }
        catch (Exception ex)
        {
            LogError("[BladesOfChaos] Model loading failed:");
            LogError(ex.ToString());
            return false;
        }
    }

    private static void Log(string message)
    {
        if (DiagLog != null)
            DiagLog.LogInfo(message);
    }

    private static void LogWarning(string message)
    {
        if (DiagLog != null)
            DiagLog.LogWarning(message);
    }

    private static void LogError(string message)
    {
        if (DiagLog != null)
            DiagLog.LogError(message);
    }
}
