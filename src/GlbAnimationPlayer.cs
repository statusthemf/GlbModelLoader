
using System;
using UnityEngine;

namespace ULTRADROID.Models
{
    public sealed class GlbAnimationPlayer
    {
        private readonly GameObject _modelRoot;
        private readonly GlbAnimationData[] _animations;
        private readonly Transform[] _nodeTransforms;

        private readonly Vector3[] _originalPositions;
        private readonly Quaternion[] _originalRotations;
        private readonly Vector3[] _originalScales;

        private GlbAnimationData _currentAnimation;
        private float _time;
        private bool _loop;
        private bool _playing;
        private bool _paused;

        public float PlaybackSpeed { get; set; } = 1f;

        public bool IsPlaying
        {
            get { return _playing && !_paused; }
        }

        public bool IsPaused
        {
            get { return _paused; }
        }

        public string CurrentAnimationName
        {
            get { return _currentAnimation?.Name; }
        }

        public float CurrentTime
        {
            get { return _time; }
        }

        public float CurrentDuration
        {
            get { return GetDuration(_currentAnimation); }
        }

        public int AnimationCount
        {
            get { return _animations.Length; }
        }

        public GlbAnimationPlayer(
            GameObject modelRoot,
            GlbAnimationData[] animations,
            Transform[] nodeTransforms)
        {
            _modelRoot = modelRoot;
            _animations = animations ?? new GlbAnimationData[0];
            _nodeTransforms = nodeTransforms ?? new Transform[0];

            _originalPositions = new Vector3[_nodeTransforms.Length];
            _originalRotations = new Quaternion[_nodeTransforms.Length];
            _originalScales = new Vector3[_nodeTransforms.Length];

            for (int i = 0; i < _nodeTransforms.Length; i++)
            {
                Transform node = _nodeTransforms[i];

                if (node == null)
                    continue;

                _originalPositions[i] = node.localPosition;
                _originalRotations[i] = node.localRotation;
                _originalScales[i] = node.localScale;
            }
        }

        public string GetAnimationName(int index)
        {
            if (index < 0 || index >= _animations.Length)
                return null;

            return _animations[index]?.Name;
        }

        public bool Play(string name, bool loop)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            for (int i = 0; i < _animations.Length; i++)
            {
                GlbAnimationData animation = _animations[i];

                if (animation != null &&
                    string.Equals(
                        animation.Name,
                        name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Play(i, loop);
                }
            }

            return false;
        }

        public bool Play(int index, bool loop)
        {
            if (index < 0 || index >= _animations.Length)
                return false;

            GlbAnimationData animation = _animations[index];

            if (animation == null)
                return false;

            _currentAnimation = animation;
            _loop = loop;
            _playing = true;
            _paused = false;

            float duration = GetDuration(animation);
            _time = PlaybackSpeed < 0f ? duration : 0f;

            ApplyAnimation(_currentAnimation, _time);

            if (_modelRoot != null)
                ModelAPI.UpdateCpuSkinning(_modelRoot);

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
            _currentAnimation = null;
            _time = 0f;

            RestoreOriginalTransforms();

            if (_modelRoot != null)
                ModelAPI.UpdateCpuSkinning(_modelRoot);
        }

        public void Update(float deltaTime)
        {
            if (!_playing || _paused || _currentAnimation == null)
                return;

            if (_modelRoot == null)
            {
                _playing = false;
                return;
            }

            float duration = GetDuration(_currentAnimation);

            if (duration <= 0f)
            {
                ApplyAnimation(_currentAnimation, 0f);
                ModelAPI.UpdateCpuSkinning(_modelRoot);
                return;
            }

            _time += deltaTime * PlaybackSpeed;

            if (_loop)
            {
                _time = Mathf.Repeat(_time, duration);
            }
            else
            {
                if (_time >= duration)
                {
                    _time = duration;
                    ApplyAnimation(_currentAnimation, _time);
                    _playing = false;
                    ModelAPI.UpdateCpuSkinning(_modelRoot);
                    return;
                }

                if (_time <= 0f)
                {
                    _time = 0f;
                    ApplyAnimation(_currentAnimation, _time);

                    if (PlaybackSpeed < 0f)
                        _playing = false;

                    ModelAPI.UpdateCpuSkinning(_modelRoot);
                    return;
                }
            }

            ApplyAnimation(_currentAnimation, _time);
            ModelAPI.UpdateCpuSkinning(_modelRoot);
        }

