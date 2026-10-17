using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.SceneManagement;
using ULTRADROID.Models;

[BepInPlugin("ultradroid.bladesofchaos", "ULTRADROID Blades of Chaos", "0.5.2")]
public sealed class BladesOfChaosPlugin : BasePlugin
{
    public override void Load()
    {
        BladesOfChaosRunner.PluginLog = Log;
        ClassInjector.RegisterTypeInIl2Cpp<BladesOfChaosRunner>();
        AddComponent<BladesOfChaosRunner>();
        Log.LogInfo("[Blades of Chaos] Plugin 0.5.2 loaded.");
    }
}

public sealed class BladesOfChaosRunner : MonoBehaviour
{
    public static ManualLogSource PluginLog;

    private const string ModelFileName = "blades_of_chaos_god.glb";
    private const string PreferredAnimationName = "Bow";

    private GameObject _modelRoot;
    private string _modelPath;
    private string _lastSceneName = "";
    private float _nextLoadAttempt;
    private float _animationTime;
    private GlbAnimationData _activeAnimation;
    private readonly Dictionary<int, Transform> _nodeTransforms = new Dictionary<int, Transform>();
    private readonly HashSet<Transform> _mappedTransforms = new HashSet<Transform>();

    public BladesOfChaosRunner(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        string sceneName = SceneManager.GetActiveScene().name;

        if (!string.Equals(sceneName, _lastSceneName, StringComparison.Ordinal))
        {
            _lastSceneName = sceneName;
            ClearLoadedModel();
            _nextLoadAttempt = 0f;
            LogInfo("Scene changed to: " + sceneName);
        }

        if (IsNonGameplayScene(sceneName))
            return;

        if (_modelRoot == null)
        {
            if (Time.unscaledTime >= _nextLoadAttempt)
            {
                _nextLoadAttempt = Time.unscaledTime + 2f;
                TryLoadModel();
            }
            return;
        }

        if (_activeAnimation != null)
            UpdateAnimation(Time.deltaTime);
    }

    private static bool IsNonGameplayScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
            return true;

