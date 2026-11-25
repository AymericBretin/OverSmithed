using System;
using System.Collections.Generic;
using UnityEngine;

namespace OverSmithed.Story
{
    /// <summary>
    /// One shot of a cutscene: the camera moves through its <see cref="_cameraKeys"/> (smooth glide, or a cut), can turn
    /// to keep an actor in the picture (<see cref="_followTarget"/>), the steps on its children play (actors walk,
    /// animate, lights fade...) and one dialogue box tells the story.
    /// The players go to the next shot with Interact (the host decides, see <see cref="StoryIntroDirector"/>).
    /// Camera and follow are local looks on every machine: nothing goes on the network.
    /// </summary>
    public class CutsceneShot : MonoBehaviour
    {
        /// <summary>A camera place at a time of the shot.</summary>
        [Serializable]
        public class CameraKey
        {
            [Tooltip("Seconds after the start of the shot.")]
            [Min(0f)] public float Time;
            [Tooltip("Camera position and rotation (an empty child of the shot).")]
            public Transform Pose;
            [Range(10f, 80f)] public float Fov = 38f;
            [Tooltip("How much the camera turns toward the follow target here (0 = keeps the key's rotation).")]
            [Range(0f, 1f)] public float Follow;
            [Tooltip("The camera jumps here at this time (a cut) instead of gliding from the previous key.")]
            public bool Cut;
        }

        [Header("Camera")]
        [Tooltip("In time order. The camera glides smoothly through the keys between two cuts.")]
        [SerializeField] private List<CameraKey> _cameraKeys = new();
        [Tooltip("Actor kept in the picture where the keys say Follow > 0 (optional).")]
        [SerializeField] private CutsceneActor _followTarget;
        [Tooltip("Height (m) of the point looked at on the follow target.")]
        [SerializeField] private float _followHeight = 0.9f;
        [Tooltip("Where that point sits on the screen (0..1). 0.62 = above the dialogue box.")]
        [SerializeField] private Vector2 _followScreen = new(0.5f, 0.62f);
        [Tooltip("How fast the camera catches up with the target (higher = tighter).")]
        [SerializeField, Min(0.1f)] private float _followSharpness = 5f;

        [Header("Look")]
        [Tooltip("Lighting of this shot (night, morning). Empty: no change.")]
        [SerializeField] private CutsceneMood _mood;
        [Tooltip("The screen starts black and fades in (time passes between the shots).")]
        [SerializeField] private bool _fadeFromBlack;

        [Header("Dialogue")]
        [Tooltip("Who speaks: an actor (an apprentice shows its skin name). Empty: the name below.")]
        [SerializeField] private CutsceneActor _speaker;
        [Tooltip("Name shown when there is no speaker actor (empty: no name, the narrator).")]
        [SerializeField] private string _speakerName;
        [SerializeField, TextArea(2, 5)] private string _text;
        [Tooltip("Seconds before the dialogue box appears.")]
        [SerializeField, Min(0f)] private float _textDelay = 0.8f;

        private CutsceneStep[] _steps;
        private float _time;
        private bool _followReady;
        private bool _posed;
        private Vector3 _followPoint;
        private float _followPresence;

        public CutsceneMood Mood => _mood;
        public bool FadeFromBlack => _fadeFromBlack;
        public string Text => _text;
        public float TextDelay => _textDelay;
        public CutsceneActor FollowTarget => _followTarget;

        public string SpeakerName =>
            _speaker != null && _speaker.IsPresent ? _speaker.DisplayName : _speakerName;

        /// <summary>Seconds since the shot started.</summary>
        public float Elapsed => _time;

        /// <summary>The steps of this shot, in play order.</summary>
        public IReadOnlyList<CutsceneStep> Steps => _steps ??= GetComponentsInChildren<CutsceneStep>(true);

        public void Begin()
        {
            _time = 0f;
            _followReady = false;
            _posed = false;
            _followPresence = 0f;
            foreach (var step in Steps)
            {
                step.ResetStep();
            }
        }

        public void Tick(float deltaTime)
        {
            _time += deltaTime;
            foreach (var step in Steps)
            {
                step.Tick(_time, deltaTime);
            }
        }

        /// <summary>Jumps every step to its end (the players went on before the shot was over).</summary>
        public void Complete()
        {
            foreach (var step in Steps)
            {
                step.Complete();
            }
        }

        // ---------- Camera ----------

        /// <summary>Places the camera for the current time. <paramref name="deltaTime"/> 0 = no smoothing (snap).</summary>
        public void PoseCamera(Camera camera, float deltaTime)
        {
            if (camera == null || !SampleKeys(_time, out var position, out var rotation, out var fov, out var follow))
            {
                return;
            }
            rotation = ApplyFollow(position, rotation, fov, camera.aspect, follow, deltaTime);
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.fieldOfView = fov;
        }

