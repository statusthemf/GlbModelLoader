
using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.SceneManagement;
using ULTRADROID.Models;

namespace ULTRADROID.BladesOfChaos
{
    [BepInPlugin(
        "com.statusthemf.ultradroid.bladesofchaos",
        "ULTRADROID Blades of Chaos",
        "0.3.0"
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

            Log.LogInfo("[BLADE DIAG 0.3.0] Plugin starting.");
            Log.LogInfo("[BLADE DIAG] Plugin path: " + Paths.PluginPath);

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<BladesOfChaosDiagnosticRunner>();
                Log.LogInfo("[BLADE DIAG] Runner type registered.");

                GameObject obj = new GameObject("BladesOfChaosDiagnosticRunner");
                UnityEngine.Object.DontDestroyOnLoad(obj);
                obj.AddComponent<BladesOfChaosDiagnosticRunner>();

                Log.LogInfo("[BLADE DIAG] Runner component added.");

                SceneManager.sceneLoaded += OnSceneLoaded;

                Scene current = SceneManager.GetActiveScene();
                Log.LogInfo(
                    "[BLADE DIAG] Current scene: " +
                    current.name + ", loaded=" + current.isLoaded
                );
            }
            catch (Exception ex)
            {
                Log.LogError("[BLADE DIAG] Startup exception:\n" + ex);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Log.LogInfo(
                "[BLADE DIAG] Scene loaded callback: " +
                scene.name + ", mode=" + mode
            );
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

            if (BladesOfChaosPlugin.DiagLog == null)
                return;

            if (Time.unscaledTime >= nextLog)
            {
                nextLog = Time.unscaledTime + 5f;

                Scene scene = SceneManager.GetActiveScene();
                Camera cam = Camera.main;

                if (cam == null)
                    cam = UnityEngine.Object.FindObjectOfType<Camera>();

                BladesOfChaosPlugin.DiagLog.LogInfo(
                    "[BLADE DIAG] UPDATE WORKS. Count=" + updateCount +
                    ", scene=" + scene.name +
                    ", loaded=" + scene.isLoaded +
                    ", camera=" + (cam != null ? cam.name : "<none>")
                );
            }

            if (spawned || Time.unscaledTime < nextAttempt)
                return;

            nextAttempt = Time.unscaledTime + 5f;
            TrySpawn();
        }

        private void TrySpawn()
        {
            var log = BladesOfChaosPlugin.DiagLog;

            try
            {
                Scene scene = SceneManager.GetActiveScene();

                if (!scene.IsValid() || !scene.isLoaded)
                {
                    log.LogInfo("[BLADE DIAG] Waiting for a loaded scene.");
                    return;
                }

                string name = (scene.name ?? "").ToLowerInvariant();

                if (name.Contains("bootstrap") ||
                    name.Contains("intro") ||
                    name.Contains("menu") ||
                    name.Contains("loading"))
                {
                    log.LogInfo("[BLADE DIAG] Skipping startup scene: " + scene.name);
                    return;
                }

                Camera cam = Camera.main;

                if (cam == null)
                    cam = UnityEngine.Object.FindObjectOfType<Camera>();

                if (cam == null)
                {
                    log.LogWarning("[BLADE DIAG] No camera found yet.");
                    return;
                }

                string folder = Path.Combine(
                    Paths.PluginPath,
                    "ULTRADROID.BladesOfChaos"
                );

                string path = Path.Combine(
                    folder,
                    "blades_of_chaos_god.glb"
                );

                log.LogInfo("[BLADE DIAG] Gameplay scene: " + scene.name);
                log.LogInfo("[BLADE DIAG] Camera: " + cam.name);
                log.LogInfo("[BLADE DIAG] Looking for: " + path);

                if (!Directory.Exists(folder))
                {
                    log.LogError("[BLADE DIAG] Folder does not exist: " + folder);
                    return;
                }

                foreach (string file in Directory.GetFiles(folder))
                    log.LogInfo("[BLADE DIAG] Found file: " + file);

                if (!File.Exists(path))
                {
                    log.LogError("[BLADE DIAG] GLB file not found at expected path.");
                    return;
                }

                FileInfo info = new FileInfo(path);
                log.LogInfo("[BLADE DIAG] GLB size: " + info.Length + " bytes");

                if (info.Length < 20)
                {
                    log.LogError("[BLADE DIAG] GLB file is too small.");
                    return;
                }

                using (FileStream stream = File.OpenRead(path))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    uint magic = reader.ReadUInt32();
                    uint version = reader.ReadUInt32();
                    uint length = reader.ReadUInt32();

                    log.LogInfo(
                        "[BLADE DIAG] Header: magic=0x" +
                        magic.ToString("X8") +
                        ", version=" + version +
                        ", declaredLength=" + length
                    );

                    if (magic != 0x46546C67 || version != 2 ||
                        length < 20 || length > stream.Length)
                    {
                        log.LogError("[BLADE DIAG] Invalid GLB header.");
                        return;
                    }
                }

                log.LogInfo("[BLADE DIAG] Calling ModelAPI.LoadGLB.");

                GameObject model = ModelAPI.LoadGLB(
                    path,
                    cam.transform,
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
                    log.LogError("[BLADE DIAG] LoadGLB returned null.");
                    return;
                }

                spawned = true;

                MeshFilter[] meshes =
                    model.GetComponentsInChildren<MeshFilter>(true);

                Renderer[] renderers =
                    model.GetComponentsInChildren<Renderer>(true);

                log.LogInfo("[BLADE DIAG] Model created: " + model.name);
                log.LogInfo("[BLADE DIAG] MeshFilter count: " + meshes.Length);
                log.LogInfo("[BLADE DIAG] Renderer count: " + renderers.Length);

                for (int i = 0; i < meshes.Length; i++)
                {
                    Mesh mesh = meshes[i].sharedMesh;

                    log.LogInfo(
                        "[BLADE DIAG] Mesh " + i +
                        ": " + (mesh != null ? mesh.name : "<null>") +
                        ", vertices=" + (mesh != null ? mesh.vertexCount : 0)
                    );
                }

                if (meshes.Length == 0)
                    log.LogError("[BLADE DIAG] Model has no MeshFilters.");
                else if (renderers.Length == 0)
                    log.LogError("[BLADE DIAG] Model has no Renderers.");
                else
                    log.LogInfo("[BLADE DIAG] Model objects and renderers exist.");
            }
            catch (Exception ex)
            {
                log.LogError("[BLADE DIAG] Spawn attempt exception:\n" + ex);
            }
        }
    }
}