        string s = sceneName.ToLowerInvariant();
        return s.Contains("bootstrap") ||
               s == "intro" ||
               s.Contains("mainmenu") ||
               s.Contains("main_menu") ||
               s.Contains("main menu") ||
               s.Contains("title") ||
               s.Contains("loading") ||
               s.Contains("credits") ||
               s.Contains("ending");
    }

    private void TryLoadModel()
    {
        try
        {
            _modelPath = FindModelFile();
            if (string.IsNullOrEmpty(_modelPath))
            {
                LogWarning("Could not find " + ModelFileName + ". Put it beside the plugin DLL or in the plugin folder.");
                return;
            }

            Camera camera = FindGameCamera();
            if (camera == null)
            {
                LogInfo("Waiting for the gameplay camera...");
                return;
            }

            LogInfo("Loading model from: " + _modelPath);

            // Keep the working appearance and placement settings.
            ModelLoadOptions options = new ModelLoadOptions
            {
                LoadTextures = true,
                FlipUvV = true
            };

            GameObject loaded = ModelAPI.LoadGLB(_modelPath, camera.transform, options);
            if (loaded == null)
            {
                LogError("ModelAPI.LoadGLB returned null.");
                return;
            }

            _modelRoot = loaded;
            _modelRoot.name = "ULTRADROID_BladesOfChaos";
            _modelRoot.transform.localPosition = new Vector3(0.05f, -0.65f, 1.0f);
            _modelRoot.transform.localRotation = Quaternion.identity;
            _modelRoot.transform.localScale = new Vector3(0.03f, 0.03f, 0.03f);

            LoadAnimationDataAndMapNodes();

            LogInfo("Model loaded. Mapped glTF nodes: " + _nodeTransforms.Count);
            if (_activeAnimation != null)
            {
                LogInfo("Playing animation: " + _activeAnimation.Name + " (" + _activeAnimation.Duration + " seconds, looping).");
            }
            else
            {
                LogWarning("The model loaded, but no usable animation clip was found. The model will remain visible.");
            }
        }
        catch (Exception ex)
        {
            LogError("Failed to load or initialize the Blades of Chaos model: " + ex);
            ClearLoadedModel();
        }
    }

    private static Camera FindGameCamera()
    {
        Camera main = Camera.main;
        if (main != null && main.isActiveAndEnabled)
            return main;

        Camera[] cameras = Camera.allCameras;
        if (cameras != null)
        {
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera candidate = cameras[i];
                if (candidate != null && candidate.isActiveAndEnabled)
                    return candidate;
            }
        }

        return null;
    }

    private string FindModelFile()
    {
        var candidates = new List<string>();

        try
        {
            string assemblyDirectory = Path.GetDirectoryName(typeof(BladesOfChaosPlugin).Assembly.Location);
            if (!string.IsNullOrEmpty(assemblyDirectory))
            {
                candidates.Add(Path.Combine(assemblyDirectory, ModelFileName));
                candidates.Add(Path.Combine(assemblyDirectory, "assets", ModelFileName));
                candidates.Add(Path.Combine(assemblyDirectory, "Assets", ModelFileName));
            }
        }
        catch { }

        try
        {
            candidates.Add(Path.Combine(Paths.PluginPath, ModelFileName));
            candidates.Add(Path.Combine(Paths.PluginPath, "BladesOfChaos", ModelFileName));
            candidates.Add(Path.Combine(Paths.PluginPath, "ULTRADROID.BladesOfChaos", ModelFileName));
        }
        catch { }

        for (int i = 0; i < candidates.Count; i++)
        {
            try
            {
                if (File.Exists(candidates[i]))
                    return candidates[i];
            }
            catch { }
        }

        return null;
    }

    private void LoadAnimationDataAndMapNodes()
    {
        _nodeTransforms.Clear();
        _mappedTransforms.Clear();
        _activeAnimation = null;
        _animationTime = 0f;

        // The API already reads animation channels and their keyframe values.
        GlbAnimationData[] animations = ModelAPI.ReadGLBAnimations(_modelPath);
        if (animations != null && animations.Length > 0)
        {
            for (int i = 0; i < animations.Length; i++)
            {
                GlbAnimationData clip = animations[i];
                if (clip == null)
                    continue;

                LogInfo("Animation clip " + i + ": " + clip.Name + ", duration=" + clip.Duration + "s, tracks=" +
                        (clip.Tracks == null ? 0 : clip.Tracks.Length));

                if (_activeAnimation == null &&
                    string.Equals(clip.Name, PreferredAnimationName, StringComparison.OrdinalIgnoreCase))
                {
                    _activeAnimation = clip;
                }
            }

            if (_activeAnimation == null)
            {
                for (int i = 0; i < animations.Length; i++)
                {
                    if (animations[i] != null && animations[i].Tracks != null && animations[i].Tracks.Length > 0)
                    {
                        _activeAnimation = animations[i];
                        break;
                    }
                }
            }
        }

        // Read only the node hierarchy from the GLB JSON chunk. This is deliberately
        // separate from the existing loader, so ModelAPI.cs and the working mesh loader
        // do not need to be replaced.
        GlbHierarchyData hierarchy = ReadHierarchy(_modelPath);
        if (hierarchy == null || hierarchy.nodes == null || _modelRoot == null)
        {
            LogWarning("Could not read the GLB node hierarchy; animation tracks cannot be attached to transforms.");
            return;
        }

        Transform[] allTransforms = _modelRoot.GetComponentsInChildren<Transform>(true);
        int sceneIndex = hierarchy.scene;
        int[] rootNodes = null;

        if (hierarchy.scenes != null && hierarchy.scenes.Length > 0)
        {
            if (sceneIndex < 0 || sceneIndex >= hierarchy.scenes.Length)
                sceneIndex = 0;

            if (hierarchy.scenes[sceneIndex] != null)
                rootNodes = hierarchy.scenes[sceneIndex].nodes;
        }

        if (rootNodes == null || rootNodes.Length == 0)
        {
            // Fallback for GLBs whose scene declaration is missing or unusual:
            // infer top-level nodes as nodes not listed as somebody else's child.
            var childSet = new HashSet<int>();
            for (int i = 0; i < hierarchy.nodes.Length; i++)
            {
                GlbNodeData node = hierarchy.nodes[i];
                if (node != null && node.children != null)
                {
                    for (int c = 0; c < node.children.Length; c++)
                        childSet.Add(node.children[c]);
                }
            }

            var inferredRoots = new List<int>();
            for (int i = 0; i < hierarchy.nodes.Length; i++)
            {
                if (!childSet.Contains(i))
                    inferredRoots.Add(i);
            }
            rootNodes = inferredRoots.ToArray();
        }

        for (int i = 0; i < rootNodes.Length; i++)
            MapNodeRecursive(rootNodes[i], _modelRoot.transform, hierarchy.nodes, allTransforms, 0);

        LogInfo("Animation node map completed: " + _nodeTransforms.Count + " of " + hierarchy.nodes.Length + " glTF nodes mapped.");
    }

    private void MapNodeRecursive(int nodeIndex, Transform expectedParent, GlbNodeData[] nodes, Transform[] allTransforms, int depth)
    {
        if (nodeIndex < 0 || nodeIndex >= nodes.Length || depth > nodes.Length)
            return;
        if (_nodeTransforms.ContainsKey(nodeIndex))
            return;

        GlbNodeData node = nodes[nodeIndex];
        if (node == null)
            return;

        Transform mapped = FindNodeTransform(nodeIndex, node, expectedParent, allTransforms);
        if (mapped == null)
        {
            LogDebug("No Unity transform found for glTF node " + nodeIndex + " (" + (node.name ?? "unnamed") + ").");
            return;
        }

        _nodeTransforms[nodeIndex] = mapped;
        _mappedTransforms.Add(mapped);

        if (node.children != null)
        {
            for (int i = 0; i < node.children.Length; i++)
                MapNodeRecursive(node.children[i], mapped, nodes, allTransforms, depth + 1);
        }
    }

    private Transform FindNodeTransform(int nodeIndex, GlbNodeData node, Transform expectedParent, Transform[] allTransforms)
    {
        string generatedName = "GLTF_Node_" + nodeIndex;
        string alternateName = "Node_" + nodeIndex;

        // Match direct children first so duplicate node names in different branches resolve correctly.
        if (expectedParent != null)
        {
            for (int i = 0; i < expectedParent.childCount; i++)
            {
                Transform child = expectedParent.GetChild(i);
                if (child == null || _mappedTransforms.Contains(child))
                    continue;

                if ((!string.IsNullOrEmpty(node.name) && child.name == node.name) ||
                    child.name == generatedName || child.name == alternateName)
                    return child;
            }
        }

        // Some loader versions insert an extra scene wrapper. Search the whole loaded
        // hierarchy as a fallback, preferring the original glTF node name or indexed name.
        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform candidate = allTransforms[i];
            if (candidate == null || candidate == _modelRoot.transform || _mappedTransforms.Contains(candidate))
                continue;

            if ((!string.IsNullOrEmpty(node.name) && candidate.name == node.name) ||
                candidate.name == generatedName || candidate.name == alternateName)
                return candidate;
        }

        return null;
    }

    private void UpdateAnimation(float deltaTime)
    {
        if (_activeAnimation == null || _activeAnimation.Tracks == null)
            return;

        float duration = _activeAnimation.Duration;
        if (duration <= 0f)
            duration = FindAnimationDuration(_activeAnimation);
        if (duration <= 0f)
            return;

        _animationTime += Mathf.Max(0f, deltaTime);
        float time = _animationTime % duration;

        GlbAnimationTrack[] tracks = _activeAnimation.Tracks;
        for (int i = 0; i < tracks.Length; i++)
        {
            GlbAnimationTrack track = tracks[i];
            if (track == null)
                continue;

            Transform target;
            if (!_nodeTransforms.TryGetValue(track.TargetNode, out target) || target == null)
                continue;

            string path = track.Path;
            if (path == "weights")
                continue; // Morph targets need separate mesh support.

            if (string.Equals(track.Interpolation, "CUBICSPLINE", StringComparison.OrdinalIgnoreCase))
                continue; // These clips use STEP/LINEAR; cubic Hermite playback is not implemented here.

            if (path == "translation")
            {
                float[] v = new float[3];
                if (TrySample(track, time, 3, v))
                    target.localPosition = new Vector3(v[0], v[1], v[2]);
            }
            else if (path == "scale")
            {
                float[] v = new float[3];
                if (TrySample(track, time, 3, v))
                    target.localScale = new Vector3(v[0], v[1], v[2]);
            }
            else if (path == "rotation")
            {
                float[] v = new float[4];
                if (TrySample(track, time, 4, v))
                {
                    Quaternion q = new Quaternion(v[0], v[1], v[2], v[3]);
                    target.localRotation = q.normalized;
                }
            }
        }
    }

    private static float FindAnimationDuration(GlbAnimationData animation)
    {
        float max = 0f;
        if (animation == null || animation.Tracks == null)
            return max;

        for (int i = 0; i < animation.Tracks.Length; i++)
        {
            GlbAnimationTrack track = animation.Tracks[i];
            if (track == null || track.Times == null)
                continue;
            for (int k = 0; k < track.Times.Length; k++)
            {
                if (track.Times[k] > max)
                    max = track.Times[k];
            }
        }
        return max;
    }

    private static bool TrySample(GlbAnimationTrack track, float time, int components, float[] output)
    {
        if (track == null || track.Times == null || track.Values == null || track.Times.Length == 0)
            return false;
        if (track.Values.Length < track.Times.Length * components || output == null || output.Length < components)
            return false;

        float[] times = track.Times;
        float[] values = track.Values;
        int last = times.Length - 1;

        if (time <= times[0])
        {
            CopyKey(values, 0, components, output);
            return true;
        }
        if (time >= times[last])
        {
            CopyKey(values, last, components, output);
            return true;
        }

        // Binary search for adjacent keyframes times[left] <= time < times[right].
        int low = 0;
        int high = last;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (times[mid] <= time)
                low = mid;
            else
                high = mid;
        }

        int left = low;
        int right = high;
        string interpolation = track.Interpolation ?? "LINEAR";
        if (string.Equals(interpolation, "STEP", StringComparison.OrdinalIgnoreCase))
        {
            CopyKey(values, left, components, output);
            return true;
        }

        float span = times[right] - times[left];
        float alpha = span <= 0.000001f ? 0f : (time - times[left]) / span;
        int leftOffset = left * components;
        int rightOffset = right * components;

        for (int i = 0; i < components; i++)
            output[i] = Mathf.LerpUnclamped(values[leftOffset + i], values[rightOffset + i], alpha);

        // Quaternion channels need spherical interpolation rather than component-wise lerp.
        if (components == 4 && string.Equals(track.Path, "rotation", StringComparison.OrdinalIgnoreCase))
        {
            Quaternion a = new Quaternion(values[leftOffset], values[leftOffset + 1], values[leftOffset + 2], values[leftOffset + 3]).normalized;
            Quaternion b = new Quaternion(values[rightOffset], values[rightOffset + 1], values[rightOffset + 2], values[rightOffset + 3]).normalized;
            Quaternion q = Quaternion.SlerpUnclamped(a, b, alpha).normalized;
            output[0] = q.x;
            output[1] = q.y;
            output[2] = q.z;
            output[3] = q.w;
        }

        return true;
    }

    private static void CopyKey(float[] values, int keyIndex, int components, float[] output)
    {
        int offset = keyIndex * components;
        for (int i = 0; i < components; i++)
            output[i] = values[offset + i];
    }

    private GlbHierarchyData ReadHierarchy(string path)
    {
        string json = ReadGlbJsonChunk(path);
        if (string.IsNullOrEmpty(json))
            throw new InvalidDataException("GLB JSON chunk is empty.");

        GlbHierarchyData parsed = JsonUtility.FromJson<GlbHierarchyData>(json);
        if (parsed == null)
            throw new InvalidDataException("Unity could not parse the GLB hierarchy JSON.");
        return parsed;
    }

    private static string ReadGlbJsonChunk(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 20)
            throw new InvalidDataException("File is too short to be a valid GLB.");

        // GLB chunk layout: 12-byte header, followed by (uint32 length, uint32 type, data).
        int offset = 12;
        const uint JsonChunkType = 0x4E4F534A; // ASCII JSON in little-endian form.

        while (offset + 8 <= bytes.Length)
        {
            int chunkLength = BitConverter.ToInt32(bytes, offset);
            uint chunkType = BitConverter.ToUInt32(bytes, offset + 4);
            offset += 8;

            if (chunkLength < 0 || offset + chunkLength > bytes.Length)
                throw new InvalidDataException("Invalid GLB chunk length.");

            if (chunkType == JsonChunkType)
            {
                return Encoding.UTF8.GetString(bytes, offset, chunkLength)
                    .Trim('\0', ' ', '\r', '\n', '\t');
            }

            offset += chunkLength;
        }

        throw new InvalidDataException("No JSON chunk was found in the GLB file.");
    }

    private void ClearLoadedModel()
    {
        if (_modelRoot != null)
            UnityEngine.Object.Destroy(_modelRoot);

        _modelRoot = null;
        _activeAnimation = null;
        _animationTime = 0f;
        _nodeTransforms.Clear();
        _mappedTransforms.Clear();
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

    private static void LogDebug(string message)
    {
        if (PluginLog != null)
            PluginLog.LogDebug("[Blades of Chaos] " + message);
    }
}

[Serializable]
public sealed class GlbHierarchyData
{
    public int scene;
    public GlbSceneData[] scenes;
    public GlbNodeData[] nodes;
}

[Serializable]
public sealed class GlbSceneData
{
    public int[] nodes;
}

[Serializable]
public sealed class GlbNodeData
{
    public string name;
    public int[] children;
}
