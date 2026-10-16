
using System;
using System.IO;
using BepInEx;
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
        "0.2.0"
    )]
    [BepInDependency(
        "com.statusthemf.ultradroid.models",
        BepInDependency.DependencyFlags.HardDependency
    )]
    public sealed class BladesOfChaosPlugin : BasePlugin
    {
        public override void Load()
        {
            Log.LogInfo("[BladesOfChaos DIAG] VERSION 0.2.0 STARTING");
            Log.LogInfo("[BladesOfChaos DIAG] Plugin path: " + Paths.PluginPath);

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<BladesOfChaosDiagnosticRunner>();

                GameObject runnerObject =
                    new GameObject("ULTRADROID_BladesOfChaos_DiagnosticRunner");

                UnityEngine.Object.DontDestroyOnLoad(runnerObject);
                runnerObject.AddComponent<BladesOfChaosDiagnosticRunner>();

                Log.LogInfo("[BladesOfChaos DIAG] Runner created.");
            }
            catch (Exception ex)
            {
                Log.LogError("[BladesOfChaos DIAG] Runner creation failed:\n" + ex);
            }
        }
    }

    public sealed class BladesOfChaosDiagnosticRunner : MonoBehaviour
    {
        private bool finished;
        private float nextStatusTime;
        private float nextAttemptTime;
        private int waitingFrames;

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
            if (finished)
                return;

            waitingFrames++;

            if (Time.unscaledTime >= nextStatusTime)
            {
                nextStatusTime = Time.unscaledTime + 5f;

                try
                {
                    Scene scene = SceneManager.GetActiveScene();
                    Camera camera = Camera.main;

                    if (camera == null)
                        camera = UnityEngine.Object.FindObjectOfType<Camera>();

                    Debug.Log(
                        "[BladesOfChaos DIAG] Waiting. Scene='" +
                        scene.name +
                        "', loaded=" + scene.isLoaded +
                        ", camera='" +
                        (camera != null ? camera.name : "<no camera>") +
                        "', frames=" + waitingFrames
                    );
                }
                catch (Exception ex)
                {
                    Debug.LogError("[BladesOfChaos DIAG] Status check failed:\n" + ex);
                }
            }

            if (Time.unscaledTime < nextAttemptTime)
                return;

            nextAttemptTime = Time.unscaledTime + 5f;
            TryLoadModel();
        }

        private void TryLoadModel()
        {
            try
            {
                Scene scene = SceneManager.GetActiveScene();

                if (!scene.IsValid() || !scene.isLoaded)
                    return;

                string sceneName = scene.name ?? "";
                string lowerName = sceneName.ToLowerInvariant();

                if (lowerName.Contains("bootstrap") ||
                    lowerName.Contains("intro") ||
                    lowerName.Contains("menu") ||
                    lowerName.Contains("loading"))
                {
                    return;
                }

                Camera targetCamera = Camera.main;

                if (targetCamera == null)
                    targetCamera = UnityEngine.Object.FindObjectOfType<Camera>();

                if (targetCamera == null)
                    return;

                string modelDirectory = Path.Combine(
                    Paths.PluginPath,
                    "ULTRADROID.BladesOfChaos"
                );

                string modelPath = Path.Combine(
                    modelDirectory,
                    "blades_of_chaos_god.glb"
                );

                Debug.Log("[BladesOfChaos DIAG] Gameplay scene: " + sceneName);
                Debug.Log("[BladesOfChaos DIAG] Camera: " + targetCamera.name);
                Debug.Log("[BladesOfChaos DIAG] Model path: " + modelPath);

                if (!Directory.Exists(modelDirectory))
                {
                    Debug.LogError(
                        "[BladesOfChaos DIAG] MODEL DIRECTORY NOT FOUND: " +
                        modelDirectory
                    );
                    return;
                }

                string[] files = Directory.GetFiles(modelDirectory);

                Debug.Log(
                    "[BladesOfChaos DIAG] Files in model folder: " +
                    files.Length
                );

                foreach (string file in files)
                    Debug.Log("[BladesOfChaos DIAG] File: " + file);

                if (!File.Exists(modelPath))
                {
                    Debug.LogError(
                        "[BladesOfChaos DIAG] GLB NOT FOUND. Expected exact filename: " +
                        "blades_of_chaos_god.glb"
                    );
                    return;
                }

                FileInfo info = new FileInfo(modelPath);

                Debug.Log(
                    "[BladesOfChaos DIAG] GLB size: " +
                    info.Length + " bytes"
                );

                if (info.Length < 20)
                {
                    Debug.LogError("[BladesOfChaos DIAG] GLB file is too small.");
                    return;
                }

                using (FileStream stream = File.OpenRead(modelPath))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    uint magic = reader.ReadUInt32();
                    uint version = reader.ReadUInt32();
                    uint declaredLength = reader.ReadUInt32();

                    Debug.Log(
                        "[BladesOfChaos DIAG] GLB header: magic=0x" +
                        magic.ToString("X8") +
                        ", version=" + version +
                        ", declaredLength=" + declaredLength
                    );

                    if (magic != 0x46546C67)
                    {
                        Debug.LogError("[BladesOfChaos DIAG] Invalid GLB magic.");
                        return;
                    }

                    if (version != 2)
                    {
                        Debug.LogError("[BladesOfChaos DIAG] GLB version must be 2.");
                        return;
                    }

                    if (declaredLength > stream.Length || declaredLength < 20)
                    {
                        Debug.LogError("[BladesOfChaos DIAG] Invalid GLB declared length.");
                        return;
                    }
                }

                Debug.Log("[BladesOfChaos DIAG] Calling ModelAPI.LoadGLB...");

                ModelLoadOptions options = new ModelLoadOptions
                {
                    Name = "ULTRADROID_BladesOfChaos_DIAGNOSTIC",
                    Position = new Vector3(0f, 0f, 2f),
                    RotationEuler = Vector3.zero,
                    Scale = new Vector3(0.1f, 0.1f, 0.1f),
                    LoadTextures = true,
                    CreateColliders = false
                };

                GameObject model = ModelAPI.LoadGLB(
                    modelPath,
                    targetCamera.transform,
                    options
                );

                if (model == null)
                {
                    Debug.LogError("[BladesOfChaos DIAG] LoadGLB returned NULL.");
                    return;
                }

                finished = true;

                Debug.Log(
                    "[BladesOfChaos DIAG] Model object created: " + model.name
                );

                MeshFilter[] meshFilters =
                    model.GetComponentsInChildren<MeshFilter>(true);

                Renderer[] renderers =
                    model.GetComponentsInChildren<Renderer>(true);

                Debug.Log(
                    "[BladesOfChaos DIAG] MeshFilter count: " +
                    meshFilters.Length
                );

                Debug.Log(
                    "[BladesOfChaos DIAG] Renderer count: " +
                    renderers.Length
                );

                for (int i = 0; i < meshFilters.Length; i++)
                {
                    Mesh mesh = meshFilters[i].sharedMesh;

                    Debug.Log(
                        "[BladesOfChaos DIAG] Mesh[" + i + "]: " +
                        (mesh != null ? mesh.name : "<null>") +
                        ", vertices=" +
                        (mesh != null ? mesh.vertexCount : 0)
                    );
                }

                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];

                    Debug.Log(
                        "[BladesOfChaos DIAG] Renderer[" + i + "]: " +
                        renderer.name +
                        ", enabled=" + renderer.enabled +
                        ", active=" + renderer.gameObject.activeInHierarchy
                    );
                }

                if (meshFilters.Length == 0)
                {
                    Debug.LogError(
                        "[BladesOfChaos DIAG] No MeshFilters found. " +
                        "Inspect the GLB loader's mesh creation."
                    );
                }
                else if (renderers.Length == 0)
                {
                    Debug.LogError(
                        "[BladesOfChaos DIAG] Meshes exist, but no renderers were found."
                    );
                }
                else
                {
                    Debug.Log(
                        "[BladesOfChaos DIAG] MODEL CREATED. " +
                        "Check visibility, scale, orientation and materials."
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "[BladesOfChaos DIAG] MODEL LOAD EXCEPTION:\n" + ex
                );
            }
        }
    }
}
