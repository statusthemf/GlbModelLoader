
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

[BepInPlugin("ultradroid.bladesofchaos", "ULTRADROID Blades of Chaos", "0.5.3")]
public sealed class BladesOfChaosPlugin : BasePlugin
{
    public override void Load()
    {
        BladesOfChaosRunner.PluginLog = Log;
        ClassInjector.RegisterTypeInIl2Cpp<BladesOfChaosRunner>();
        AddComponent<BladesOfChaosRunner>();
        Log.LogInfo("[Blades of Chaos] Plugin 0.5.3 loaded.");
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

    private readonly Dictionary<int, Transform> _nodeTransforms =
        new Dictionary<int, Transform>();

    private readonly HashSet<Transform> _mappedTransforms =
        new HashSet<Transform>();

    public BladesOfChaosRunner(IntPtr ptr) : base(ptr) { }

    public void Update()
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
                LogWarning(
                    "Could not find " + ModelFileName +
                    ". Put it beside the plugin DLL or in the plugin folder.");
                return;
            }

            Camera camera = FindGameCamera();

            if (camera == null)
            {
                LogInfo("Waiting for the gameplay camera...");
                return;
            }

            LogInfo("Loading model from: " + _modelPath);

            ModelLoadOptions options = new ModelLoadOptions
            {
                LoadTextures = true,
                FlipUvV = true
            };

            GameObject loaded = ModelAPI.LoadGLB(
                _modelPath,
                camera.transform,
                options);

            if (loaded == null)
            {
                LogError("ModelAPI.LoadGLB returned null.");
                return;
            }

            _modelRoot = loaded;
            _modelRoot.name = "ULTRADROID_BladesOfChaos";

            _modelRoot.transform.localPosition =
                new Vector3(0f, 0f, 1f);

            _modelRoot.transform.localRotation = Quaternion.identity;

            _modelRoot.transform.localScale =
                new Vector3(0.1f, 0.1f, 0.1f);

            LoadAnimationDataAndMapNodes();

            LogInfo(
                "Model loaded. Mapped glTF nodes: " +
                _nodeTransforms.Count);

