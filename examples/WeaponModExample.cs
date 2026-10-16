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
            Log.LogInfo("================================================");
            Log.LogInfo("[BladesOfChaos DIAG] VERSION 0.2.0 STARTING");
            Log.LogInfo("[BladesOfChaos DIAG] Plugin directory: " + Paths.PluginPath);

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<BladesOfChaosDiagnosticRunner>();

                GameObject runnerObject =
                    new GameObject("ULTRADROID_BladesOfChaos_DiagnosticRunner");

                UnityEngine.Object.DontDestroyOnLoad(runnerObject);
                runnerObject.AddComponent<BladesOfChaosDiagnosticRunner>();

                Log.LogInfo("[BladesOfChaos DIAG] Diagnostic runner created.");
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[BladesOfChaos DIAG] Could not create runner:\n" + ex
                );
            }

            Log.LogInfo("================================================");
        }
    }

    public sealed class BladesOfChaosDiagnosticRunner : MonoBehaviour
    {
        private bool finished;
        private float nextStatusTime;
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

            // Print a status message periodically, not every frame.
            if (Time.unscaledTime >= nextStatusTime)
            {
                nextStatusTime = Time.unscaledTime + 5f;

                try
                {
                    Scene scene = SceneManager.GetActiveScene();

                    Camera mainCamera = Camera.main;
                    if (mainCamera == null)
                        mainCamera = UnityEngine.Object.FindObjectOfType<Camera>();

                    string cameraName =
                        mainCamera != null ? mainCamera.name : "<no camera>";

                    Debug.Log(
                        "[BladesOfChaos DIAG] Waiting. Scene='" +
                        scene.name +
                        "', loaded=" + scene.isLoaded +
                        ", camera='" + cameraName +
                        "', frames=" + waitingFrames
                    );
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        "[BladesOfChaos DIAG] Status check failed: " + ex
                    );
                }
            }

            TryLoadModel();
        }

        private void TryLoadModel()
        {
            Scene scene;

            try
            {
                scene = SceneManager.GetActiveScene();
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "[BladesOfChaos DIAG] Cannot read active scene: " + ex
                );
                return;
            }

            if (!scene.IsValid() || !scene.isLoaded)
                return;

            string sceneName = scene.name ?? "";
            string lowerSceneName = sceneName.ToLowerInvariant();

            if (lowerSceneName.Contains("bootstrap") ||
                lowerSceneName.Contains("intro") ||
                lowerSceneName.Contains("menu") ||
                lowerSceneName.Contains("loading"))
            {
                return;
            }

            Camera targetCamera = Camera.main;

            if (targetCamera == null)
                targetCamera = UnityEngine.Object.FindObjectOfType<Camera>();

            if (targetCamera == null)
                return;

            finished = true;

            string modelDirectory = Path.Combine(
                Paths.PluginPath,
                "ULTRADROID.BladesOfChaos"
            );

            string modelPath = Path.Combine(
                modelDirectory,
                "blades_of_chaos_god.glb"
            );

            Debug.Log("[BladesOfChaos DIAG] Gameplay scene detected: " + sceneName);
            Debug.Log("[BladesOfChaos DIAG] Camera found: " + targetCamera.name);
            Debug.Log("[BladesOfChaos DIAG] Model directory: " + modelDirectory);
            Debug.Log("[BladesOfChaos DIAG] Model path: " + modelPath);

            try
            {
                if (!Directory.Exists(modelDirectory))
                {
                    Debug.LogError(
                        "[BladesOfChaos DIAG] MODEL DIRECTORY DOES NOT EXIST."
                    );
                    finished = false;
                    return;
                }

                string[] files = Directory.GetFiles(modelDirectory);

                Debug.Log(
                    "[BladesOfChaos DIAG] Files in model directory: " +
                    files.Length
                );

                for (int i = 0; i < files.Length; i++)
                {
                    Debug.Log(
                        "[BladesOfChaos DIAG] File[" + i + "]: " + files[i]
                    );
                }

                if (!File.Exists(modelPath))
                {
                    Debug.LogError(
                        "[BladesOfChaos DIAG] GLB FILE NOT FOUND. " +
                        "The filename must be exactly: blades_of_chaos_god.glb"
                    );

                    finished = false;
                    return;
                }

                FileInfo info = new FileInfo(modelPath);

                Debug.Log(
                    "[BladesOfChaos DIAG] GLB exists. Size: " +
                    info.Length + " bytes"
                );

                if (info.Length < 20)
                {
                    Debug.LogError(
                        "[BladesOfChaos DIAG] GLB is suspiciously small."
                    );
                    finished = false;
                    return;
                }

                // Read the GLB header: magic should be "glTF".
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
                        Debug.LogError(
                            "[BladesOfChaos DIAG] INVALID GLB MAGIC. " +
                            "The file may not be a valid binary glTF."
                        );
                        finished = false;
                        return;
                    }

                    if (version != 2)
                    {
                        Debug.LogError(
                            "[BladesOfChaos DIAG] Expected GLB version 2."
                        );
                        finished = false;
                        return;
                    }

                    if (declaredLength > stream.Length)
                    {
                        Debug.LogError(
                            "[BladesOfChaos DIAG] Header length exceeds file size."
                        );
                        finished = false;
                        return;
                    }
                }

                Debug.Log("[BladesOfChaos DIAG] Calling ModelAPI.LoadGLB now.");

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
                    Debug.LogError(
                        "[BladesOfChaos DIAG] ModelAPI.LoadGLB returned NULL."
                    );
                    return;
                }

                Debug.Log(
                    "[BladesOfChaos DIAG] LoadGLB returned object: " +
                    model.name
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
                    MeshFilter filter = meshFilters[i];

                    string meshName =
                        filter.sharedMesh != null
                            ? filter.sharedMesh.name
                            : "<null>";

                    int vertexCount =
                        filter.sharedMesh != null
                            ? filter.sharedMesh.vertexCount
                            : 0;

                    Debug.Log(
                        "[BladesOfChaos DIAG] Mesh[" + i + "]='" +
                        meshName + "', vertices=" + vertexCount
                    );
                }

                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];

                    Debug.Log(
                        "[BladesOfChaos DIAG] Renderer[" + i + "]='" +
                        renderer.name +
                        "', enabled=" + renderer.enabled +
                        ", active=" + renderer.gameObject.activeInHierarchy
                    );
                }

                if (meshFilters.Length == 0)
                {
                    Debug.LogError(
                        "[BladesOfChaos DIAG] MODEL HAS NO MESH FILTERS. " +
                        "Inspect the GLB loader's mesh creation code."
                    );
                }
                else if (renderers.Length == 0)
                {
                    Debug.LogError(
                        "[BladesOfChaos DIAG] MESHES EXIST BUT NO RENDERERS."
                    );
                }
                else
                {
                    Debug.LogInfo(
                        "[BladesOfChaos DIAG] MODEL CREATED. " +
                        "Check visibility, scale, orientation, and materials in-game."
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
