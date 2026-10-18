
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ULTRADROID.Models
{
    public sealed class GlbAnimationPlayer
    {
        private readonly GameObject _root;
        private readonly GlbAnimationData[] _animations;
        private readonly Transform[] _nodeTransforms;

        private object _currentAnimation;
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
            get { return GetDuration(_currentAnimation); }
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

            _currentAnimation = animation;
            _time = 0f;
            _playing = true;
            _paused = false;
            Loop = loop;

            SampleAnimation(_time);
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

            float duration = Duration;

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

        private void SampleAnimation(float time)
        {
            if (_currentAnimation == null)
                return;

            object tracksObject = GetMember(
                _currentAnimation,
                "Tracks", "tracks",
                "Channels", "channels");

            IEnumerable tracks = tracksObject as IEnumerable;

            if (tracks == null)
                return;

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

                SampleTrack(
                    target,
                    path,
                    interpolation,
                    times,
                    values,
                    components,
                    time);
            }
        }

        private static void SampleTrack(
            Transform target,
            string path,
            string interpolation,
            float[] times,
            float[] values,
            int components,
            float time)
        {
            if (target == null || times.Length == 0)
                return;

            bool cubic = string.Equals(
                interpolation,
                "CUBICSPLINE",
                StringComparison.OrdinalIgnoreCase);

            bool step = string.Equals(
                interpolation,
                "STEP",
                StringComparison.OrdinalIgnoreCase);

            int keyCount = times.Length;

            if (components <= 0)
                components = GetDefaultComponents(path);

            int first = 0;
            int second = 0;

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
                for (int i = 0; i < keyCount - 1; i++)
                {
                    if (time >= times[i] && time <= times[i + 1])
                    {
                        first = i;
                        second = i + 1;
                        break;
                    }
                }
            }

            float segmentDuration = times[second] - times[first];

            float t = segmentDuration > 0f
                ? Mathf.Clamp01((time - times[first]) / segmentDuration)
                : 0f;

            float[] result = new float[components];

            if (cubic)
            {
                int stride = components * 3;

                int firstValueOffset = first * stride + components;
                int secondValueOffset = second * stride + components;
                int firstOutOffset = first * stride + components * 2;
                int secondInOffset = second * stride;

                for (int c = 0; c < components; c++)
                {
                    float p0 = ReadValue(
                        values, firstValueOffset + c, 0f);

                    float p1 = ReadValue(
                        values, secondValueOffset + c, p0);

                    if (step || first == second)
                    {
                        result[c] = p0;
                        continue;
                    }

                    float m0 = ReadValue(
                        values, firstOutOffset + c, 0f);

                    float m1 = ReadValue(
                        values, secondInOffset + c, 0f);

                    float t2 = t * t;
                    float t3 = t2 * t;

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

                    result[c] = step
                        ? a
                        : Mathf.LerpUnclamped(a, b, t);
                }
            }

            ApplyValue(target, path, result);
        }

        private static void ApplyValue(
            Transform target,
            string path,
            float[] value)
        {
            if (target == null || value == null)
                return;

            if (string.Equals(
                path, "translation",
                StringComparison.OrdinalIgnoreCase))
            {
                if (value.Length >= 3)
                    target.localPosition =
                        new Vector3(value[0], value[1], value[2]);
            }
            else if (string.Equals(
                path, "scale",
                StringComparison.OrdinalIgnoreCase))
            {
                if (value.Length >= 3)
                    target.localScale =
                        new Vector3(value[0], value[1], value[2]);
            }
            else if (string.Equals(
                path, "rotation",
                StringComparison.OrdinalIgnoreCase))
            {
                if (value.Length >= 4)
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

        private float GetDuration(object animation)
        {
            if (animation == null)
                return 0f;

            object tracksObject = GetMember(
                animation,
                "Tracks", "tracks",
                "Channels", "channels");

            IEnumerable tracks = tracksObject as IEnumerable;

            if (tracks == null)
                return 0f;

            float duration = 0f;

            foreach (object track in tracks)
            {
                float[] times = GetFloatArray(
                    track,
                    "Times", "times",
                    "Input", "input");

                if (times != null && times.Length > 0)
                {
                    float last = times[times.Length - 1];

                    if (last > duration)
                        duration = last;
                }
            }

            return duration;
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
