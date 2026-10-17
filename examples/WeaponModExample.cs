
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
    "0.5.1"
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
            Log.LogInfo("[BladesOfChaos] Diagnostic runner started.");
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

    private void CheckScene(bool forceLog)
    {
        Scene scene = SceneManager.GetActiveScene();
        string sceneName = scene.name ?? "";

        bool changed = !string.Equals(
            lastSceneName,
            sceneName,
            StringComparison.Ordinal
        );

        if (!forceLog && !changed)
            return;

        lastSceneName = sceneName;
        nextModelAttempt = -1f;

        if (changed)
            modelLoaded = false;

        if (!IsGameplayScene(sceneName, scene.isLoaded))
            return;

        if (modelLoaded)
            return;

        Log("[BladesOfChaos] Gameplay scene detected: " + sceneName);
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
            "intro",
            "bootstrap",
            "mainmenu",
            "main_menu",
            "menu",
            "loading",
            "loader",
            "credits",
            "options",
            "settings",
            "title"
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

    private bool TryLoadModel()
    {
        Scene scene = SceneManager.GetActiveScene();
        string sceneName = scene.name ?? "";

        if (!IsGameplayScene(sceneName, scene.isLoaded))
            return false;

        Camera camera = Camera.main;

        if (camera == null)
            camera = UnityEngine.Object.FindObjectOfType<Camera>();

        if (camera == null)
        {
            LogWarning("[BladesOfChaos] No camera found.");
            return false;
        }

        string folder = Path.Combine(
            Paths.PluginPath,
            "ULTRADROID.BladesOfChaos"
        );

        string path = Path.Combine(
            folder,
            "blades_of_chaos_god.glb"
        );

        if (!File.Exists(path))
        {
            LogWarning(
                "[BladesOfChaos] GLB file was not found: " + path);
            return false;
        }

        // STEP 1: Decode and report GLB animation data.
        // Animation errors do not prevent the static model from loading.
        try
        {
            Log("[BladesOfChaos] Reading GLB animation data...");

            GlbAnimationData[] animations =
                ModelAPI.ReadGLBAnimations(path);

            if (animations == null || animations.Length == 0)
            {
                LogWarning(
                    "[BladesOfChaos] No animation clips were found. " +
                    "Check whether GlbManualJsonParser parses animations.");
            }
            else
            {
                Log(
                    "[BladesOfChaos] Animation clips found: " +
                    animations.Length);

                for (int i = 0; i < animations.Length; i++)
                {
                    GlbAnimationData animation = animations[i];

                    if (animation == null)
                        continue;

                    int trackCount = animation.Tracks == null
                        ? 0
                        : animation.Tracks.Length;

                    Log(
                        "[BladesOfChaos] Clip " + i +
                        ": name=\"" + animation.Name +
                        "\", duration=" + animation.Duration +
                        " seconds, tracks=" + trackCount);

                    if (animation.Tracks == null)
                        continue;

                    for (int t = 0; t < animation.Tracks.Length; t++)
                    {
                        GlbAnimationTrack track =
                            animation.Tracks[t];

                        if (track == null)
                            continue;

                        int keyCount = track.Times == null
                            ? 0
                            : track.Times.Length;

                        int valueCount = track.Values == null
                            ? 0
                            : track.Values.Length;

                        string firstTime = keyCount > 0
                            ? track.Times[0].ToString()
                            : "none";

                        string lastTime = keyCount > 0
                            ? track.Times[keyCount - 1].ToString()
                            : "none";

                        Log(
                            "[BladesOfChaos]   Track " + t +
                            ": node=" + track.TargetNode +
                            ", path=" + track.Path +
                            ", interpolation=" + track.Interpolation +
                            ", keyframes=" + keyCount +
                            ", values=" + valueCount +
                            ", componentsPerElement=" +
                            track.ComponentsPerElement +
                            ", valuesPerKeyframe=" +
                            track.ValuesPerKeyframe +
                            ", timeRange=" + firstTime +
                            " to " + lastTime);
                    }
                }

                Log(
                    "[BladesOfChaos] Animation data decoding completed.");
            }
        }
        catch (Exception ex)
        {
            LogError(
                "[BladesOfChaos] Animation decoding failed. " +
                "Continuing with static model loading.");

            LogError(ex.ToString());
        }

        // STEP 2: Load the model as before.
        try
        {
            var options = new ModelLoadOptions
            {
                Name = "ULTRADROID_BladesOfChaos",
                Position = new Vector3(0f, 0f, 1.0f),
                RotationEuler = Vector3.zero,
                Scale = new Vector3(0.1f, 0.1f, 0.1f),

                LoadTextures = true,
                CreateColliders = false,
                FlipUvV = true
            };

            GameObject result = ModelAPI.LoadGLB(
                path,
                camera.transform,
                options
            );

            if (result == null)
            {
                LogError("[BladesOfChaos] LoadGLB returned null.");
                return false;
            }

            Log("[BladesOfChaos] Model loaded: " + result.name);
            Log(
                "[BladesOfChaos] Diagnostic pass finished. " +
                "Animation playback has not been implemented yet.");

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
