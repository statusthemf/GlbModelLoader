
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using ULTRADROID.API;
using ULTRADROID.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;

[BepInPlugin(
    "ultradroid.bladesofchaos",
    "ULTRADROID Blades of Chaos",
    "0.8.0")]
[BepInDependency(
    ULTRADROIDControls.PluginGuid,
    BepInDependency.DependencyFlags.HardDependency)]
public sealed class BladesOfChaosPlugin : BasePlugin
{
    public override void Load()
    {
        BladesOfChaosRunner.PluginLog = Log;

        ClassInjector.RegisterTypeInIl2Cpp<BladesOfChaosRunner>();
        AddComponent<BladesOfChaosRunner>();

        Log.LogInfo("[Blades of Chaos] Animation tester loaded.");
    }
}

public sealed class BladesOfChaosRunner : MonoBehaviour
{
    internal static ManualLogSource PluginLog;

    private const string ModelFileName = "blades_of_chaos_god.glb";
    private const float RetryInterval = 2f;

    private GameObject _modelRoot;
    private GlbAnimationPlayer _player;
    private GlbAnimationData[] _animations = new GlbAnimationData[0];

    private readonly List<ButtonHandle> _buttons =
        new List<ButtonHandle>();

    private string _lastScene = "";
    private float _nextRetry;
    private bool _buttonsCreated;
    private bool _loop = true;
    private int _selectedIndex;

    public BladesOfChaosRunner(IntPtr ptr) : base(ptr)
    {
    }

    public void Update()
    {
        string scene = SceneManager.GetActiveScene().name;

        if (!string.Equals(scene, _lastScene, StringComparison.Ordinal))
        {
            _lastScene = scene;
            ClearModel();
            RemoveButtons();
            _nextRetry = 0f;
        }

        if (IsMenu(scene))
            return;

        if (_modelRoot == null && Time.unscaledTime >= _nextRetry)
        {
            _nextRetry = Time.unscaledTime + RetryInterval;
            TryLoadModel();
        }

        if (_player != null)
            _player.Update(Time.deltaTime);
        else if (_modelRoot != null)
            ModelAPI.UpdateCpuSkinning(_modelRoot);
    }

    private static bool IsMenu(string scene)
    {
        if (string.IsNullOrEmpty(scene))
            return true;

        string name = scene.ToLowerInvariant();

        return name.Contains("bootstrap") ||
               name == "intro" ||
               name.Contains("main menu") ||
               name.Contains("mainmenu") ||
               name.Contains("title") ||
               name.Contains("loading") ||
               name.Contains("credits");
    }

