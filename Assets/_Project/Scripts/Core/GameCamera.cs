using OverSmithed.Players;
using UnityEngine;

namespace OverSmithed.Core
{
    /// <summary>
    /// The game camera of a level. It keeps the same angle everywhere and frames the level's <see cref="PlayArea"/>
    /// by itself: it moves closer or further so the whole area is visible under the order cards (between a min and
    /// a max distance, so players never look too big or too small).
    /// When the area fits, the camera only slides a little toward the players (to feel alive). When the area is too
    /// big even at the max distance, the camera follows the players on that axis, without showing past the area.
    /// Runs on every machine from the synced player positions: nothing is sent on the network.
    /// In Edit Mode it places itself too (for a 16:9 screen), so level designers see the framing while they build.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class GameCamera : MonoBehaviour
    {
        private const float EditorAspect = 16f / 9f;

        [Header("Framing")]
        [SerializeField] private PlayArea _playArea;
        [Tooltip("Angle of the camera, down from the horizon (degrees). The same in every level.")]
        [SerializeField, Range(30f, 89f)] private float _pitch = 56f;
        [Tooltip("Closest the camera can be (meters): small rooms don't get closer, so players don't look too big.")]
        [SerializeField] private float _minDistance = 18f;
        [Tooltip("Furthest the camera can be (meters): bigger rooms make the camera follow the players instead.")]
        [SerializeField] private float _maxDistance = 26f;

        [Header("Screen space kept free (part of the screen height / width)")]
        [Tooltip("In game: the order cards. The camera keeps the play area under the bottom of this rect.")]
        [SerializeField] private RectTransform _topHud;
        [Tooltip("Used when there is no Top Hud, and in Edit Mode.")]
        [SerializeField, Range(0f, 0.5f)] private float _topMargin = 0.23f;
        [SerializeField, Range(0f, 0.3f)] private float _bottomMargin = 0.03f;
        [SerializeField, Range(0f, 0.3f)] private float _sideMargin = 0.02f;

        [Header("Moving with the players")]
        [Tooltip("When the area fits: part of the players' distance from the area center the camera follows (0 = fixed).")]
        [SerializeField, Range(0f, 1f)] private float _follow = 0.15f;
        [Tooltip("When the area fits: largest slide, in meters (x = left/right, y = toward the back/front).")]
        [SerializeField] private Vector2 _maxOffset = new(0.8f, 0.5f);
        [Tooltip("About how long (seconds) the camera takes to reach its target when the area fits: higher = softer.")]
        [SerializeField] private float _smoothTime = 0.8f;
        [Tooltip("Same, when the camera follows the players in a big area (shorter, to keep up with them).")]
        [SerializeField] private float _followSmoothTime = 0.35f;

        private Camera _camera;
        private Vector3 _velocity;
        private bool _snapNextFrame = true;

        /// <summary>Distance chosen by the last framing (for debugging in the Inspector).</summary>
        public float CurrentDistance { get; private set; }

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            _snapNextFrame = true;
        }

        // LateUpdate: after the players moved this frame (owner input or network interpolation).
        private void LateUpdate()
        {
            if (_playArea == null)
            {
                return;
            }
            var playing = Application.isPlaying;
            var aspect = playing ? _camera.aspect : EditorAspect;
            var topMargin = playing ? TopMarginFromHud() : _topMargin;
            var framing = Frame(_playArea.Bounds, aspect, topMargin);
            CurrentDistance = framing.Distance;

            var rotation = Quaternion.Euler(_pitch, 0f, 0f);
            var look = TargetLookPoint(framing, playing);
            var target = look - rotation * Vector3.forward * framing.Distance;

            if (!playing)
            {
                // Edit Mode: snap, and only write when it really changed (opening a scene must not dirty it).
                if ((transform.position - target).sqrMagnitude > 1e-6f || Quaternion.Angle(transform.rotation, rotation) > 0.01f)
                {
                    transform.SetPositionAndRotation(target, rotation);
                }
                return;
            }
            transform.rotation = rotation;
            if (_snapNextFrame)
            {
                _snapNextFrame = false;
                transform.position = target;
                _velocity = Vector3.zero;
                return;
            }
            var smoothTime = framing.FollowX || framing.FollowZ ? _followSmoothTime : _smoothTime;
            transform.position = Vector3.SmoothDamp(transform.position, target, ref _velocity, smoothTime);
        }

