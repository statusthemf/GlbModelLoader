
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
    "ULTRADROID Model Test",
    "0.6.0"
)]
[BepInDependency(
    "com.statusthemf.ultradroid.models",
    BepInDependency.DependencyFlags.HardDependency
)]
public sealed class BladesOfChaosPlugin : BasePlugin
{
    internal static ManualLogSource DiagLog;

    public override void Load()
    {
        DiagLog = Log;

        try
        {
            AddComponent<BladesOfChaosRunner>();
            Log.LogInfo("[ULTRADROID Model Test] Plugin loaded.");
        }
        catch (Exception ex)
        {
            Log.LogError("[ULTRADROID Model Test] Initialization failed:");
            Log.LogError(ex.ToString());
        }
    }
}

public sealed class BladesOfChaosRunner : MonoBehaviour
{
    private float nextSceneCheck;
    private float nextModelAttempt = -1f;

    private string lastSceneName;
    private bool modelsLoaded;

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
            LogError("[ULTRADROID Model Test] Initial scene check failed:");
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
                LogError("[ULTRADROID Model Test] Scene check failed:");
                LogError(ex.ToString());
            }
        }

        if (nextModelAttempt >= 0f &&
            now >= nextModelAttempt &&
            !modelsLoaded)
        {
            nextModelAttempt = -1f;

            if (TryLoadModels())
            {
                modelsLoaded = true;
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
            StringComparison.Ordinal
        );

        if (!forceCheck && !changed)
            return;

        lastSceneName = sceneName;
        nextModelAttempt = -1f;

        if (changed)
            modelsLoaded = false;

        if (!IsGameplayScene(sceneName, scene.isLoaded))
            return;

        if (modelsLoaded)
            return;

        // Allow the gameplay camera time to initialize.
        nextModelAttempt = Time.unscaledTime + 5f;
    }

    private bool IsGameplayScene(string sceneName, bool isLoaded)
    {
        if (!isLoaded || string.IsNullOrWhiteSpace(sceneName))
            return false;

        if (sceneName.Equals(
            "uk_construct",
            StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

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

        for (int i = 0; i + 2 < sceneName.Length; i++)
        {
            if (char.IsDigit(sceneName[i]) &&
                sceneName[i + 1] == '-' &&
                char.IsDigit(sceneName[i + 2]))
            {
                return true;
            }
        }

        string upper = sceneName.ToUpperInvariant();
        return upper.Contains("P-1") || upper.Contains("P-2");
    }

    private bool TryLoadModels()
    {
        try
        {
            Scene scene = SceneManager.GetActiveScene();

            if (!IsGameplayScene(scene.name ?? "", scene.isLoaded))
                return false;

            Camera camera = Camera.main;

            if (camera == null)
                camera = UnityEngine.Object.FindObjectOfType<Camera>();

            if (camera == null)
            {
                LogWarning("[ULTRADROID Model Test] No camera found.");
                return false;
            }

            string folder = Path.Combine(
                Paths.PluginPath,
                "ULTRADROID.BladesOfChaos"
            );

            string swordPath = Path.Combine(
                folder,
                "blades_of_chaos_god.glb"
            );

            string awpPath = Path.Combine(folder, "awp.glb");

            if (!File.Exists(swordPath))
            {
                LogWarning("[ULTRADROID Model Test] Missing file: " + swordPath);
                return false;
            }

            if (!File.Exists(awpPath))
            {
                LogWarning("[ULTRADROID Model Test] Missing file: " + awpPath);
                return false;
            }

            var swordOptions = new ModelLoadOptions
            {
                Name = "ULTRADROID_BladesOfChaos",
                Position = new Vector3(0.05f, -0.65f, 1.0f),
                RotationEuler = Vector3.zero,
                Scale = new Vector3(0.03f, 0.03f, 0.03f),
                LoadTextures = true,
                CreateColliders = false,
                FlipUvV = true
            };

            var awpOptions = new ModelLoadOptions
            {
                Name = "ULTRADROID_AWP",
                Position = new Vector3(0.35f, -0.4f, 1.2f),
                RotationEuler = Vector3.zero,
                Scale = new Vector3(0.03f, 0.03f, 0.03f),
                LoadTextures = true,
                CreateColliders = false,
                FlipUvV = false
            };

            GameObject sword = ModelAPI.LoadGLB(
                swordPath,
                camera.transform,
                swordOptions
            );

            if (sword == null)
            {
                LogError("[ULTRADROID Model Test] Sword model failed to load.");
                return false;
            }

            GameObject awp = ModelAPI.LoadGLB(
                awpPath,
                camera.transform,
                awpOptions
            );

            if (awp == null)
            {
                // Avoid leaving a partially loaded test model behind.
                UnityEngine.Object.Destroy(sword);
                LogError("[ULTRADROID Model Test] AWP model failed to load.");
                return false;
            }

            Log("[ULTRADROID Model Test] Both models loaded successfully.");
            return true;
        }
        catch (Exception ex)
        {
            LogError("[ULTRADROID Model Test] Model loading failed:");
            LogError(ex.ToString());
            return false;
        }
    }

    private static void Log(string message)
    {
        if (BladesOfChaosPlugin.DiagLog != null)
            BladesOfChaosPlugin.DiagLog.LogInfo(message);
    }

    private static void LogWarning(string message)
    {
        if (BladesOfChaosPlugin.DiagLog != null)
            BladesOfChaosPlugin.DiagLog.LogWarning(message);
    }

    private static void LogError(string message)
    {
        if (BladesOfChaosPlugin.DiagLog != null)
            BladesOfChaosPlugin.DiagLog.LogError(message);
    }
}
