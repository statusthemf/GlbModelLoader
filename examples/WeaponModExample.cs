
using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.SceneManagement;
using ULTRADROID.Models;

[BepInPlugin(
    "com.statusthemf.ultradroid.bladesofchaos",
    "ULTRADROID Blades of Chaos",
    "0.4.0"
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

        Log.LogInfo("[BLADE DEBUG 0.4.0] Plugin starting.");
        Log.LogInfo("[BLADE DEBUG] Plugin directory: " + Paths.PluginPath);

        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<BladesOfChaosDiagnosticRunner>();
            Log.LogInfo("[BLADE DEBUG] Runner type registered.");

            GameObject runnerObject =
                new GameObject("ULTRADROID_BladesOfChaos_DebugRunner");

            UnityEngine.Object.DontDestroyOnLoad(runnerObject);
            runnerObject.AddComponent<BladesOfChaosDiagnosticRunner>();

            Log.LogInfo("[BLADE DEBUG] Runner component attached.");

            Scene scene = SceneManager.GetActiveScene();

            Log.LogInfo(
                "[BLADE DEBUG] Initial scene: '" +
                scene.name +
                "', buildIndex=" +
                scene.buildIndex +
                ", loaded=" +
                scene.isLoaded
            );
        }
        catch (Exception ex)
        {
            Log.LogError("[BLADE DEBUG] Plugin initialization failed:");
            Log.LogError(ex.ToString());
        }
    }
}

public sealed class BladesOfChaosDiagnosticRunner : MonoBehaviour
{
    private float nextSceneCheck;
    private float nextStatusLog;
    private float loadAtTime = -1f;

    private string lastSceneName = null;
    private bool attemptedInCurrentScene;
    private bool modelLoaded;
    private int updateCount;

    public BladesOfChaosDiagnosticRunner(IntPtr ptr) : base(ptr)
    {
    }

    public BladesOfChaosDiagnosticRunner()
        : base(ClassInjector.DerivedConstructorPointer<BladesOfChaosDiagnosticRunner>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    private void OnEnable()
    {
        Log("[BLADE DEBUG] Runner OnEnable called.");
    }

    private void Start()
    {
        Log("[BLADE DEBUG] Runner Start called.");
        CheckScene(true);
    }

    private void Update()
    {
        updateCount++;

        if (Time.unscaledTime >= nextStatusLog)
        {
            nextStatusLog = Time.unscaledTime + 5f;

            Scene current = SceneManager.GetActiveScene();

            Log(
                "[BLADE DEBUG] Heartbeat. Updates=" +
                updateCount +
                ", scene='" +
                current.name +
                "', buildIndex=" +
                current.buildIndex +
                ", loaded=" +
                current.isLoaded
            );
        }

        if (Time.unscaledTime >= nextSceneCheck)
        {
            nextSceneCheck = Time.unscaledTime + 1f;
            CheckScene(false);
        }

        if (loadAtTime >= 0f &&
            Time.unscaledTime >= loadAtTime &&
            !attemptedInCurrentScene &&
            !modelLoaded)
        {
            loadAtTime = -1f;
            attemptedInCurrentScene = true;

            Log("[BLADE DEBUG] Five-second gameplay delay completed.");
            TryLoadModel();
        }
    }

    private void CheckScene(bool forceLog)
    {
        Scene scene = SceneManager.GetActiveScene();
        string sceneName = scene.name ?? "";

        if (!forceLog &&
            lastSceneName != null &&
            string.Equals(lastSceneName, sceneName, StringComparison.Ordinal))
        {
            return;
        }

        bool changed = !string.Equals(
            lastSceneName,
            sceneName,
            StringComparison.Ordinal
        );

        if (!forceLog && !changed)
        {
            return;
        }

        lastSceneName = sceneName;

        Log(
            "[BLADE DEBUG] Scene detected: '" +
            sceneName +
            "', buildIndex=" +
            scene.buildIndex +
            ", loaded=" +
            scene.isLoaded
        );

        // A different scene means a fresh scene-specific attempt.
        attemptedInCurrentScene = false;
        loadAtTime = -1f;

        if (modelLoaded)
        {
            Log(
                "[BLADE DEBUG] Model already loaded; not loading a duplicate."
            );
            return;
        }

        if (!IsGameplayScene(sceneName, scene.isLoaded))
        {
            Log(
                "[BLADE DEBUG] Skipping scene '" +
                sceneName +
                "': it does not match the gameplay-scene filter."
            );
            return;
        }

        loadAtTime = Time.unscaledTime + 5f;

        Log(
            "[BLADE DEBUG] Gameplay candidate detected. " +
            "Model attempt scheduled in five seconds."
        );
    }

    private bool IsGameplayScene(string sceneName, bool isLoaded)
    {
        if (!isLoaded || string.IsNullOrWhiteSpace(sceneName))
        {
            return false;
        }

        string name = sceneName.ToLowerInvariant();

        // Conservative exclusions for startup, menu and transition scenes.
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
            if (name.Contains(word))
            {
                return false;
            }
        }

        // ULTRAKILL campaign scenes commonly use names such as
        // 0-1, 1-1, 4-2, 7-1, or names containing those level identifiers.
        // We deliberately log the candidate before attempting the load.
        if (sceneName.Length >= 3 &&
            char.IsDigit(sceneName[0]) &&
            sceneName[1] == '-' &&
            char.IsDigit(sceneName[2]))
        {
            return true;
        }

        Log(
            "[BLADE DEBUG] Scene name is not a recognized numeric level: '" +
            sceneName +
            "'. Waiting for another scene."
        );

        return false;
    }