        private Vector3 TargetLookPoint(Framing framing, bool playing)
        {
            var look = framing.Look;
            var players = playing ? NetworkPlayer.Spawned : null;
            if (players == null || players.Count == 0)
            {
                return look;
            }
            var sum = Vector3.zero;
            foreach (var player in players)
            {
                sum += player.transform.position;
            }
            var average = sum / players.Count;
            var center = _playArea.Bounds.center;

            // Axis too big for the screen: follow the players, but never show past the area.
            // Axis that fits: stay framed, with a small slide toward the players.
            look.x = framing.FollowX
                ? Mathf.Clamp(average.x, framing.MinLook.x, framing.MaxLook.x)
                : look.x + Mathf.Clamp((average.x - center.x) * _follow, -_maxOffset.x, _maxOffset.x);
            look.z = framing.FollowZ
                ? Mathf.Clamp(average.z + framing.LookToBandCenter, framing.MinLook.z, framing.MaxLook.z)
                : look.z + Mathf.Clamp((average.z - center.z) * _follow, -_maxOffset.y, _maxOffset.y);
            return look;
        }

        // ---------------------------------------------------------------- Framing math

        private struct Framing
        {
            public float Distance;
            public Vector3 Look;          // Ground point the camera looks at when the area is framed.
            public bool FollowX, FollowZ; // The area doesn't fit on this axis, even at the max distance.
            public Vector3 MinLook, MaxLook;
            public float LookToBandCenter; // Look offset that puts a ground point in the middle of the free band.
        }

        private float TopMarginFromHud()
        {
            if (_topHud == null || _camera.pixelHeight <= 0)
            {
                return _topMargin;
            }
            var canvas = _topHud.GetComponentInParent<Canvas>();
            var canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var corners = new Vector3[4];
            _topHud.GetWorldCorners(corners);
            var bottom = float.MaxValue;
            foreach (var corner in corners)
            {
                bottom = Mathf.Min(bottom, RectTransformUtility.WorldToScreenPoint(canvasCamera, corner).y);
            }
            return Mathf.Clamp01(1f - bottom / _camera.pixelHeight);
        }

        private Framing Frame(Bounds area, float aspect, float topMargin)
        {
            var bandBottom = _bottomMargin;
            var bandTop = Mathf.Max(bandBottom + 0.05f, 1f - topMargin);
            var halfWidth = 0.5f - _sideMargin;

            // Smallest distance where the whole area fits in the free band (fitting only gets easier further away).
            var near = Mathf.Max(1f, _minDistance);
            var far = Mathf.Max(near, _maxDistance);
            float distance;
            if (Fits(area, near, aspect, bandBottom, bandTop, halfWidth))
            {
                distance = near;
            }
            else if (!Fits(area, far, aspect, bandBottom, bandTop, halfWidth))
            {
                distance = far;
            }
            else
            {
                for (var i = 0; i < 24; i++)
                {
                    var middle = (near + far) * 0.5f;
                    if (Fits(area, middle, aspect, bandBottom, bandTop, halfWidth))
                    {
                        far = middle;
                    }
                    else
                    {
                        near = middle;
                    }
                }
                distance = far;
            }

            var framing = new Framing { Distance = distance };
            var lookZ = CenterInBand(area, distance, aspect, bandBottom, bandTop, out var bottom, out var top);
            framing.Look = new Vector3(area.center.x, 0f, lookZ);
            framing.FollowZ = top - bottom > bandTop - bandBottom + 1e-4f;
            framing.FollowX = MaxHalfWidth(area, framing.Look, distance, aspect) > halfWidth + 1e-4f;
            framing.LookToBandCenter = (0.5f - (bandBottom + bandTop) * 0.5f) * MetersPerViewportY(distance);

            // Follow limits: the furthest look points that still keep the free band covered by the area.
            framing.MinLook = framing.MaxLook = framing.Look;
            if (framing.FollowX)
            {
                var max = Search(area.center.x, area.max.x,
                    x => RightEdge(area, new Vector3(x, 0f, lookZ), distance, aspect) >= 0.5f + halfWidth);
                framing.MaxLook.x = max;
                framing.MinLook.x = 2f * area.center.x - max;
            }
            if (framing.FollowZ)
            {
                // Looking further back moves the area down on screen: stop when its back reaches the band top.
                framing.MaxLook.z = Search(lookZ, area.max.z + area.size.z,
                    z => TopEdge(area, new Vector3(area.center.x, 0f, z), distance, aspect) >= bandTop);
                // Looking further front moves it up: stop when its front reaches the band bottom.
                framing.MinLook.z = Search(lookZ, area.min.z - area.size.z,
                    z => BottomEdge(area, new Vector3(area.center.x, 0f, z), distance, aspect) <= bandBottom);
            }
            return framing;
        }

