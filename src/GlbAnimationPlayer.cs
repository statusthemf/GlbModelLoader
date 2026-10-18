
using System;
using UnityEngine;

namespace ULTRADROID.Models
{
    /// <summary>
    /// Reusable animation player for a model loaded by ModelAPI.
    /// Call Update(Time.deltaTime) from your mod's Update method.
    /// </summary>
    public sealed class GlbAnimationPlayer
    {
        private readonly GameObject _modelRoot;
        private readonly Transform[] _nodes;
        private readonly GlbAnimationData[] _animations;

        private readonly Vector3[] _initialPositions;
        private readonly Quaternion[] _initialRotations;
        private readonly Vector3[] _initialScales;

        private GlbAnimationData _current;
        private float _time;
        private bool _playing;
        private bool _loop = true;
        private float _speed = 1f;

        internal GlbAnimationPlayer(
            GameObject modelRoot,
            GlbAnimationData[] animations,
            Transform[] nodes)
        {
            _modelRoot = modelRoot;
            _animations = animations ?? new GlbAnimationData[0];
            _nodes = nodes ?? new Transform[0];

            _initialPositions = new Vector3[_nodes.Length];
            _initialRotations = new Quaternion[_nodes.Length];
            _initialScales = new Vector3[_nodes.Length];

            for (int i = 0; i < _nodes.Length; i++)
            {
                Transform node = _nodes[i];

                if (node == null)
                    continue;

                _initialPositions[i] = node.localPosition;
                _initialRotations[i] = node.localRotation;
                _initialScales[i] = node.localScale;
            }
        }

        public bool IsPlaying
        {
            get { return _playing; }
        }

        public bool IsPaused
        {
            get { return _current != null && !_playing; }
        }

        public string CurrentAnimationName
        {
            get { return _current == null ? null : _current.Name; }
        }

        public float Time
        {
            get { return _time; }
        }

        public float Duration
        {
            get
            {
                return _current == null
                    ? 0f
                    : GetDuration(_current);
            }
        }

        public float PlaybackSpeed
        {
            get { return _speed; }
            set
            {
                if (!float.IsNaN(value) &&
                    !float.IsInfinity(value) &&
                    value >= 0f)
                {
                    _speed = value;
                }
            }
        }