        // Keys between two cuts form a chain: the camera moves along a smooth curve through their positions, easing in
        // at the start of the chain and out at its end (no stop at the keys in between).
        private bool SampleKeys(float time, out Vector3 position, out Quaternion rotation, out float fov, out float follow)
        {
            position = default;
            rotation = Quaternion.identity;
            fov = 38f;
            follow = 0f;
            var count = _cameraKeys.Count;
            if (count == 0 || _cameraKeys[0].Pose == null)
            {
                return false;
            }
            // The chain that holds this time: from the last cut at or before it, to the next cut.
            var first = 0;
            for (var i = 1; i < count; i++)
            {
                if (_cameraKeys[i].Cut && _cameraKeys[i].Time <= time)
                {
                    first = i;
                }
            }
            var last = first;
            while (last + 1 < count && !_cameraKeys[last + 1].Cut)
            {
                last++;
            }
            var start = _cameraKeys[first].Time;
            var end = _cameraKeys[last].Time;
            if (last == first || end <= start)
            {
                return Read(_cameraKeys[first], out position, out rotation, out fov, out follow);
            }
            // Eased time through the chain, then the segment it falls in.
            var u = Mathf.Clamp01((time - start) / (end - start));
            var easedTime = start + (end - start) * (u * u * (3f - 2f * u));
            var segment = first;
            while (segment < last - 1 && _cameraKeys[segment + 1].Time <= easedTime)
            {
                segment++;
            }
            var a = _cameraKeys[segment];
            var b = _cameraKeys[segment + 1];
            if (a.Pose == null || b.Pose == null)
            {
                return Read(a, out position, out rotation, out fov, out follow);
            }
            var s = Mathf.Clamp01((easedTime - a.Time) / Mathf.Max(0.0001f, b.Time - a.Time));
            var before = segment > first && _cameraKeys[segment - 1].Pose != null ? _cameraKeys[segment - 1].Pose.position : a.Pose.position;
            var after = segment + 2 <= last && _cameraKeys[segment + 2].Pose != null ? _cameraKeys[segment + 2].Pose.position : b.Pose.position;
            position = CatmullRom(before, a.Pose.position, b.Pose.position, after, s);
            var smooth = s * s * (3f - 2f * s);
            rotation = Quaternion.Slerp(a.Pose.rotation, b.Pose.rotation, smooth);
            fov = Mathf.Lerp(a.Fov, b.Fov, smooth);
            follow = Mathf.Lerp(a.Follow, b.Follow, smooth);
            return true;
        }

        private static bool Read(CameraKey key, out Vector3 position, out Quaternion rotation, out float fov, out float follow)
        {
            position = key.Pose != null ? key.Pose.position : default;
            rotation = key.Pose != null ? key.Pose.rotation : Quaternion.identity;
            fov = key.Fov;
            follow = key.Follow;
            return key.Pose != null;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        // Turns the camera (no move) so the follow target sits at _followScreen, by the key's follow amount. The target
        // point is smoothed, and the follow fades out while the target is hidden.
        private Quaternion ApplyFollow(Vector3 position, Quaternion rotation, float fov, float aspect, float follow,
            float deltaTime)
        {
            var visible = _followTarget != null && _followTarget.IsVisible;
            // First pose of the shot: no smoothing. A target that shows up later fades in (presence) from its own place.
            var k = deltaTime > 0f && _posed ? 1f - Mathf.Exp(-_followSharpness * deltaTime) : 1f;
            _posed = true;
            _followPresence = Mathf.Lerp(_followPresence, visible ? 1f : 0f, k);
            if (visible)
            {
                var target = _followTarget.transform.position + Vector3.up * _followHeight;
                _followPoint = _followReady ? Vector3.Lerp(_followPoint, target, k) : target;
                _followReady = true;
            }
            var weight = follow * _followPresence;
            if (weight <= 0.001f || !_followReady)
            {
                return rotation;
            }
            var toTarget = _followPoint - position;
            if (toTarget.sqrMagnitude < 0.01f)
            {
                return rotation;
            }
            var halfHeight = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            var pitch = Mathf.Atan((_followScreen.y - 0.5f) * 2f * halfHeight) * Mathf.Rad2Deg;
            var yaw = Mathf.Atan((_followScreen.x - 0.5f) * 2f * halfHeight * aspect) * Mathf.Rad2Deg;
            var look = Quaternion.LookRotation(toTarget) * Quaternion.Euler(pitch, -yaw, 0f);
            return Quaternion.Slerp(rotation, look, weight);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3? previous = null;
            foreach (var key in _cameraKeys)
            {
                if (key.Pose == null)
                {
                    continue;
                }
                Gizmos.DrawWireSphere(key.Pose.position, 0.12f);
                Gizmos.DrawRay(key.Pose.position, key.Pose.forward * 1.2f);
                if (previous.HasValue && !key.Cut)
                {
                    Gizmos.DrawLine(previous.Value, key.Pose.position);
                }
                previous = key.Pose.position;
            }
        }
#endif
    }
}