    private void TryLoadModel()
    {
        string path = FindModelFile();

        if (string.IsNullOrEmpty(path))
        {
            LogWarning("Could not find " + ModelFileName);
            return;
        }

        Camera camera = Camera.main;

        if (camera == null)
        {
            Camera[] cameras = Camera.allCameras;

            for (int i = 0; cameras != null && i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].isActiveAndEnabled)
                {
                    camera = cameras[i];
                    break;
                }
            }
        }

        if (camera == null)
            return;

        try
        {
            _animations = ModelAPI.ReadGLBAnimations(path)
                ?? new GlbAnimationData[0];

            LogInfo("Animation clips found: " + _animations.Length);

            for (int i = 0; i < _animations.Length; i++)
            {
                LogInfo(
                    "Clip [" + i + "]: " +
                    (_animations[i].Name ?? "(unnamed)"));
            }

            ModelLoadOptions options = new ModelLoadOptions
            {
                LoadTextures = true,
                FlipUvV = true
            };

            _modelRoot = ModelAPI.LoadGLB(
                path,
                camera.transform,
                options);

            if (_modelRoot == null)
            {
                LogWarning("ModelAPI.LoadGLB returned null.");
                return;
            }

            _modelRoot.name = "ULTRADROID_BladesOfChaos";
            _modelRoot.transform.localPosition = new Vector3(0f, 0f, 1f);
            _modelRoot.transform.localRotation = Quaternion.identity;
            _modelRoot.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);

            _player = ModelAPI.GetAnimationPlayer(_modelRoot);

            if (_player == null)
            {
                LogWarning("Model loaded, but no animation player was registered.");
                return;
            }

            _selectedIndex = FindPreferredClip("Bow");
            _player.PlaybackSpeed = 1f;
            _loop = true;

            if (_animations.Length > 0)
                PlaySelected();

            CreateButtons();

            LogInfo("Model and animation player initialized.");
        }
        catch (Exception ex)
        {
            LogError("Initialization failed: " + ex);

            ClearModel();
            RemoveButtons();
        }
    }

    private int FindPreferredClip(string preferred)
    {
        for (int i = 0; i < _animations.Length; i++)
        {
            if (string.Equals(
                _animations[i].Name,
                preferred,
                StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    private void CreateButtons()
    {
        if (_buttonsCreated)
            return;

        _buttonsCreated = true;

        // Small row placed near the bottom-right area.
        // PositionOffset follows the coordinate convention of Controls API.
        AddControl("Prev", -250f, 100f, PreviousAnimation);
        AddControl("Play", -150f, 100f, PlaySelected);
        AddControl("Pause", -50f, 100f, PauseAnimation);
        AddControl("Stop", 50f, 100f, StopAnimation);
        AddControl("Next", 150f, 100f, NextAnimation);
        AddControl("1.0x", 250f, 100f, ChangeSpeed);
        AddControl("Loop", 150f, 190f, ToggleLoop);
    }

    private void AddControl(
        string label,
        float x,
        float y,
        Action callback)
    {
        ButtonHandle handle = ULTRADROIDControls.AddButton(
            new ButtonConfig
            {
                Id = "ULTRADROID.Blades.Animation." + label,
                Label = label,
                PositionOffset = new Vector2(x, y),
                ApplyTint = false,
                ScaleMultiplier = 0.65f,
                OnClick = callback
            });

        if (handle != null)
            _buttons.Add(handle);

        LogInfo("Requested control: " + label);
    }

    private void PlaySelected()
    {
        if (_player == null || _animations.Length == 0)
            return;

        if (_selectedIndex < 0 || _selectedIndex >= _animations.Length)
            _selectedIndex = 0;

        bool success = _player.Play(_selectedIndex, _loop);

        LogInfo(
            success
                ? "Playing: " + _animations[_selectedIndex].Name
                : "Could not play selected animation.");
    }

    private void PauseAnimation()
    {
        if (_player == null)
            return;

        if (_player.IsPaused)
        {
            _player.Resume();
            LogInfo("Resumed animation.");
        }
        else if (_player.IsPlaying)
        {
            _player.Pause();
            LogInfo("Paused animation.");
        }
        else
        {
            LogInfo("Animation is stopped. Tap Play to start.");
        }
    }

    private void StopAnimation()
    {
        if (_player == null)
            return;

        _player.Stop();
        LogInfo("Stopped: " + _player.CurrentAnimationName);
    }

    private void PreviousAnimation()
    {
        SelectRelative(-1);
    }

    private void NextAnimation()
    {
        SelectRelative(1);
    }

    private void SelectRelative(int direction)
    {
        if (_animations.Length == 0)
            return;

        _selectedIndex =
            (_selectedIndex + direction + _animations.Length)
            % _animations.Length;

        PlaySelected();
    }

    private void ChangeSpeed()
    {
        if (_player == null)
            return;

        float speed = _player.PlaybackSpeed + 0.25f;

        if (speed > 2f)
            speed = 0.25f;

        _player.PlaybackSpeed = speed;

        LogInfo("Playback speed: " + speed.ToString("0.00") + "x");
    }

    private void ToggleLoop()
    {
        _loop = !_loop;

        if (_player != null)
            _player.Loop = _loop;

        LogInfo("Loop: " + (_loop ? "ON" : "OFF"));
    }

    private static string FindModelFile()
    {
        List<string> candidates = new List<string>();

        try
        {
            string directory = Path.GetDirectoryName(
                typeof(BladesOfChaosPlugin).Assembly.Location);

            if (!string.IsNullOrEmpty(directory))
            {
                candidates.Add(Path.Combine(directory, ModelFileName));
                candidates.Add(Path.Combine(directory, "Assets", ModelFileName));
                candidates.Add(Path.Combine(directory, "assets", ModelFileName));
            }
        }
        catch
        {
        }

        try
        {
            candidates.Add(Path.Combine(Paths.PluginPath, ModelFileName));
            candidates.Add(Path.Combine(Paths.PluginPath, "BladesOfChaos", ModelFileName));
        }
        catch
        {
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
            }
        }

        return null;
    }

    private void RemoveButtons()
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            try
            {
                _buttons[i]?.Dispose();
            }
            catch
            {
            }
        }

        _buttons.Clear();
        _buttonsCreated = false;
    }

    private void ClearModel()
    {
        if (_modelRoot != null)
            UnityEngine.Object.Destroy(_modelRoot);

        _modelRoot = null;
        _player = null;
        _animations = new GlbAnimationData[0];
    }

    public void OnDestroy()
    {
        RemoveButtons();
        ClearModel();
    }

    private static void LogInfo(string message)
    {
        if (PluginLog != null)
            PluginLog.LogInfo("[Blades of Chaos] " + message);
    }

    private static void LogWarning(string message)
    {
        if (PluginLog != null)
            PluginLog.LogWarning("[Blades of Chaos] " + message);
    }

    private static void LogError(string message)
    {
        if (PluginLog != null)
            PluginLog.LogError("[Blades of Chaos] " + message);
    }
}