    private void TryLoadModel()
    {
        try
        {
            Scene scene = SceneManager.GetActiveScene();

            Log(
                "[BLADE DEBUG] Beginning model test in scene '" +
                scene.name +
                "'."
            );

            // Verify that the scene has not changed while the timer ran.
            if (!IsGameplayScene(scene.name ?? "", scene.isLoaded))
            {
                Log(
                    "[BLADE DEBUG] Aborting: current scene is no longer " +
                    "recognized as gameplay."
                );
                return;
            }

            Camera camera = Camera.main;

            if (camera == null)
            {
                Log("[BLADE DEBUG] Camera.main is null; searching for a camera.");
                camera = UnityEngine.Object.FindObjectOfType<Camera>();
            }

            if (camera == null)
            {
                LogWarning(
                    "[BLADE DEBUG] No camera found. No model was loaded."
                );
                return;
            }

            Log("[BLADE DEBUG] Camera found: " + camera.name);

            string folder = Path.Combine(
                Paths.PluginPath,
                "ULTRADROID.BladesOfChaos"
            );

            string path = Path.Combine(
                folder,
                "blades_of_chaos_god.glb"
            );

            Log("[BLADE DEBUG] Expected model folder: " + folder);
            Log("[BLADE DEBUG] Expected model path: " + path);

            if (!Directory.Exists(folder))
            {
                LogWarning("[BLADE DEBUG] Model folder does not exist.");
                return;
            }

            foreach (string file in Directory.GetFiles(folder))
            {
                Log(
                    "[BLADE DEBUG] File in model folder: " +
                    Path.GetFileName(file)
                );
            }

            if (!File.Exists(path))
            {
                LogWarning("[BLADE DEBUG] GLB file not found.");
                return;
            }

            FileInfo fileInfo = new FileInfo(path);

            Log("[BLADE DEBUG] GLB size: " + fileInfo.Length + " bytes");

            if (fileInfo.Length < 20)
            {
                LogError("[BLADE DEBUG] GLB file is too small.");
                return;
            }

            using (FileStream stream = File.OpenRead(path))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                uint magic = reader.ReadUInt32();
                uint version = reader.ReadUInt32();
                uint length = reader.ReadUInt32();

                Log(
                    "[BLADE DEBUG] GLB header: magic=0x" +
                    magic.ToString("X8") +
                    ", version=" +
                    version +
                    ", declaredLength=" +
                    length +
                    ", actualLength=" +
                    stream.Length
                );

                if (magic != 0x46546C67 || version != 2)
                {
                    LogError(
                        "[BLADE DEBUG] Invalid GLB header. Expected glTF 2.0."
                    );
                    return;
                }

                if (length < 20 || length > stream.Length)
                {
                    LogError("[BLADE DEBUG] Invalid declared GLB length.");
                    return;
                }
            }

            Log("[BLADE DEBUG] Calling ModelAPI.LoadGLB now.");

            GameObject result = ModelAPI.LoadGLB(
                path,
                camera.transform,
                new ModelLoadOptions
                {
                    Name = "ULTRADROID_BladesOfChaos_DebugModel",
                    Position = new Vector3(0f, 0f, 2f),
                    RotationEuler = Vector3.zero,
                    Scale = new Vector3(0.1f, 0.1f, 0.1f),
                    LoadTextures = true,
                    CreateColliders = false
                }
            );

            if (result == null)
            {
                LogError("[BLADE DEBUG] LoadGLB returned null.");
                return;
            }

            MeshFilter[] meshes =
                result.GetComponentsInChildren<MeshFilter>(true);

            Renderer[] renderers =
                result.GetComponentsInChildren<Renderer>(true);

            Log("[BLADE DEBUG] Model GameObject: " + result.name);
            Log("[BLADE DEBUG] MeshFilter count: " + meshes.Length);
            Log("[BLADE DEBUG] Renderer count: " + renderers.Length);

            foreach (MeshFilter filter in meshes)
            {
                if (filter != null && filter.sharedMesh != null)
                {
                    Log(
                        "[BLADE DEBUG] Mesh '" +
                        filter.sharedMesh.name +
                        "' has " +
                        filter.sharedMesh.vertexCount +
                        " vertices."
                    );
                }
            }

            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                {
                    Log(
                        "[BLADE DEBUG] Renderer '" +
                        renderer.name +
                        "', enabled=" +
                        renderer.enabled
                    );
                }
            }

            modelLoaded = true;
            Log("[BLADE DEBUG] MODEL LOAD CALL SUCCEEDED.");
        }
        catch (Exception ex)
        {
            LogError("[BLADE DEBUG] Model loading threw an exception:");
            LogError(ex.ToString());
        }
    }

    private static void Log(string message)
    {
        if (BladesOfChaosPlugin.DiagLog != null)
        {
            BladesOfChaosPlugin.DiagLog.LogInfo(message);
        }
    }

    private static void LogWarning(string message)
    {
        if (BladesOfChaosPlugin.DiagLog != null)
        {
            BladesOfChaosPlugin.DiagLog.LogWarning(message);
        }
    }

    private static void LogError(string message)
    {
        if (BladesOfChaosPlugin.DiagLog != null)
        {
            BladesOfChaosPlugin.DiagLog.LogError(message);
        }
    }
}