        public bool Play(string animationName, bool loop = true)
        {
            if (_modelRoot == null ||
                string.IsNullOrEmpty(animationName))
            {
                return false;
            }

            for (int i = 0; i < _animations.Length; i++)
            {
                GlbAnimationData clip = _animations[i];

                if (clip == null ||
                    !string.Equals(
                        clip.Name,
                        animationName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (clip.Tracks == null || clip.Tracks.Length == 0)
                    return false;

                RestoreInitialPose();

                _current = clip;
                _time = 0f;
                _loop = loop;
                _playing = true;

                ApplyAnimation(0f);
                ModelAPI.UpdateCpuSkinning(_modelRoot);

                return true;
            }

            return false;
        }

        public bool PlayFirst(bool loop = true)
        {
            if (_animations == null)
                return false;

            for (int i = 0; i < _animations.Length; i++)
            {
                GlbAnimationData clip = _animations[i];

                if (clip != null &&
                    clip.Tracks != null &&
                    clip.Tracks.Length > 0)
                {
                    return Play(clip.Name, loop);
                }
            }

            return false;
        }

        public void Pause()
        {
            _playing = false;
        }

        public void Resume()
        {
            if (_current != null && GetDuration(_current) > 0f)
                _playing = true;
        }

        public void Stop()
        {
            _playing = false;
            _time = 0f;
            _current = null;

            RestoreInitialPose();

            if (_modelRoot != null)
                ModelAPI.UpdateCpuSkinning(_modelRoot);
        }

        /// <summary>
        /// Advances playback. Call once per frame from your mod.
        /// </summary>
        public void Update(float deltaTime)
        {
            if (_modelRoot == null)
                return;

            if (_playing && _current != null)
            {
                float duration = GetDuration(_current);

                if (duration > 0f)
                {
                    _time += Mathf.Max(0f, deltaTime) * _speed;

                    if (_loop)
                    {
                        _time %= duration;
                    }
                    else if (_time >= duration)
                    {
                        _time = duration;
                    }

                    ApplyAnimation(_time);

                    if (!_loop && _time >= duration)
                        _playing = false;
                }
                else
                {
                    _playing = false;
                }
            }

            // Essential for models using the API's CPU-skinning fallback.
            ModelAPI.UpdateCpuSkinning(_modelRoot);
        }

        private void ApplyAnimation(float time)
        {
            if (_current == null || _current.Tracks == null)
                return;

            for (int i = 0; i < _current.Tracks.Length; i++)
            {
                GlbAnimationTrack track = _current.Tracks[i];

                if (track == null)
                    continue;

                int nodeIndex = track.TargetNode;

                if (nodeIndex < 0 || nodeIndex >= _nodes.Length)
                    continue;

                Transform target = _nodes[nodeIndex];

                if (target == null)
                    continue;

                int components;

                if (track.Path == "translation" ||
                    track.Path == "scale")
                {
                    components = 3;
                }
                else if (track.Path == "rotation")
                {
                    components = 4;
                }
                else
                {
                    // Morph-target weights are not implemented here.
                    continue;
                }

                float[] value = new float[components];

                if (!TrySample(track, time, components, value))
                    continue;

                if (track.Path == "translation")
                {
                    target.localPosition = new Vector3(
                        value[0], value[1], value[2]);
                }
                else if (track.Path == "scale")
                {
                    target.localScale = new Vector3(
                        value[0], value[1], value[2]);
                }
                else if (track.Path == "rotation")
                {
                    Quaternion rotation = new Quaternion(
                        value[0], value[1], value[2], value[3]);

                    if (rotation.sqrMagnitude > 0.000001f)
                        target.localRotation = rotation.normalized;
                }
            }
        }

        private void RestoreInitialPose()
        {
            for (int i = 0; i < _nodes.Length; i++)
            {
                Transform node = _nodes[i];

                if (node == null)
                    continue;

                node.localPosition = _initialPositions[i];
                node.localRotation = _initialRotations[i];
                node.localScale = _initialScales[i];
            }
        }

        private static float GetDuration(GlbAnimationData clip)
        {
            if (clip == null)
                return 0f;

            float duration = clip.Duration;

            if (clip.Tracks == null)
                return duration;

            for (int i = 0; i < clip.Tracks.Length; i++)
            {
                GlbAnimationTrack track = clip.Tracks[i];

                if (track == null || track.Times == null)
                    continue;

                for (int k = 0; k < track.Times.Length; k++)
                {
                    if (track.Times[k] > duration)
                        duration = track.Times[k];
                }
            }

            return Mathf.Max(0f, duration);
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
                output == null ||
                output.Length < components ||
                track.Times.Length == 0)
            {
                return false;
            }

            float[] times = track.Times;
            float[] values = track.Values;
            int last = times.Length - 1;

            bool cubic = string.Equals(
                track.Interpolation,
                "CUBICSPLINE",
                StringComparison.OrdinalIgnoreCase);

            int multiplier = cubic ? 3 : 1;

            long required = (long)times.Length *
                components * multiplier;

            if (values.Length < required)
                return false;

            if (time <= times[0])
            {
                CopyKey(values, 0, components, multiplier, output);
                return true;
            }

            if (time >= times[last])
            {
                CopyKey(values, last, components, multiplier, output);
                return true;
            }

            int low = 0;
            int high = last;

            while (high - low > 1)
            {
                int middle = (low + high) / 2;

                if (times[middle] <= time)
                    low = middle;
                else
                    high = middle;
            }

            int left = low;
            int right = high;

            string interpolation = track.Interpolation ?? "LINEAR";

            if (string.Equals(
                interpolation,
                "STEP",
                StringComparison.OrdinalIgnoreCase))
            {
                CopyKey(values, left, components, multiplier, output);
                return true;
            }

            float span = times[right] - times[left];

            if (span <= 0.000001f)
            {
                CopyKey(values, left, components, multiplier, output);
                return true;
            }

            float alpha = (time - times[left]) / span;

            if (cubic)
            {
                // glTF CUBICSPLINE stores each key as:
                // incoming tangent, value, outgoing tangent.
                int leftValue = (left * 3 + 1) * components;
                int leftOut = (left * 3 + 2) * components;
                int rightIn = (right * 3) * components;
                int rightValue = (right * 3 + 1) * components;

                float t2 = alpha * alpha;
                float t3 = t2 * alpha;

                float h00 = 2f * t3 - 3f * t2 + 1f;
                float h10 = t3 - 2f * t2 + alpha;
                float h01 = -2f * t3 + 3f * t2;
                float h11 = t3 - t2;

                for (int c = 0; c < components; c++)
                {
                    output[c] =
                        h00 * values[leftValue + c] +
                        h10 * span * values[leftOut + c] +
                        h01 * values[rightValue + c] +
                        h11 * span * values[rightIn + c];
                }

                // glTF quaternion spline values are normalized after
                // component-wise cubic interpolation.
                if (track.Path == "rotation" && components == 4)
                {
                    Quaternion q = new Quaternion(
                        output[0], output[1], output[2], output[3]);

                    if (q.sqrMagnitude > 0.000001f)
                    {
                        q = q.normalized;
                        output[0] = q.x;
                        output[1] = q.y;
                        output[2] = q.z;
                        output[3] = q.w;
                    }
                }

                return true;
            }

            int leftOffset = left * components;
            int rightOffset = right * components;

            if (track.Path == "rotation" && components == 4)
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

                Quaternion q = Quaternion.SlerpUnclamped(
                    a, b, alpha).normalized;

                output[0] = q.x;
                output[1] = q.y;
                output[2] = q.z;
                output[3] = q.w;
            }
            else
            {
                for (int c = 0; c < components; c++)
                {
                    output[c] = Mathf.LerpUnclamped(
                        values[leftOffset + c],
                        values[rightOffset + c],
                        alpha);
                }
            }

            return true;
        }

        private static void CopyKey(
            float[] values,
            int key,
            int components,
            int multiplier,
            float[] output)
        {
            int offset = (key * multiplier +
                (multiplier == 3 ? 1 : 0)) * components;

            for (int i = 0; i < components; i++)
                output[i] = values[offset + i];

            if (components == 4 && output.Length >= 4)
            {
                Quaternion q = new Quaternion(
                    output[0], output[1], output[2], output[3]);

                if (q.sqrMagnitude > 0.000001f)
                {
                    q = q.normalized;
                    output[0] = q.x;
                    output[1] = q.y;
                    output[2] = q.z;
                    output[3] = q.w;
                }
            }
        }
    }
}
