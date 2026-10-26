using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.SceneManagement;
using ULTRADROID.Models;

[BepInPlugin(
"ultradroid.bladesofchaos",
"ULTRADROID Blades of Chaos",
"0.6.0")]
public sealed class BladesOfChaosPlugin : BasePlugin
{
public override void Load()
{
BladesOfChaosRunner.PluginLog = Log;

ClassInjector.RegisterTypeInIl2Cpp<BladesOfChaosRunner>();  
    AddComponent<BladesOfChaosRunner>();  

    Log.LogInfo(  
        "ULTRADROID Blades of Chaos example loaded.");  
}

}

public sealed class BladesOfChaosRunner : MonoBehaviour
{
internal static ManualLogSource PluginLog;

private const string ModelFileName =  
    "blades_of_chaos_god.glb";  

private const string AnimationName = "Bow";  

private const float LoadRetryInterval = 2f;  

private GameObject _modelRoot;  
private GlbAnimationPlayer _animationPlayer;  
private Camera _gameCamera;  

private string _lastSceneName = string.Empty;  
private float _nextLoadAttempt;  
private bool _loadErrorReported;  

public BladesOfChaosRunner(IntPtr ptr) : base(ptr)  
{  
}  

public void Update()  
{  
    string sceneName =  
        SceneManager.GetActiveScene().name;  

    // Clean up when the active scene changes.  
    if (!string.Equals(  
        sceneName,  
        _lastSceneName,  
        StringComparison.Ordinal))  
    {  
        _lastSceneName = sceneName;  

        ClearLoadedModel();  

        _nextLoadAttempt = 0f;  
        _loadErrorReported = false;  
    }  

    if (IsNonGameplayScene(sceneName))  
        return;  

    if (_modelRoot != null)  
    {  
        Camera currentCamera = FindGameCamera();  

        if (currentCamera != null)  
        {  
            if (_gameCamera != currentCamera ||  
                _modelRoot.transform.parent != currentCamera.transform)  
            {  
                _gameCamera = currentCamera;  

                _modelRoot.transform.SetParent(  
                    _gameCamera.transform,  
                    false);  

                _modelRoot.transform.localPosition =  
                    new Vector3(0f, 0f, 1f);  

                _modelRoot.transform.localRotation =  
                    Quaternion.identity;  

                _modelRoot.transform.localScale =  
                    Vector3.one * 0.1f;  
            }  
        }  
    }  

    // Retry until a model can be loaded.  
    if (_modelRoot == null)  
    {  
        if (Time.unscaledTime >= _nextLoadAttempt)  
        {  
            _nextLoadAttempt =  
                Time.unscaledTime + LoadRetryInterval;  

            TryLoadModel();  
        }  

        return;  
    }  

    // GlbAnimationPlayer.Update() already updates CPU skinning.  
    if (_animationPlayer != null)  
    {  
        _animationPlayer.Update(Time.deltaTime);  
    }  
    else  
    {  
        // Preserve CPU skinning when no animation player exists.  
        ModelAPI.UpdateCpuSkinning(_modelRoot);  
    }  
}  

private static bool IsNonGameplayScene(string sceneName)  
{  
    if (string.IsNullOrEmpty(sceneName))  
        return true;  

    string scene = sceneName.ToLowerInvariant();  

    return scene.Contains("bootstrap") ||  
           scene == "intro" ||  
           scene.Contains("mainmenu") ||  
           scene.Contains("main_menu") ||  
           scene.Contains("main menu") ||  
           scene.Contains("title") ||  
           scene.Contains("loading") ||  
           scene.Contains("credits") ||  
           scene.Contains("ending");  
}  

private void TryLoadModel()  
{  
    string modelPath = FindModelFile();  

    if (string.IsNullOrEmpty(modelPath))  
    {  
        ReportLoadProblem(  
            "Could not find " + ModelFileName +  
            ". Place it beside the plugin DLL, inside its " +  
            "assets folder, or in the supported plugin folders.");  

        return;  
    }  

    Camera camera = FindGameCamera();  

    if (camera == null)  
    {  
        ReportLoadProblem(  
            "No active game camera was found. Will retry.");  

        return;  
    }  

    GameObject loaded = null;  

    try  
    {  
        ModelLoadOptions options = new ModelLoadOptions  
        {  
            LoadTextures = true,  
            FlipUvV = true  
        };  

        LogInfo("Loading GLB: " + modelPath);  

        loaded = ModelAPI.LoadGLB(  
            modelPath,  
            camera.transform,  
            options);  

        if (loaded == null)  
        {  
            ReportLoadProblem(  
                "ModelAPI.LoadGLB returned null.");  

            return;  
        }  

        _modelRoot = loaded;  
        _modelRoot.name = "ULTRADROID_BladesOfChaos";  

        _gameCamera = camera;  

        _modelRoot.transform.SetParent(  
            _gameCamera.transform,  
            false);  

        _modelRoot.transform.localPosition =  
            new Vector3(0f, 0f, 1f);  

        _modelRoot.transform.localRotation =  
            Quaternion.identity;  

        _modelRoot.transform.localScale =  
            Vector3.one * 0.1f;  

        _modelRoot.SetActive(true);  

        LogModelVisibility();

        LogInfo(  
            "Camera-relative placement: localPosition=" +  
            _modelRoot.transform.localPosition +  
            ", localScale=" +  
            _modelRoot.transform.localScale +  
            ", camera=" + _gameCamera.name);  

        // Temporarily disabled for visibility debugging.  
        // _animationPlayer = ModelAPI.GetAnimationPlayer(_modelRoot);  
        // if (_animationPlayer != null)  
        //     _animationPlayer.Play(AnimationName, true);  

        LogInfo("GLB model loaded successfully.");  
    }  
    catch (Exception ex)  
    {  
        LogError(  
            "Failed to load or initialize the GLB model: " + ex);  

        // Avoid leaving a partially initialized model behind.  
        if (loaded != null)  
            UnityEngine.Object.Destroy(loaded);  

        _modelRoot = null;  
        _animationPlayer = null;  
    }  
}  

private static Camera FindGameCamera()  
{  
    Camera main = Camera.main;  

    if (main != null && main.isActiveAndEnabled)  
        return main;  

    Camera[] cameras = Camera.allCameras;  

    if (cameras == null)  
        return null;  

    for (int i = 0; i < cameras.Length; i++)  
    {  
        Camera candidate = cameras[i];  

        if (candidate != null &&  
            candidate.isActiveAndEnabled)  
        {  
            return candidate;  
        }  
    }  

    return null;  
}  

private static string FindModelFile()  
{  
    List<string> candidates = new List<string>();  

    try  
    {  
        string assemblyDirectory =  
            Path.GetDirectoryName(  
                typeof(BladesOfChaosPlugin)  
                    .Assembly.Location);  

        if (!string.IsNullOrEmpty(assemblyDirectory))  
        {  
            candidates.Add(  
                Path.Combine(  
                    assemblyDirectory,  
                    ModelFileName));  

            candidates.Add(  
                Path.Combine(  
                    assemblyDirectory,  
                    "assets",  
                    ModelFileName));  

            candidates.Add(  
                Path.Combine(  
                    assemblyDirectory,  
                    "Assets",  
                    ModelFileName));  
        }  
    }  
    catch  
    {  
        // Continue checking plugin directories.  
    }  

    try  
    {  
        candidates.Add(  
            Path.Combine(  
                Paths.PluginPath,  
                ModelFileName));  

        candidates.Add(  
            Path.Combine(  
                Paths.PluginPath,  
                "BladesOfChaos",  
                ModelFileName));  

        candidates.Add(  
            Path.Combine(  
                Paths.PluginPath,  
                "ULTRADROID.BladesOfChaos",  
                ModelFileName));  
    }  
    catch  
    {  
        // Other candidate locations may still work.  
    }  

    for (int i = 0; i < candidates.Count; i++)  
    {  
        try  
        {  
            if (File.Exists(candidates[i]))  
                return candidates[i];  
        }  
        catch  
        {  
            // Try the next candidate.  
        }  
    }  

    return null;  
}  

private void LogModelVisibility()
{
    if (_modelRoot == null)
        return;

    Renderer[] renderers =
        _modelRoot.GetComponentsInChildren<Renderer>(true);

    LogInfo(
        "Visibility check: active=" +
        _modelRoot.activeInHierarchy +
        ", camera=" +
        (_gameCamera != null ? _gameCamera.name : "NULL") +
        ", rendererCount=" + renderers.Length);

    for (int i = 0; i < renderers.Length; i++)
    {
        Renderer renderer = renderers[i];

        if (renderer == null)
            continue;

        LogInfo(
            "Renderer " + i +
            ": name=" + renderer.name +
            ", enabled=" + renderer.enabled +
            ", active=" + renderer.gameObject.activeInHierarchy +
            ", boundsCenter=" + renderer.bounds.center +
            ", boundsSize=" + renderer.bounds.size +
            ", materials=" + renderer.sharedMaterials.Length);

        Material[] materials = renderer.sharedMaterials;

        for (int j = 0; j < materials.Length; j++)
        {
            Material material = materials[j];

            LogInfo(
                "Material " + j + ": " +
                (material != null
                    ? material.name + ", shader=" +
                      (material.shader != null
                          ? material.shader.name
                          : "NULL")
                    : "NULL"));
        }
    }
}

private void ReportLoadProblem(string message)  
{  
    if (_loadErrorReported)  
        return;  

    _loadErrorReported = true;  
    LogWarning(message);  
}  

private void ClearLoadedModel()  
{  
    if (_modelRoot != null)  
        UnityEngine.Object.Destroy(_modelRoot);  

    _modelRoot = null;  
    _animationPlayer = null;  
}  

public void OnDestroy()  
{  
    ClearLoadedModel();  
}  

private static void LogInfo(string message)  
{  
    if (PluginLog != null)  
        PluginLog.LogInfo(  
            "[Blades of Chaos] " + message);  
}  

private static void LogWarning(string message)  
{  
    if (PluginLog != null)  
        PluginLog.LogWarning(  
            "[Blades of Chaos] " + message);  
}  

private static void LogError(string message)  
{  
    if (PluginLog != null)  
        PluginLog.LogError(  
            "[Blades of Chaos] " + message);  
}

}
