
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
    "0.4.3"
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

        Log.LogInfo("[BLADE DEBUG 0.4.3] Plugin starting.");
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
                "[BLADE DEBUG] Initial scene='" +
                scene.name +
                "', loaded=" +
                scene.isLoaded
            );

            Log.LogInfo("[BLADE DEBUG] Plugin initialization completed.");
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
    private float nextModelAttempt = -1f;

    private string lastSceneName;
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

        try
        {
            CheckScene(true);
        }
        catch (Exception ex)
        {
            LogError("[BLADE DEBUG] Initial scene check failed:");
            LogError(ex.ToString());
        }
    }

    private void Update()
    {
        updateCount++;
        float now = Time.unscaledTime;

        if (now >= nextStatusLog)
        {
            nextStatusLog = now + 5f;

            try
            {
                Scene scene = SceneManager.GetActiveScene();

                Log(
                    "[BLADE DEBUG] HEARTBEAT. Updates=" +
                    updateCount +
                    ", scene='" +
                    scene.name +
                    "', loaded=" +
                    scene.isLoaded
                );
            }
            catch (Exception ex)
            {
                LogError("[BLADE DEBUG] Heartbeat failed:");
                LogError(ex.ToString());
            }
        }

        if (now >= nextSceneCheck)
        {
            nextSceneCheck = now + 1f;

            try
            {
                CheckScene(false);
            }
            catch (Exception ex)
            {
                LogError("[BLADE DEBUG] Scene polling failed:");
                LogError(ex.ToString());
            }
        }

        if (nextModelAttempt >= 0f &&
            now >= nextModelAttempt &&
            !modelLoaded)
        {
            nextModelAttempt = -1f;

            Log("[BLADE DEBUG] Five-second delay completed.");

            bool success = TryLoadModel();

            if (success)
            {
                modelLoaded = true;
                Log("[BLADE DEBUG] Model load returned a GameObject.");
            }
            else
            {
                nextModelAttempt = Time.unscaledTime + 5f;

                Log(
                    "[BLADE DEBUG] Attempt failed. " +
                    "Retrying in five seconds if gameplay remains active."
                );
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

        Log(
            "[BLADE DEBUG] Scene detected: '" +
            sceneName +
            "', loaded=" +
            scene.isLoaded
        );

        if (!IsGameplayScene(sceneName, scene.isLoaded))
        {
            Log(
                "[BLADE DEBUG] Skipping scene '" +
                sceneName +
                "': not recognized as gameplay."
            );
            return;
        }

        if (modelLoaded)
        {
            Log("[BLADE DEBUG] Model already loaded; skipping duplicate.");
            return;
        }

        nextModelAttempt = Time.unscaledTime + 5f;

        Log(
            "[BLADE DEBUG] Gameplay candidate detected: '" +
            sceneName +
            "'. Model attempt scheduled in five seconds."
        );
    }

    private bool IsGameplayScene(string sceneName, bool isLoaded)
    {
        if (!isLoaded || string.IsNullOrWhiteSpace(sceneName))
            return false;

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

        // Recognize numeric level identifiers such as 0-1, 1-1, 7-1.
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

        if (upper.Contains("P-1") || upper.Contains("P-2"))
            return true;

        Log(
            "[BLADE DEBUG] Unrecognized scene name: '" +
            sceneName +
            "'."
        );

        return false;
    }

    private bool TryLoadModel()
    {
        try
        {
            Scene scene = SceneManager.GetActiveScene();
            string sceneName = scene.name ?? "";

            Log(
                "[BLADE DEBUG] Starting model attempt in scene '" +
                sceneName +
                "'."
            );

            if (!IsGameplayScene(sceneName, scene.isLoaded))
            {
                Log("[BLADE DEBUG] Cancelled: not a recognized gameplay scene.");
                return false;
            }

            Camera camera = Camera.main;

            if (camera == null)
            {
                Log("[BLADE DEBUG] Camera.main is null; searching for camera.");
                camera = UnityEngine.Object.FindObjectOfType<Camera>();
            }

            if (camera == null)
            {
                LogWarning("[BLADE DEBUG] No camera found.");
                return false;
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

            Log("[BLADE DEBUG] Model folder: " + folder);
            Log("[BLADE DEBUG] Model path: " + path);

            if (!Directory.Exists(folder))
            {
                LogWarning("[BLADE DEBUG] Model folder does not exist.");
                return false;
            }

            string[] files = Directory.GetFiles(folder);

            Log("[BLADE DEBUG] Files in model folder: " + files.Length);

            foreach (string file in files)
            {
                Log("[BLADE DEBUG] Found file: " + Path.GetFileName(file));
            }

            if (!File.Exists(path))
            {
                LogWarning("[BLADE DEBUG] GLB file was not found.");
                return false;
            }

            FileInfo info = new FileInfo(path);

            Log("[BLADE DEBUG] GLB size: " + info.Length + " bytes");

            if (info.Length < 20)
            {
                LogError("[BLADE DEBUG] GLB file is too small.");
                return false;
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

                if (magic != 0x46546C67)
                {
                    LogError("[BLADE DEBUG] Invalid GLB magic.");
                    return false;
                }

                if (version != 2)
                {
                    LogError("[BLADE DEBUG] Expected GLB version 2.");
                    return false;
                }

                if (length < 20 || length > stream.Length)
                {
                    LogError("[BLADE DEBUG] Invalid GLB declared length.");
                    return false;
                }
            }

            Log("[BLADE DEBUG] Calling ModelAPI.LoadGLB...");

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
                return false;
            }

            MeshFilter[] meshes =
                result.GetComponentsInChildren<MeshFilter>(true);

            Renderer[] renderers =
                result.GetComponentsInChildren<Renderer>(true);

            Log("[BLADE DEBUG] Returned GameObject: " + result.name);
            Log("[BLADE DEBUG] MeshFilter count: " + meshes.Length);
            Log("[BLADE DEBUG] Renderer count: " + renderers.Length);

            foreach (MeshFilter filter in meshes)
            {
                if (filter != null && filter.sharedMesh != null)
                {
                    Log(
                        "[BLADE DEBUG] Mesh: " +
                        filter.sharedMesh.name +
                        ", vertices=" +
                        filter.sharedMesh.vertexCount
                    );
                }
            }

            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                {
                    Log(
                        "[BLADE DEBUG] Renderer: " +
                        renderer.name +
                        ", enabled=" +
                        renderer.enabled
                    );
                }
            }

            Log("[BLADE DEBUG] MODEL LOAD CALL COMPLETED.");
            return true;
        }
        catch (Exception ex)
        {
            LogError("[BLADE DEBUG] Model loading/check failed:");
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
