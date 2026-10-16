
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
    "0.3.1"
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

        Log.LogInfo("[BLADE DIAG 0.3.1] Plugin starting.");
        Log.LogInfo("[BLADE DIAG] Plugin path: " + Paths.PluginPath);

        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<BladesOfChaosDiagnosticRunner>();
            Log.LogInfo("[BLADE DIAG] Diagnostic runner type registered.");

            GameObject runnerObject =
                new GameObject("ULTRADROID_BladesOfChaos_DiagnosticRunner");

            UnityEngine.Object.DontDestroyOnLoad(runnerObject);

            runnerObject.AddComponent<BladesOfChaosDiagnosticRunner>();

            Log.LogInfo("[BLADE DIAG] Runner created.");

            Scene current = SceneManager.GetActiveScene();

            Log.LogInfo(
                "[BLADE DIAG] Startup scene: " +
                current.name +
                ", loaded=" +
                current.isLoaded
            );
        }
        catch (Exception ex)
        {
            Log.LogError("[BLADE DIAG] Failed to create runner:");
            Log.LogError(ex.ToString());
        }
    }
}

public sealed class BladesOfChaosDiagnosticRunner : MonoBehaviour
{
    private float nextLog;
    private float nextAttempt;
    private bool spawned;
    private int updateCount;

    public BladesOfChaosDiagnosticRunner(IntPtr ptr) : base(ptr)
    {
    }