            if (_activeAnimation != null)
            {
                LogInfo(
                    "Playing animation: " + _activeAnimation.Name +
                    " (" + _activeAnimation.Duration +
                    " seconds, looping).");
            }
            else
            {
                LogWarning(
                    "The model loaded, but no usable animation clip was found. " +
                    "The model will remain visible.");
            }
        }
        catch (Exception ex)
        {
            LogError("Failed to load or initialize the model: " + ex);
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
        List<string> candidates = new List<string>();

        try
        {
            string assemblyDirectory =
                Path.GetDirectoryName(
                    typeof(BladesOfChaosPlugin).Assembly.Location);

            if (!string.IsNullOrEmpty(assemblyDirectory))
            {
                candidates.Add(Path.Combine(
                    assemblyDirectory, ModelFileName));

                candidates.Add(Path.Combine(
                    assemblyDirectory, "assets", ModelFileName));

                candidates.Add(Path.Combine(
                    assemblyDirectory, "Assets", ModelFileName));
            }
        }
        catch { }

        try
        {
            candidates.Add(Path.Combine(
                Paths.PluginPath, ModelFileName));

            candidates.Add(Path.Combine(
                Paths.PluginPath, "BladesOfChaos", ModelFileName));

            candidates.Add(Path.Combine(
                Paths.PluginPath, "ULTRADROID.BladesOfChaos", ModelFileName));
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

        GlbAnimationData[] animations =
            ModelAPI.ReadGLBAnimations(_modelPath);

        if (animations != null)
        {
            for (int i = 0; i < animations.Length; i++)
            {
                GlbAnimationData clip = animations[i];

                if (clip == null)
                    continue;

                LogInfo(
                    "Animation clip " + i + ": " + clip.Name +
                    ", duration=" + clip.Duration +
                    "s, tracks=" +
                    (clip.Tracks == null ? 0 : clip.Tracks.Length));

                if (_activeAnimation == null &&
                    string.Equals(
                        clip.Name,
                        PreferredAnimationName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _activeAnimation = clip;
                }
            }

            if (_activeAnimation == null)
            {
                for (int i = 0; i < animations.Length; i++)
                {
                    if (animations[i] != null &&
                        animations[i].Tracks != null &&
                        animations[i].Tracks.Length > 0)
                    {
                        _activeAnimation = animations[i];
                        break;
                    }
                }
            }
        }

        GlbHierarchyData hierarchy = ReadHierarchy(_modelPath);

        if (hierarchy == null ||
            hierarchy.nodes == null ||
            _modelRoot == null)
        {
            LogWarning(
                "Could not read the GLB node hierarchy; " +
                "animation tracks cannot be attached to transforms.");
            return;
        }

        Transform[] allTransforms =
            _modelRoot.GetComponentsInChildren<Transform>(true);

        int sceneIndex = hierarchy.scene;
        int[] rootNodes = null;

        if (hierarchy.scenes != null &&
            hierarchy.scenes.Length > 0)
        {
            if (sceneIndex < 0 ||
                sceneIndex >= hierarchy.scenes.Length)
            {
                sceneIndex = 0;
            }

            if (hierarchy.scenes[sceneIndex] != null)
                rootNodes = hierarchy.scenes[sceneIndex].nodes;
        }

        if (rootNodes == null || rootNodes.Length == 0)
        {
            HashSet<int> childSet = new HashSet<int>();

            for (int i = 0; i < hierarchy.nodes.Length; i++)
            {
                GlbNodeData node = hierarchy.nodes[i];

                if (node == null || node.children == null)
                    continue;

                for (int c = 0; c < node.children.Length; c++)
                    childSet.Add(node.children[c]);
            }

            List<int> inferredRoots = new List<int>();

            for (int i = 0; i < hierarchy.nodes.Length; i++)
            {
                if (!childSet.Contains(i))
                    inferredRoots.Add(i);
            }

            rootNodes = inferredRoots.ToArray();
        }

        for (int i = 0; i < rootNodes.Length; i++)
        {
            MapNodeRecursive(
                rootNodes[i],
                _modelRoot.transform,
                hierarchy.nodes,
                allTransforms,
                0);
        }

        LogInfo(
            "Animation node map completed: " +
            _nodeTransforms.Count + " of " +
            hierarchy.nodes.Length + " glTF nodes mapped.");
    }

    private void MapNodeRecursive(
        int nodeIndex,
        Transform expectedParent,
        GlbNodeData[] nodes,
        Transform[] allTransforms,
        int depth)
    {
        if (nodeIndex < 0 ||
            nodeIndex >= nodes.Length ||
            depth > nodes.Length ||
            _nodeTransforms.ContainsKey(nodeIndex))
        {
            return;
        }

        GlbNodeData node = nodes[nodeIndex];

        if (node == null)
            return;

        Transform mapped = FindNodeTransform(
            nodeIndex,
            node,
            expectedParent,
            allTransforms);

        if (mapped == null)
        {
            LogDebug(
                "No Unity transform found for glTF node " +
                nodeIndex + " (" + (node.name ?? "unnamed") + ").");
            return;
        }

        _nodeTransforms[nodeIndex] = mapped;
        _mappedTransforms.Add(mapped);

        if (node.children == null)
            return;

        for (int i = 0; i < node.children.Length; i++)
        {
            MapNodeRecursive(
                node.children[i],
                mapped,
                nodes,
                allTransforms,
                depth + 1);
        }
    }

    private Transform FindNodeTransform(
        int nodeIndex,
        GlbNodeData node,
        Transform expectedParent,
        Transform[] allTransforms)
    {
        string generatedName = "GLTF_Node_" + nodeIndex;
        string alternateName = "Node_" + nodeIndex;

        if (expectedParent != null)
        {
            for (int i = 0; i < expectedParent.childCount; i++)
            {
                Transform child = expectedParent.GetChild(i);

                if (child == null ||
                    _mappedTransforms.Contains(child))
                {
                    continue;
                }

                if ((!string.IsNullOrEmpty(node.name) &&
                     child.name == node.name) ||
                    child.name == generatedName ||
                    child.name == alternateName)
                {
                    return child;
                }
            }
        }

        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform candidate = allTransforms[i];

            if (candidate == null ||
                candidate == _modelRoot.transform ||
                _mappedTransforms.Contains(candidate))
            {
                continue;
            }

            if ((!string.IsNullOrEmpty(node.name) &&
                 candidate.name == node.name) ||
                candidate.name == generatedName ||
                candidate.name == alternateName)
            {
                return candidate;
            }
        }

        return null;
    }

    private void UpdateAnimation(float deltaTime)
    {
        if (_activeAnimation == null ||
            _activeAnimation.Tracks == null)
        {
            return;
        }

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

            if (!_nodeTransforms.TryGetValue(
                    track.TargetNode, out target) ||
                target == null)
            {
                continue;
            }

            if (track.Path == "weights")
                continue;

            if (string.Equals(
                track.Interpolation,
                "CUBICSPLINE",
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (track.Path == "translation")
            {
                float[] v = new float[3];

                if (TrySample(track, time, 3, v))
                {
                    target.localPosition =
                        new Vector3(v[0], v[1], v[2]);
                }
            }
            else if (track.Path == "scale")
            {
                float[] v = new float[3];

                if (TrySample(track, time, 3, v))
                {
                    target.localScale =
                        new Vector3(v[0], v[1], v[2]);
                }
            }
            else if (track.Path == "rotation")
            {
                float[] v = new float[4];

                if (TrySample(track, time, 4, v))
                {
                    target.localRotation =
                        new Quaternion(v[0], v[1], v[2], v[3]).normalized;
                }
            }
        }
    }

    private static float FindAnimationDuration(
        GlbAnimationData animation)
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

    private static bool TrySample(
        GlbAnimationTrack track,
        float time,
        int components,
        float[] output)
    {
        if (track == null ||
            track.Times == null ||
            track.Values == null ||
            track.Times.Length == 0 ||
            output == null ||
            output.Length < components)
        {
            return false;
        }

        int valueStride = components;

        // CUBICSPLINE stores three value entries per keyframe
        // (incoming tangent, value, outgoing tangent). It is skipped
        // by UpdateAnimation, but this guard prevents invalid indexing.
        if (string.Equals(
            track.Interpolation,
            "CUBICSPLINE",
            StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (track.Values.Length < track.Times.Length * valueStride)
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

        if (string.Equals(
            interpolation,
            "STEP",
            StringComparison.OrdinalIgnoreCase))
        {
            CopyKey(values, left, components, output);
            return true;
        }

        float span = times[right] - times[left];
        float alpha = span <= 0.000001f
            ? 0f
            : (time - times[left]) / span;

        int leftOffset = left * components;
        int rightOffset = right * components;

        if (components == 4 &&
            string.Equals(
                track.Path,
                "rotation",
                StringComparison.OrdinalIgnoreCase))
        {
            Quaternion a = new Quaternion(
                values[leftOffset],
                values[leftOffset + 1],
                values[leftOffset + 2],
                values[leftOffset + 3]).normalized;

            Quaternion b = new Quaternion(
                values[rightOffset],
                values[rightOffset + 1],
                values[rightOffset + 2],
                values[rightOffset + 3]).normalized;

            Quaternion q = Quaternion.SlerpUnclamped(a, b, alpha).normalized;

            output[0] = q.x;
            output[1] = q.y;
            output[2] = q.z;
            output[3] = q.w;
        }
        else
        {
            for (int i = 0; i < components; i++)
            {
                output[i] = Mathf.LerpUnclamped(
                    values[leftOffset + i],
                    values[rightOffset + i],
                    alpha);
            }
        }

        return true;
    }

    private static void CopyKey(
        float[] values,
        int keyIndex,
        int components,
        float[] output)
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

        return GlbHierarchyParser.Parse(json);
    }

    private static string ReadGlbJsonChunk(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        if (bytes.Length < 20)
            throw new InvalidDataException(
                "File is too short to be a valid GLB.");

        int offset = 12;
        const uint JsonChunkType = 0x4E4F534A;

        while (offset + 8 <= bytes.Length)
        {
            int chunkLength = BitConverter.ToInt32(bytes, offset);
            uint chunkType = BitConverter.ToUInt32(bytes, offset + 4);

            offset += 8;

            if (chunkLength < 0 ||
                (long)offset + chunkLength > bytes.Length)
            {
                throw new InvalidDataException(
                    "Invalid GLB chunk length.");
            }

            if (chunkType == JsonChunkType)
            {
                return Encoding.UTF8.GetString(
                    bytes, offset, chunkLength)
                    .Trim('\0', ' ', '\r', '\n', '\t');
            }

            offset += chunkLength;
        }

        throw new InvalidDataException(
            "No JSON chunk was found in the GLB file.");
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

// Small JSON reader for the hierarchy fields only.
// Avoids UnityEngine.JsonUtility and does not touch the existing GLB loader.
public static class GlbHierarchyParser
{
    public static GlbHierarchyData Parse(string json)
    {
        object rootValue = new JsonReader(json).ReadValue();
        Dictionary<string, object> root = AsObject(rootValue);

        GlbHierarchyData result = new GlbHierarchyData();
        result.scene = GetInt(root, "scene", 0);

        object scenesValue;
        if (root.TryGetValue("scenes", out scenesValue))
        {
            List<object> rawScenes = AsArray(scenesValue);
            result.scenes = new GlbSceneData[rawScenes.Count];

            for (int i = 0; i < rawScenes.Count; i++)
            {
                Dictionary<string, object> sceneObject =
                    AsObject(rawScenes[i]);

                result.scenes[i] = new GlbSceneData
                {
                    nodes = GetIntArray(sceneObject, "nodes")
                };
            }
        }

        object nodesValue;
        if (root.TryGetValue("nodes", out nodesValue))
        {
            List<object> rawNodes = AsArray(nodesValue);
            result.nodes = new GlbNodeData[rawNodes.Count];

            for (int i = 0; i < rawNodes.Count; i++)
            {
                Dictionary<string, object> nodeObject =
                    AsObject(rawNodes[i]);

                object nameValue;
                string name = nodeObject.TryGetValue("name", out nameValue)
                    ? nameValue as string
                    : null;

                result.nodes[i] = new GlbNodeData
                {
                    name = name,
                    children = GetIntArray(nodeObject, "children")
                };
            }
        }

        return result;
    }

    private static int GetInt(
        Dictionary<string, object> obj,
        string key,
        int fallback)
    {
        object value;

        if (obj.TryGetValue(key, out value) && value is double)
            return (int)(double)value;

        return fallback;
    }

    private static int[] GetIntArray(
        Dictionary<string, object> obj,
        string key)
    {
        object value;

        if (!obj.TryGetValue(key, out value) || value == null)
            return new int[0];

        List<object> array = AsArray(value);
        int[] result = new int[array.Count];

        for (int i = 0; i < array.Count; i++)
        {
            if (!(array[i] is double))
                throw new InvalidDataException(
                    "Expected an integer in JSON array '" + key + "'.");

            result[i] = (int)(double)array[i];
        }

        return result;
    }

    private static Dictionary<string, object> AsObject(object value)
    {
        Dictionary<string, object> result =
            value as Dictionary<string, object>;

        if (result == null)
            throw new InvalidDataException("Expected a JSON object.");

        return result;
    }

    private static List<object> AsArray(object value)
    {
        List<object> result = value as List<object>;

        if (result == null)
            throw new InvalidDataException("Expected a JSON array.");

        return result;
    }

    private sealed class JsonReader
    {
        private readonly string _json;
        private int _position;

        public JsonReader(string json)
        {
            _json = json ?? throw new ArgumentNullException("json");
        }

        public object ReadValue()
        {
            SkipWhitespace();

            if (_position >= _json.Length)
                throw Error("Unexpected end of JSON.");

            char c = _json[_position];

            if (c == '{')
                return ReadObject();

            if (c == '[')
                return ReadArray();

            if (c == '"')
                return ReadString();

            if (c == 't')
            {
                ReadLiteral("true");
                return true;
            }

            if (c == 'f')
            {
                ReadLiteral("false");
                return false;
            }

            if (c == 'n')
            {
                ReadLiteral("null");
                return null;
            }

            if (c == '-' || (c >= '0' && c <= '9'))
                return ReadNumber();

            throw Error("Unexpected character.");
        }

        private Dictionary<string, object> ReadObject()
        {
            Dictionary<string, object> result =
                new Dictionary<string, object>();

            Expect('{');
            SkipWhitespace();

            if (TryConsume('}'))
                return result;

            while (true)
            {
                SkipWhitespace();

                if (_position >= _json.Length ||
                    _json[_position] != '"')
                {
                    throw Error("Expected an object key.");
                }

                string key = ReadString();

                SkipWhitespace();
                Expect(':');

                object value = ReadValue();
                result[key] = value;

                SkipWhitespace();

                if (TryConsume('}'))
                    break;

                Expect(',');
            }

            return result;
        }

        private List<object> ReadArray()
        {
            List<object> result = new List<object>();

            Expect('[');
            SkipWhitespace();

            if (TryConsume(']'))
                return result;

            while (true)
            {
                result.Add(ReadValue());
                SkipWhitespace();

                if (TryConsume(']'))
                    break;

                Expect(',');
            }

            return result;
        }

        private string ReadString()
        {
            Expect('"');
            StringBuilder result = new StringBuilder();

            while (_position < _json.Length)
            {
                char c = _json[_position++];

                if (c == '"')
                    return result.ToString();

                if (c != '\\')
                {
                    result.Append(c);
                    continue;
                }

                if (_position >= _json.Length)
                    throw Error("Incomplete string escape.");

                char escape = _json[_position++];

                switch (escape)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;

                    case 'u':
                        if (_position + 4 > _json.Length)
                            throw Error("Incomplete Unicode escape.");

                        string hex = _json.Substring(_position, 4);
                        int codePoint;

                        if (!int.TryParse(
                            hex,
                            System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out codePoint))
                        {
                            throw Error("Invalid Unicode escape.");
                        }

                        result.Append((char)codePoint);
                        _position += 4;
                        break;

                    default:
                        throw Error("Unknown string escape.");
                }
            }

            throw Error("Unterminated JSON string.");
        }

        private double ReadNumber()
        {
            int start = _position;

            if (Peek('-'))
                _position++;

            while (_position < _json.Length &&
                   char.IsDigit(_json[_position]))
            {
                _position++;
            }

            if (Peek('.'))
            {
                _position++;

                while (_position < _json.Length &&
                       char.IsDigit(_json[_position]))
                {
                    _position++;
                }
            }

            if (Peek('e') || Peek('E'))
            {
                _position++;

                if (Peek('+') || Peek('-'))
                    _position++;

                while (_position < _json.Length &&
                       char.IsDigit(_json[_position]))
                {
                    _position++;
                }
            }

            string number = _json.Substring(
                start, _position - start);

            double value;

            if (!double.TryParse(
                number,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value))
            {
                throw Error("Invalid JSON number.");
            }

            return value;
        }

        private void ReadLiteral(string literal)
        {
            if (_position + literal.Length > _json.Length ||
                string.Compare(
                    _json,
                    _position,
                    literal,
                    0,
                    literal.Length,
                    StringComparison.Ordinal) != 0)
            {
                throw Error("Invalid JSON literal.");
            }

            _position += literal.Length;
        }

        private void SkipWhitespace()
        {
            while (_position < _json.Length &&
                   char.IsWhiteSpace(_json[_position]))
            {
                _position++;
            }
        }

        private bool Peek(char c)
        {
            return _position < _json.Length &&
                   _json[_position] == c;
        }

        private bool TryConsume(char c)
        {
            if (!Peek(c))
                return false;

            _position++;
            return true;
        }

        private void Expect(char c)
        {
            SkipWhitespace();

            if (!TryConsume(c))
                throw Error("Expected '" + c + "'.");
        }

        private InvalidDataException Error(string message)
        {
            return new InvalidDataException(
                message + " Position: " + _position + ".");
        }
    }
}
