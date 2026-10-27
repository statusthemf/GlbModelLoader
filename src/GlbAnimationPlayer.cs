
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ULTRADROID.Models
{
    public sealed class GlbAnimationPlayer
    {
        private sealed class CachedTrack
        {
            public Transform Target;
            public string Path;
            public bool Cubic;
            public bool Step;
            public int Components;
            public float[] Times;
            public float[] Values;
        }

        private readonly GameObject _root;
        private readonly GlbAnimationData[] _animations;
        private readonly Transform[] _nodeTransforms;

        private readonly Dictionary<object, CachedTrack[]> _trackCache =
            new Dictionary<object, CachedTrack[]>();

        private readonly Dictionary<object, float> _durationCache =
            new Dictionary<object, float>();

        private object _currentAnimation;
        private CachedTrack[] _currentTracks;
        private float[] _sampleBuffer = new float[4];
        private float _currentDuration;
        private float _time;
        private bool _playing;
        private bool _paused;

        public bool IsPlaying
        {
            get { return _playing && !_paused; }
        }

        public bool IsPaused
        {
            get { return _paused; }
        }

        public float PlaybackSpeed { get; set; } = 1f;
        public bool Loop { get; set; } = true;

        public string CurrentAnimationName
        {
            get
            {
                return GetString(
                    _currentAnimation,
                    "Name", "name",
                    "AnimationName", "animationName");
            }
        }

        public float Duration
        {
            get { return _currentDuration; }
        }

        public float Time
        {
            get { return _time; }
        }

        public GlbAnimationPlayer(
            GameObject root,
            GlbAnimationData[] animations,
            Transform[] nodeTransforms)
        {
            _root = root;
            _animations = animations ?? new GlbAnimationData[0];
            _nodeTransforms = nodeTransforms ?? new Transform[0];

            _currentAnimation = null;
            _currentTracks = null;
            _currentDuration = 0f;
            _time = 0f;
            _playing = false;
            _paused = false;
            PlaybackSpeed = 1f;
            Loop = true;
        }

        public bool Play(string clipName, bool loop = true)
        {
            if (string.IsNullOrEmpty(clipName))
                return false;

            for (int i = 0; i < _animations.Length; i++)
            {
                object animation = _animations[i];

                string name = GetString(
                    animation,
                    "Name", "name",
                    "AnimationName", "animationName");

                if (string.Equals(
                    name,
                    clipName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return PlayAnimation(animation, loop);
                }
            }

            return false;
        }

        public bool Play(int animationIndex, bool loop = true)
        {
            if (animationIndex < 0 ||
                animationIndex >= _animations.Length)
                return false;

            return PlayAnimation(_animations[animationIndex], loop);
        }

        private bool PlayAnimation(object animation, bool loop)
        {
            if (animation == null)
                return false;

            CachedTrack[] tracks;

            if (!_trackCache.TryGetValue(animation, out tracks))
            {
                tracks = BuildTrackCache(animation);
                _trackCache[animation] = tracks;
            }

            if (tracks == null || tracks.Length == 0)
                return false;

            _currentAnimation = animation;
            _currentTracks = tracks;

            if (!_durationCache.TryGetValue(
                animation, out _currentDuration))
            {
                _currentDuration = CalculateDuration(_currentTracks);
                _durationCache[animation] = _currentDuration;
            }

            int requiredComponents = 4;

            for (int i = 0; i < _currentTracks.Length; i++)
            {
                if (_currentTracks[i].Components > requiredComponents)
                    requiredComponents = _currentTracks[i].Components;
            }

            if (_sampleBuffer.Length < requiredComponents)
                _sampleBuffer = new float[requiredComponents];

            _time = 0f;
            _playing = true;
            _paused = false;
            Loop = loop;

            SampleAnimation(0f);
            UpdateSkinning();

            return true;
        }

        public void Pause()
        {
            if (_playing)
                _paused = true;
        }

        public void Resume()
        {
            if (_playing)
                _paused = false;
        }

        public void Stop()
        {
            _playing = false;
            _paused = false;
            _time = 0f;

            if (_currentAnimation != null)
            {
                SampleAnimation(0f);
                UpdateSkinning();
            }
        }

        public void Update(float deltaTime)
        {
            if (!_playing || _paused || _currentAnimation == null)
                return;

            if (_root == null)
            {
                _playing = false;
                return;
            }

            float duration = _currentDuration;

            if (duration <= 0f)
            {
                SampleAnimation(0f);
                UpdateSkinning();

                if (!Loop)
                    _playing = false;

                return;
            }

            _time += deltaTime * PlaybackSpeed;

            if (Loop)
            {
                _time %= duration;

                if (_time < 0f)
                    _time += duration;
            }
            else
            {
                if (PlaybackSpeed >= 0f && _time >= duration)
                {
                    _time = duration;
                    _playing = false;
                }
                else if (PlaybackSpeed < 0f && _time <= 0f)
                {
                    _time = 0f;
                    _playing = false;
                }
            }

            _time = Mathf.Clamp(_time, 0f, duration);

            SampleAnimation(_time);
            UpdateSkinning();
        }

        private void UpdateSkinning()
        {
            if (_root == null)
                return;

            try
            {
                ModelAPI.UpdateCpuSkinning(_root);
            }
            catch
            {
                // Models without CPU-skinned meshes need no extra work.
            }
        }

        private CachedTrack[] BuildTrackCache(object animation)
        {
            List<CachedTrack> result = new List<CachedTrack>();

            object tracksObject = GetMember(
                animation,
                "Tracks", "tracks",
                "Channels", "channels");

            IEnumerable tracks = tracksObject as IEnumerable;

            if (tracks == null)
                return result.ToArray();

            foreach (object track in tracks)
            {
                if (track == null)
                    continue;

                int nodeIndex = GetInt(
                    track,
                    -1,
                    "NodeIndex", "nodeIndex",
                    "TargetNode", "targetNode");

                if (nodeIndex < 0 ||
                    nodeIndex >= _nodeTransforms.Length)
                    continue;

                Transform target = _nodeTransforms[nodeIndex];

                if (target == null)
                    continue;

                string path = GetString(
                    track,
                    "Path", "path",
                    "TargetPath", "targetPath");

                string interpolation = GetString(
                    track,
                    "Interpolation", "interpolation");

                float[] times = GetFloatArray(
                    track,
                    "Times", "times",
                    "Input", "input");

                float[] values = GetFloatArray(
                    track,
                    "Values", "values",
                    "Output", "output");

                if (times == null || times.Length == 0 ||
                    values == null || values.Length == 0)
                    continue;

                int components = GetInt(
                    track,
                    GetDefaultComponents(path),
                    "Components", "components",
                    "ComponentCount", "componentCount",
                    "ValuesPerKeyframe", "valuesPerKeyframe");

                if (components <= 0)
                    components = GetDefaultComponents(path);

                CachedTrack cached = new CachedTrack
                {
                    Target = target,
                    Path = path,
                    Cubic = string.Equals(
                        interpolation,
                        "CUBICSPLINE",
                        StringComparison.OrdinalIgnoreCase),
                    Step = string.Equals(
                        interpolation,
                        "STEP",
                        StringComparison.OrdinalIgnoreCase),
                    Components = components,
                    Times = times,
                    Values = values
                };

                result.Add(cached);
            }

            return result.ToArray();
        }

        private static float CalculateDuration(CachedTrack[] tracks)
        {
            float duration = 0f;

            for (int i = 0; i < tracks.Length; i++)
            {
                float[] times = tracks[i].Times;

                if (times != null && times.Length > 0)
                {
                    float last = times[times.Length - 1];

                    if (last > duration)
                        duration = last;
                }
            }

            return duration;
        }

        private void SampleAnimation(float time)
        {
            if (_currentTracks == null)
                return;

            for (int i = 0; i < _currentTracks.Length; i++)
            {
                CachedTrack track = _currentTracks[i];

                if (track.Target == null)
                    continue;

                SampleTrack(track, time);
            }
        }

        private void SampleTrack(CachedTrack track, float time)
        {
            float[] times = track.Times;
            float[] values = track.Values;

            int keyCount = times.Length;

            if (keyCount == 0)
                return;

            int first;
            int second;

            if (time <= times[0])
            {
                first = second = 0;
            }
            else if (time >= times[keyCount - 1])
            {
                first = second = keyCount - 1;
            }
            else
            {
                // Binary search instead of scanning every keyframe.
                int low = 0;
                int high = keyCount - 1;

                while (low + 1 < high)
                {
                    int middle = low + (high - low) / 2;

                    if (times[middle] <= time)
                        low = middle;
                    else
                        high = middle;
                }

                first = low;
                second = high;
            }

            int components = track.Components;

            float segmentDuration = times[second] - times[first];

            float t = segmentDuration > 0f
                ? Mathf.Clamp01((time - times[first]) / segmentDuration)
                : 0f;

            float[] result = _sampleBuffer;

            if (track.Cubic)
            {
                int stride = components * 3;

                int firstValueOffset = first * stride + components;
                int secondValueOffset = second * stride + components;
                int firstOutOffset = first * stride + components * 2;
                int secondInOffset = second * stride;

                float t2 = t * t;
                float t3 = t2 * t;

                for (int c = 0; c < components; c++)
                {
                    float p0 = ReadValue(
                        values, firstValueOffset + c, 0f);

                    float p1 = ReadValue(
                        values, secondValueOffset + c, p0);

                    if (track.Step || first == second)
                    {
                        result[c] = p0;
                        continue;
                    }

                    float m0 = ReadValue(
                        values, firstOutOffset + c, 0f);

                    float m1 = ReadValue(
                        values, secondInOffset + c, 0f);

                    result[c] =
                        (2f * t3 - 3f * t2 + 1f) * p0 +
                        (t3 - 2f * t2 + t) * segmentDuration * m0 +
                        (-2f * t3 + 3f * t2) * p1 +
                        (t3 - t2) * segmentDuration * m1;
                }
            }
            else
            {
                int firstOffset = first * components;
                int secondOffset = second * components;

                for (int c = 0; c < components; c++)
                {
                    float a = ReadValue(
                        values, firstOffset + c, 0f);

                    float b = ReadValue(
                        values, secondOffset + c, a);

                    result[c] = track.Step
                        ? a
                        : Mathf.LerpUnclamped(a, b, t);
                }
            }

            ApplyValue(track.Target, track.Path, result, components);
        }

        private static void ApplyValue(
            Transform target,
            string path,
            float[] value,
            int length)
        {
            if (target == null || value == null)
                return;

            if (string.Equals(
                path, "translation",
                StringComparison.OrdinalIgnoreCase))
            {
                if (length >= 3)
                {
                    target.localPosition =
                        new Vector3(value[0], value[1], value[2]);
                }
            }
            else if (string.Equals(
                path, "scale",
                StringComparison.OrdinalIgnoreCase))
            {
                if (length >= 3)
                {
                    target.localScale =
                        new Vector3(value[0], value[1], value[2]);
                }
            }
            else if (string.Equals(
                path, "rotation",
                StringComparison.OrdinalIgnoreCase))
            {
                if (length >= 4)
                {
                    Quaternion rotation = new Quaternion(
                        value[0], value[1], value[2], value[3]);

                    float magnitude = Mathf.Sqrt(
                        rotation.x * rotation.x +
                        rotation.y * rotation.y +
                        rotation.z * rotation.z +
                        rotation.w * rotation.w);

                    if (magnitude > 0.000001f)
                    {
                        rotation.x /= magnitude;
                        rotation.y /= magnitude;
                        rotation.z /= magnitude;
                        rotation.w /= magnitude;
                    }
                    else
                    {
                        rotation = Quaternion.identity;
                    }

                    target.localRotation = rotation;
                }
            }
        }

        private static int GetDefaultComponents(string path)
        {
            if (string.Equals(
                path, "rotation",
                StringComparison.OrdinalIgnoreCase))
                return 4;

            if (string.Equals(
                path, "translation",
                StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                path, "scale",
                StringComparison.OrdinalIgnoreCase))
                return 3;

            return 1;
        }

        private static object GetMember(
            object instance,
            params string[] names)
        {
            if (instance == null)
                return null;

            Type type = instance.GetType();

            for (int i = 0; i < names.Length; i++)
            {
                FieldInfo field = type.GetField(
                    names[i],
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

                if (field != null)
                    return field.GetValue(instance);

                PropertyInfo property = type.GetProperty(
                    names[i],
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

                if (property != null && property.CanRead)
                    return property.GetValue(instance, null);
            }

            return null;
        }

        private static string GetString(
            object instance,
            params string[] names)
        {
            object value = GetMember(instance, names);
            return value as string ?? string.Empty;
        }

        private static int GetInt(
            object instance,
            int fallback,
            params string[] names)
        {
            object value = GetMember(instance, names);

            if (value == null)
                return fallback;

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return fallback;
            }
        }

        private static float[] GetFloatArray(
            object instance,
            params string[] names)
        {
            object value = GetMember(instance, names);

            if (value == null)
                return null;

            float[] direct = value as float[];

            if (direct != null)
                return direct;

            IEnumerable enumerable = value as IEnumerable;

            if (enumerable == null)
                return null;

            List<float> result = new List<float>();

            foreach (object item in enumerable)
            {
                try
                {
                    result.Add(Convert.ToSingle(item));
                }
                catch
                {
                    return null;
                }
            }

            return result.ToArray();
        }

        private static float ReadValue(
            float[] values,
            int index,
            float fallback)
        {
            if (values == null ||
                index < 0 ||
                index >= values.Length)
                return fallback;

            return values[index];
        }
    }
}