    public BladesOfChaosDiagnosticRunner()
        : base(ClassInjector.DerivedConstructorPointer<BladesOfChaosDiagnosticRunner>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    private void Update()
    {
        updateCount++;

        if (Time.unscaledTime >= nextLog)
        {
            nextLog = Time.unscaledTime + 5f;

            Scene scene = SceneManager.GetActiveScene();
            Camera camera = Camera.main;

            if (camera == null)
            {
                camera = UnityEngine.Object.FindObjectOfType<Camera>();
            }

            BladesOfChaosPlugin.DiagLog.LogInfo(
                "[BLADE DIAG] UPDATE WORKS. Count=" +
                updateCount +
                ", scene=" +
                scene.name +
                ", loaded=" +
                scene.isLoaded +
                ", camera=" +
                (camera != null ? camera.name : "NULL")
            );
        }

        if (!spawned && Time.unscaledTime >= nextAttempt)
        {
            nextAttempt = Time.unscaledTime + 5f;
            TrySpawn();
        }
    }

    private void TrySpawn()
    {
        try
        {
            Scene scene = SceneManager.GetActiveScene();
            string sceneName = scene.name ?? "";

            BladesOfChaosPlugin.DiagLog.LogInfo(
                "[BLADE DIAG] Checking scene: " + sceneName
            );

            if (!scene.isLoaded ||
                sceneName.IndexOf("bootstrap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sceneName.IndexOf("intro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sceneName.IndexOf("menu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sceneName.IndexOf("loading", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                BladesOfChaosPlugin.DiagLog.LogInfo(
                    "[BLADE DIAG] Waiting for a playable scene."
                );
                return;
            }

            Camera camera = Camera.main;

            if (camera == null)
            {
                camera = UnityEngine.Object.FindObjectOfType<Camera>();
            }

            if (camera == null)
            {
                BladesOfChaosPlugin.DiagLog.LogWarning(
                    "[BLADE DIAG] No camera found yet."
                );
                return;
            }

            string modelFolder = Path.Combine(
                Paths.PluginPath,
                "ULTRADROID.BladesOfChaos"
            );

            string modelPath = Path.Combine(
                modelFolder,
                "blades_of_chaos_god.glb"
            );

            BladesOfChaosPlugin.DiagLog.LogInfo(
                "[BLADE DIAG] Model folder: " + modelFolder
            );

            BladesOfChaosPlugin.DiagLog.LogInfo(
                "[BLADE DIAG] Model path: " + modelPath
            );

            if (Directory.Exists(modelFolder))
            {
                string[] files = Directory.GetFiles(modelFolder);

                BladesOfChaosPlugin.DiagLog.LogInfo(
                    "[BLADE DIAG] Files in model folder: " + files.Length
                );

                foreach (string file in files)
                {
                    BladesOfChaosPlugin.DiagLog.LogInfo(
                        "[BLADE DIAG] Found file: " + Path.GetFileName(file)
                    );
                }
            }
            else
            {
                BladesOfChaosPlugin.DiagLog.LogWarning(
                    "[BLADE DIAG] Model folder does not exist."
                );
                return;
            }

            if (!File.Exists(modelPath))
            {
                BladesOfChaosPlugin.DiagLog.LogWarning(
                    "[BLADE DIAG] GLB file does not exist at the expected path."
                );
                return;
            }

            FileInfo info = new FileInfo(modelPath);

            BladesOfChaosPlugin.DiagLog.LogInfo(
                "[BLADE DIAG] GLB size: " + info.Length + " bytes"
            );

            if (info.Length < 20)
            {
                BladesOfChaosPlugin.DiagLog.LogError(
                    "[BLADE DIAG] GLB is too small to be a valid model."
                );
                return;
            }

            using (FileStream stream = File.OpenRead(modelPath))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                uint magic = reader.ReadUInt32();
                uint version = reader.ReadUInt32();
                uint declaredLength = reader.ReadUInt32();

                BladesOfChaosPlugin.DiagLog.LogInfo(
                    "[BLADE DIAG] GLB header: magic=0x" +
                    magic.ToString("X8") +
                    ", version=" +
                    version +
                    ", declaredLength=" +
                    declaredLength
                );

                if (magic != 0x46546C67)
                {
                    BladesOfChaosPlugin.DiagLog.LogError(
                        "[BLADE DIAG] Invalid GLB magic. Expected 0x46546C67."
                    );
                    return;
                }

                if (version != 2)
                {
                    BladesOfChaosPlugin.DiagLog.LogError(
                        "[BLADE DIAG] Unsupported GLB version: " + version
                    );
                    return;
                }

                if (declaredLength > stream.Length ||
                    declaredLength < 20)
                {
                    BladesOfChaosPlugin.DiagLog.LogError(
                        "[BLADE DIAG] GLB declared length is invalid."
                    );
                    return;
                }
            }

            BladesOfChaosPlugin.DiagLog.LogInfo(
                "[BLADE DIAG] Calling ModelAPI.LoadGLB..."
            );

            GameObject model = ModelAPI.LoadGLB(
                modelPath,
                camera.transform,
                new ModelLoadOptions
                {
                    Name = "ULTRADROID_BladesOfChaos_Test",
                    Position = new Vector3(0f, 0f, 2f),
                    RotationEuler = Vector3.zero,
                    Scale = new Vector3(0.1f, 0.1f, 0.1f),
                    LoadTextures = true,
                    CreateColliders = false
                }
            );

            if (model == null)
            {
                BladesOfChaosPlugin.DiagLog.LogError(
                    "[BLADE DIAG] ModelAPI.LoadGLB returned NULL."
                );
                return;
            }

            MeshFilter[] meshes = model.GetComponentsInChildren<MeshFilter>(true);
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);

            BladesOfChaosPlugin.DiagLog.LogInfo(
                "[BLADE DIAG] Model returned: " + model.name
            );

            BladesOfChaosPlugin.DiagLog.LogInfo(
                "[BLADE DIAG] MeshFilter count: " + meshes.Length
            );

            BladesOfChaosPlugin.DiagLog.LogInfo(
                "[BLADE DIAG] Renderer count: " + renderers.Length
            );

            foreach (MeshFilter meshFilter in meshes)
            {
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    BladesOfChaosPlugin.DiagLog.LogInfo(
                        "[BLADE DIAG] Mesh: " +
                        meshFilter.sharedMesh.name +
                        ", vertices=" +
                        meshFilter.sharedMesh.vertexCount
                    );
                }
            }

            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                {
                    BladesOfChaosPlugin.DiagLog.LogInfo(
                        "[BLADE DIAG] Renderer: " +
                        renderer.name +
                        ", enabled=" +
                        renderer.enabled
                    );
                }
            }

            spawned = true;

            BladesOfChaosPlugin.DiagLog.LogInfo(
                "[BLADE DIAG] Model loading call completed."
            );
        }
        catch (Exception ex)
        {
            BladesOfChaosPlugin.DiagLog.LogError(
                "[BLADE DIAG] Exception while checking/loading model:"
            );
            BladesOfChaosPlugin.DiagLog.LogError(ex.ToString());
        }
    }
}
