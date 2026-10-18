using Il2CppInterop.Runtime.Injection;
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

[BepInPlugin(
    "ultradroid.bladesofchaos",
    "ULTRADROID Blades of Chaos",
    "0.8.1")]

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

    private GlbAnimationData[] _animations =
        new GlbAnimationData[0];

    private readonly List<ButtonHandle> _buttons =
        new List<ButtonHandle>();

    private string _lastScene = "";
    private float _nextRetry;
    private bool _buttonsCreated;
    private bool _loop = true;
    private int _selectedIndex;
    private float _playbackSpeed = 1f;

    public BladesOfChaosRunner(IntPtr ptr) : base(ptr)
    {
    }

    public void Update()
    {
        string scene = SceneManager.GetActiveScene().name;

        // Handle scene changes before trying to load the model.
        if (!string.Equals(scene, _lastScene, StringComparison.Ordinal))
        {
            _lastScene = scene;

            RemoveButtons();
            ClearModel();

            _nextRetry = 0f;

            LogInfo("Scene changed: " + scene);

            if (!IsMenu(scene))
            {
                // Controls must not depend on successful model loading.
                CreateButtons();
            }
        }

        if (IsMenu(scene))
            return;

        // Retry model loading independently of button registration.
        if (_modelRoot == null &&
            Time.unscaledTime >= _nextRetry)
        {
            _nextRetry = Time.unscaledTime + RetryInterval;
            TryLoadModel();
        }

        // GlbAnimationPlayer.Update already handles CPU skinning.
        if (_player != null)
        {
            _player.Update(Time.deltaTime);
        }
        else if (_modelRoot != null)
        {
            ModelAPI.UpdateCpuSkinning(_modelRoot);
        }
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
                if (cameras[i] != null &&
                    cameras[i].isActiveAndEnabled)
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

            _modelRoot.transform.localPosition =
                new Vector3(0f, 0f, 1f);

            _modelRoot.transform.localRotation =
                Quaternion.identity;

            _modelRoot.transform.localScale =
                new Vector3(0.1f, 0.1f, 0.1f);

            _player = ModelAPI.GetAnimationPlayer(_modelRoot);

            if (_player == null)
            {
                LogWarning(
                    "Model loaded, but no animation player was registered.");
                return;
            }

            _selectedIndex = FindPreferredClip("Bow");
            _loop = true;
            _playbackSpeed = 1f;

            _player.PlaybackSpeed = _playbackSpeed;
            _player.Loop = _loop;

            if (_animations.Length > 0)
                PlaySelected();

            LogInfo("Model and animation player initialized.");
        }
        catch (Exception ex)
        {
            LogError("Initialization failed: " + ex);

            ClearModel();
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
            {
                return i;
            }
        }

        return 0;
    }

    private void CreateButtons()
    {
        if (_buttonsCreated)
            return;

        _buttonsCreated = true;

        LogInfo("Registering animation controls.");

        // Register through the existing ULTRADROID Controls API.
        // Do not pass Action as a parameter to an injected MonoBehaviour method.

        try
        {
            ButtonHandle handle = ULTRADROIDControls.AddButton(new ButtonConfig
            {
                Id = "ULTRADROID.Blades.Animation.Prev",
                Label = "Prev",
                PositionOffset = new Vector2(120f, 90f),
                ApplyTint = false,
                ScaleMultiplier = 0.55f,
                OnClick = PreviousAnimation
            });

            if (handle != null) _buttons.Add(handle);
            LogInfo("Registered control: Prev");
        }
        catch (Exception ex) { LogError("Prev registration failed: " + ex); }

        try
        {
            ButtonHandle handle = ULTRADROIDControls.AddButton(new ButtonConfig
            {
                Id = "ULTRADROID.Blades.Animation.Play",
                Label = "Play",
                PositionOffset = new Vector2(185f, 90f),
                ApplyTint = false,
                ScaleMultiplier = 0.55f,
                OnClick = PlaySelected
            });

            if (handle != null) _buttons.Add(handle);
            LogInfo("Registered control: Play");
        }
        catch (Exception ex) { LogError("Play registration failed: " + ex); }

        try
        {
            ButtonHandle handle = ULTRADROIDControls.AddButton(new ButtonConfig
            {
                Id = "ULTRADROID.Blades.Animation.Pause",
                Label = "Pause",
                PositionOffset = new Vector2(250f, 90f),
                ApplyTint = false,
                ScaleMultiplier = 0.55f,
                OnClick = PauseAnimation
            });

            if (handle != null) _buttons.Add(handle);
            LogInfo("Registered control: Pause");
        }
        catch (Exception ex) { LogError("Pause registration failed: " + ex); }

        try
        {
            ButtonHandle handle = ULTRADROIDControls.AddButton(new ButtonConfig
            {
                Id = "ULTRADROID.Blades.Animation.Stop",
                Label = "Stop",
                PositionOffset = new Vector2(315f, 90f),
                ApplyTint = false,
                ScaleMultiplier = 0.55f,
                OnClick = StopAnimation
            });

            if (handle != null) _buttons.Add(handle);
            LogInfo("Registered control: Stop");
        }
        catch (Exception ex) { LogError("Stop registration failed: " + ex); }

        try
        {
            ButtonHandle handle = ULTRADROIDControls.AddButton(new ButtonConfig
            {
                Id = "ULTRADROID.Blades.Animation.Next",
                Label = "Next",
                PositionOffset = new Vector2(120f, 148f),
                ApplyTint = false,
                ScaleMultiplier = 0.55f,
                OnClick = NextAnimation
            });

            if (handle != null) _buttons.Add(handle);
            LogInfo("Registered control: Next");
        }
        catch (Exception ex) { LogError("Next registration failed: " + ex); }

        try
        {
            ButtonHandle handle = ULTRADROIDControls.AddButton(new ButtonConfig
            {
                Id = "ULTRADROID.Blades.Animation.Speed",
                Label = "Speed",
                PositionOffset = new Vector2(185f, 148f),
                ApplyTint = false,
                ScaleMultiplier = 0.55f,
                OnClick = ChangeSpeed
            });

            if (handle != null) _buttons.Add(handle);
            LogInfo("Registered control: Speed");
        }
        catch (Exception ex) { LogError("Speed registration failed: " + ex); }

        try
        {
            ButtonHandle handle = ULTRADROIDControls.AddButton(new ButtonConfig
            {
                Id = "ULTRADROID.Blades.Animation.Loop",
                Label = "Loop",
                PositionOffset = new Vector2(250f, 148f),
                ApplyTint = false,
                ScaleMultiplier = 0.55f,
                OnClick = ToggleLoop
            });

            if (handle != null) _buttons.Add(handle);
            LogInfo("Registered control: Loop");
        }
        catch (Exception ex) { LogError("Loop registration failed: " + ex); }
    }

    private void PlaySelected()
    {
        if (_player == null || _animations.Length == 0)
        {
            LogWarning("Play unavailable: animation player not ready.");
            return;
        }

        if (_selectedIndex < 0 ||
            _selectedIndex >= _animations.Length)
        {
            _selectedIndex = 0;
        }

        _player.PlaybackSpeed = _playbackSpeed;

        bool success = _player.Play(_selectedIndex, _loop);

        LogInfo(
            success
                ? "Playing: " + _animations[_selectedIndex].Name
                : "Could not play selected animation.");
    }

    private void PauseAnimation()
    {
        if (_player == null)
        {
            LogWarning("Pause unavailable: animation player not ready.");
            return;
        }

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
            LogInfo("Animation stopped. Tap Play to start.");
        }
    }

    private void StopAnimation()
    {
        if (_player == null)
        {
            LogWarning("Stop unavailable: animation player not ready.");
            return;
        }

        _player.Stop();
        LogInfo("Stopped animation.");
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
        {
            LogWarning("No animation clips available yet.");
            return;
        }

        _selectedIndex =
            (_selectedIndex + direction + _animations.Length)
            % _animations.Length;

        LogInfo(
            "Selected clip [" + _selectedIndex + "]: " +
            (_animations[_selectedIndex].Name ?? "(unnamed)"));

        PlaySelected();
    }

    private void ChangeSpeed()
    {
        if (_player == null)
        {
            LogWarning("Speed unavailable: animation player not ready.");
            return;
        }

        _playbackSpeed += 0.25f;

        if (_playbackSpeed > 2f)
            _playbackSpeed = 0.25f;

        _player.PlaybackSpeed = _playbackSpeed;

        LogInfo(
            "Playback speed: " +
            _playbackSpeed.ToString("0.00") + "x");
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
            candidates.Add(Path.Combine(
                Paths.PluginPath,
                "BladesOfChaos",
                ModelFileName));
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
            catch (Exception ex)
            {
                LogWarning("Button cleanup failed: " + ex.Message);
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
        _selectedIndex = 0;
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