        private void ApplyAnimation(
            GlbAnimationData animation,
            float time)
        {
            if (animation == null || animation.Tracks == null)
                return;

            for (int i = 0; i < animation.Tracks.Length; i++)
            {
                GlbAnimationTrack track = animation.Tracks[i];

                if (track == null ||
                    track.TargetNode < 0 ||
                    track.TargetNode >= _nodeTransforms.Length)
                    continue;

                Transform target = _nodeTransforms[track.TargetNode];

                if (target == null ||
                    track.Times == null ||
                    track.Times.Length == 0 ||
                    track.Values == null ||
                    track.Values.Length == 0)
                    continue;

                switch (track.Path)
                {
                    case "translation":
                    {
                        float[] v;

                        if (TrySample(track, time, 3, out v))
                        {
                            target.localPosition =
                                new Vector3(v[0], v[1], v[2]);
                        }

                        break;
                    }

                    case "scale":
                    {
                        float[] v;

                        if (TrySample(track, time, 3, out v))
                        {
                            target.localScale =
                                new Vector3(v[0], v[1], v[2]);
                        }

                        break;
                    }

                    case "rotation":
                    {
                        Quaternion q;

                        if (TrySampleRotation(track, time, out q))
                            target.localRotation = q;

                        break;
                    }

                    // "weights" requires morph-target support.
                    // It is intentionally ignored by this player.
                }
            }
        }

        private bool TrySample(
            GlbAnimationTrack track,
            float time,
            int requiredComponents,
            out float[] result)
        {
            result = null;

            float[] times = track.Times;
            float[] values = track.Values;

            if (times == null || times.Length == 0 ||
                values == null || values.Length == 0)
                return false;

            int components = track.ComponentsPerElement > 0
                ? track.ComponentsPerElement
                : requiredComponents;

            if (components < requiredComponents)
                return false;

            bool cubic = IsInterpolation(track, "CUBICSPLINE");
            bool step = IsInterpolation(track, "STEP");

            int stride = components * (cubic ? 3 : 1);

            if (values.Length < times.Length * stride)
            {
                // A single-key animation can still be sampled safely.
                if (times.Length == 1 &&
                    values.Length >= requiredComponents)
                {
                    int offset = cubic ? components : 0;

                    if (offset + requiredComponents > values.Length)
                        return false;

                    result = new float[requiredComponents];

                    Array.Copy(
                        values,
                        offset,
                        result,
                        0,
                        requiredComponents);

                    return true;
                }

                return false;
            }

            int last = times.Length - 1;

            if (time <= times[0])
                return ReadKey(values, 0, components, cubic,
                    requiredComponents, out result);

            if (time >= times[last])
                return ReadKey(values, last * stride, components, cubic,
                    requiredComponents, out result);

            int left = 0;

            for (int i = 0; i < last; i++)
            {
                if (time >= times[i] && time < times[i + 1])
                {
                    left = i;
                    break;
                }
            }

            int right = left + 1;
            float t0 = times[left];
            float t1 = times[right];
            float dt = t1 - t0;

            if (dt <= 0f)
                return ReadKey(values, left * stride, components, cubic,
                    requiredComponents, out result);

            if (step)
                return ReadKey(values, left * stride, components, cubic,
                    requiredComponents, out result);

            float t = Mathf.Clamp01((time - t0) / dt);

            result = new float[requiredComponents];

            int leftOffset = left * stride;
            int rightOffset = right * stride;

            if (cubic)
            {
                int leftValue = leftOffset + components;
                int leftOutTangent = leftOffset + components * 2;
                int rightInTangent = rightOffset;
                int rightValue = rightOffset + components;

                float t2 = t * t;
                float t3 = t2 * t;

                float h00 = 2f * t3 - 3f * t2 + 1f;
                float h10 = t3 - 2f * t2 + t;
                float h01 = -2f * t3 + 3f * t2;
                float h11 = t3 - t2;

                for (int c = 0; c < requiredComponents; c++)
                {
                    float p0 = values[leftValue + c];
                    float m0 = values[leftOutTangent + c];
                    float p1 = values[rightValue + c];
                    float m1 = values[rightInTangent + c];

                    result[c] =
                        h00 * p0 +
                        h10 * dt * m0 +
                        h01 * p1 +
                        h11 * dt * m1;
                }

                return true;
            }

            for (int c = 0; c < requiredComponents; c++)
            {
                result[c] = Mathf.LerpUnclamped(
                    values[leftOffset + c],
                    values[rightOffset + c],
                    t);
            }

            return true;
        }