        private bool Fits(Bounds area, float distance, float aspect, float bandBottom, float bandTop, float halfWidth)
        {
            var lookZ = CenterInBand(area, distance, aspect, bandBottom, bandTop, out var bottom, out var top);
            return top - bottom <= bandTop - bandBottom
                && MaxHalfWidth(area, new Vector3(area.center.x, 0f, lookZ), distance, aspect) <= halfWidth;
        }

        // Moves the look point forward/back until the area is centered in the free band. Returns its z.
        private float CenterInBand(Bounds area, float distance, float aspect, float bandBottom, float bandTop,
            out float bottom, out float top)
        {
            var lookZ = area.center.z;
            bottom = top = 0f;
            for (var i = 0; i < 8; i++)
            {
                var look = new Vector3(area.center.x, 0f, lookZ);
                bottom = BottomEdge(area, look, distance, aspect);
                top = TopEdge(area, look, distance, aspect);
                var error = (bottom + top) * 0.5f - (bandBottom + bandTop) * 0.5f;
                // Looking further back (bigger z) moves everything down on screen.
                lookZ += error * MetersPerViewportY(distance);
            }
            return lookZ;
        }

        // About how many meters on the ground one screen height covers, at this distance (for the corrections).
        private float MetersPerViewportY(float distance)
        {
            var tanHalf = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return 2f * distance * tanHalf / Mathf.Sin(_pitch * Mathf.Deg2Rad);
        }

        private float TopEdge(Bounds area, Vector3 look, float distance, float aspect)
        {
            var top = float.MinValue;
            foreach (var corner in Corners(area))
            {
                top = Mathf.Max(top, Viewport(corner, look, distance, aspect).y);
            }
            return top;
        }

        private float BottomEdge(Bounds area, Vector3 look, float distance, float aspect)
        {
            var bottom = float.MaxValue;
            foreach (var corner in Corners(area))
            {
                bottom = Mathf.Min(bottom, Viewport(corner, look, distance, aspect).y);
            }
            return bottom;
        }

        private float RightEdge(Bounds area, Vector3 look, float distance, float aspect)
        {
            var right = float.MinValue;
            foreach (var corner in Corners(area))
            {
                right = Mathf.Max(right, Viewport(corner, look, distance, aspect).x);
            }
            return right;
        }

        private float MaxHalfWidth(Bounds area, Vector3 look, float distance, float aspect)
        {
            var half = 0f;
            foreach (var corner in Corners(area))
            {
                half = Mathf.Max(half, Mathf.Abs(Viewport(corner, look, distance, aspect).x - 0.5f));
            }
            return half;
        }

        // Where a world point lands on screen (0..1), for a camera looking at `look` from `distance`.
        private Vector2 Viewport(Vector3 point, Vector3 look, float distance, float aspect)
        {
            var rotation = Quaternion.Euler(_pitch, 0f, 0f);
            var position = look - rotation * Vector3.forward * distance;
            var local = Quaternion.Inverse(rotation) * (point - position);
            var tanHalf = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var depth = Mathf.Max(0.01f, local.z);
            return new Vector2(0.5f + 0.5f * local.x / (depth * tanHalf * aspect),
                0.5f + 0.5f * local.y / (depth * tanHalf));
        }

        private static Vector3[] Corners(Bounds area)
        {
            var min = area.min;
            var max = area.max;
            return new[]
            {
                new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z),
            };
        }

        // Furthest value from `from` toward `to` for which `stillOk` is true (it is true at `from`).
        private static float Search(float from, float to, System.Func<float, bool> stillOk)
        {
            if (stillOk(to))
            {
                return to;
            }
            for (var i = 0; i < 24; i++)
            {
                var middle = (from + to) * 0.5f;
                if (stillOk(middle))
                {
                    from = middle;
                }
                else
                {
                    to = middle;
                }
            }
            return from;
        }
    }
}
