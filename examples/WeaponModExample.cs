
using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.SceneManagement;
using ULTRADROID.Models;

[BepInPlugin(
    "com.statusthemf.glbmodelloader.example",
    "GLB Model Loader Example",
    "1.0.0")]
[BepInDependency(
    "com.statusthemf.ultradroid.models",
    BepInDependency.DependencyFlags.HardDependency)]
public sealed class WeaponModExample : BasePlugin
{
    internal static ManualLogSource ExampleLog { get; private set; }

    public override void Load()
    {
        ExampleLog = Log;
        Log.LogInfo("[GLB Example] Plugin loaded.");

        try
        {
            AddComponent<StandaloneModelRunner>();
        }
        catch (Exception ex)
        {
            Log.LogError("[GLB Example] Initialization failed: " + ex);
        }
    }
}

public sealed class StandaloneModelRunner : MonoBehaviour
{
    private const float RetryInterval = 5f;

    private const string ModelFolder = "ULTRADROID.BladesOfChaos";
    private const string KnifeFile = "blades_of_chaos_god.glb";
    private const string AwpFile = "awp.glb";

    private float nextAttemptTime;
    private string lastSceneName = "";
    private bool modelsLoaded;

    private GameObject knifeModel;
    private GameObject awpModel;

    public StandaloneModelRunner(IntPtr ptr) : base(ptr)
    {
    }

    private void Start()
    {
        nextAttemptTime = Time.unscaledTime + 1f;
    }

    private void Update()
    {
        Scene scene = SceneManager.GetActiveScene();

        if (!scene.isLoaded)
            return;

        if (scene.name != lastSceneName)
        {
            lastSceneName = scene.name;
            modelsLoaded = false;
            nextAttemptTime = Time.unscaledTime + 1f;
        }

        if (modelsLoaded || Time.unscaledTime < nextAttemptTime)
            return;

        nextAttemptTime = Time.unscaledTime + RetryInterval;

        try
        {
            Camera camera = Camera.main;

            if (camera == null)
            {
                WeaponModExample.ExampleLog.LogInfo(
                    "[GLB Example] Waiting for main camera.");
                return;
            }

            LoadModels(camera);
        }
        catch (Exception ex)
        {
            WeaponModExample.ExampleLog.LogError(
                "[GLB Example] Model loading failed: " + ex);
        }
    }

    private void LoadModels(Camera camera)
    {
        string folder = Path.Combine(Paths.PluginPath, ModelFolder);

        string knifePath = Path.Combine(folder, KnifeFile);
        string awpPath = Path.Combine(folder, AwpFile);

        if (!File.Exists(knifePath))
        {
            WeaponModExample.ExampleLog.LogError(
                "[GLB Example] Knife file not found: " + knifePath);
            return;
        }

        if (!File.Exists(awpPath))
        {
            WeaponModExample.ExampleLog.LogError(
                "[GLB Example] AWP file not found: " + awpPath);
            return;
        }

        ModelLoadOptions knifeOptions = new ModelLoadOptions
        {
            Name = "ULTRADROID_Example_Knife",
            Position = new Vector3(0f, -0.65f, 1f),
            RotationEuler = Vector3.zero,
            Scale = new Vector3(0.03f, 0.03f, 0.03f),
            LoadTextures = true,
            CreateColliders = false
        };

        ModelLoadOptions awpOptions = new ModelLoadOptions
        {
            Name = "ULTRADROID_Example_AWP",
            Position = new Vector3(-0.35f, -0.4f, 1.2f),
            RotationEuler = Vector3.zero,
            Scale = new Vector3(0.005f, 0.005f, 0.005f),
            LoadTextures = true,
            CreateColliders = false
        };

        WeaponModExample.ExampleLog.LogInfo(
            "[GLB Example] Loading knife model.");

        knifeModel = ModelAPI.LoadGLB(
            knifePath,
            camera.transform,
            knifeOptions);

        if (knifeModel == null)
        {
            WeaponModExample.ExampleLog.LogError(
                "[GLB Example] Knife loader returned null.");
            return;
        }

        WeaponModExample.ExampleLog.LogInfo(
            "[GLB Example] Knife model loaded.");

        try
        {
            WeaponModExample.ExampleLog.LogInfo(
                "[GLB Example] Loading AWP model.");

            awpModel = ModelAPI.LoadGLB(
                awpPath,
                camera.transform,
                awpOptions);

            if (awpModel == null)
            {
                WeaponModExample.ExampleLog.LogError(
                    "[GLB Example] AWP loader returned null.");

                Destroy(knifeModel);
                knifeModel = null;
                return;
            }

            WeaponModExample.ExampleLog.LogInfo(
                "[GLB Example] AWP model loaded.");

            modelsLoaded = true;
        }
        catch
        {
            if (knifeModel != null)
            {
                Destroy(knifeModel);
                knifeModel = null;
            }

            throw;
        }
    }
}