        private bool TrySampleRotation(
            GlbAnimationTrack track,
            float time,
            out Quaternion result)
        {
            result = Quaternion.identity;

            if (track == null ||
                track.Times == null ||
                track.Times.Length == 0)
                return false;

            if (IsInterpolation(track, "CUBICSPLINE") ||
                IsInterpolation(track, "STEP") ||
                track.Times.Length == 1)
            {
                float[] values;

                if (!TrySample(track, time, 4, out values))
                    return false;

                result = NormalizeQuaternion(new Quaternion(
                    values[0], values[1], values[2], values[3]));

                return true;
            }

            float[] times = track.Times;
            float[] valuesArray = track.Values;

            int components = track.ComponentsPerElement > 0
                ? track.ComponentsPerElement
                : 4;

            if (components < 4 ||
                valuesArray.Length < times.Length * components)
                return false;

            int last = times.Length - 1;

            if (time <= times[0])
            {
                result = ReadQuaternion(valuesArray, 0);
                return true;
            }

            if (time >= times[last])
            {
                result = ReadQuaternion(
                    valuesArray,
                    last * components);

                return true;
            }

            int left = 0;

            for (int i = 0; i < last; i++)
            {
                if (time >= times[i] && time < times[i + 1])
                {
                    left = i;
                    break;
                }
            }

            int right = left + 1;
            float dt = times[right] - times[left];

            if (dt <= 0f)
            {
                result = ReadQuaternion(
                    valuesArray,
                    left * components);

                return true;
            }

            float t = Mathf.Clamp01(
                (time - times[left]) / dt);

            result = Quaternion.Slerp(
                ReadQuaternion(valuesArray, left * components),
                ReadQuaternion(valuesArray, right * components),
                t);

            return true;
        }

        private bool ReadKey(
            float[] values,
            int keyOffset,
            int components,
            bool cubic,
            int requiredComponents,
            out float[] result)
        {
            result = null;

            int offset = cubic
                ? keyOffset + components
                : keyOffset;

            if (offset < 0 ||
                offset + requiredComponents > values.Length)
                return false;

            result = new float[requiredComponents];

            Array.Copy(
                values,
                offset,
                result,
                0,
                requiredComponents);

            return true;
        }

        private Quaternion ReadQuaternion(float[] values, int offset)
        {
            if (values == null ||
                offset < 0 ||
                offset + 4 > values.Length)
                return Quaternion.identity;

            return NormalizeQuaternion(new Quaternion(
                values[offset],
                values[offset + 1],
                values[offset + 2],
                values[offset + 3]));
        }

        private Quaternion NormalizeQuaternion(Quaternion q)
        {
            float magnitude = Mathf.Sqrt(
                q.x * q.x +
                q.y * q.y +
                q.z * q.z +
                q.w * q.w);

            if (magnitude <= 0.000001f)
                return Quaternion.identity;

            float inverse = 1f / magnitude;

            return new Quaternion(
                q.x * inverse,
                q.y * inverse,
                q.z * inverse,
                q.w * inverse);
        }

        private bool IsInterpolation(
            GlbAnimationTrack track,
            string interpolation)
        {
            return string.Equals(
                track.Interpolation,
                interpolation,
                StringComparison.OrdinalIgnoreCase);
        }

        private float GetDuration(GlbAnimationData animation)
        {
            if (animation == null)
                return 0f;

            float duration = Mathf.Max(0f, animation.Duration);

            if (animation.Tracks == null)
                return duration;

            for (int i = 0; i < animation.Tracks.Length; i++)
            {
                GlbAnimationTrack track = animation.Tracks[i];

                if (track == null || track.Times == null)
                    continue;

                for (int j = 0; j < track.Times.Length; j++)
                {
                    duration = Mathf.Max(duration, track.Times[j]);
                }
            }

            return duration;
        }

        private void RestoreOriginalTransforms()
        {
            for (int i = 0; i < _nodeTransforms.Length; i++)
            {
                Transform node = _nodeTransforms[i];

                if (node == null)
                    continue;

                node.localPosition = _originalPositions[i];
                node.localRotation = _originalRotations[i];
                node.localScale = _originalScales[i];
            }
        }
    }
}
