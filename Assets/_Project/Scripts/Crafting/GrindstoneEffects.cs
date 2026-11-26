using System.Collections.Generic;
using UnityEngine;

namespace OverSmithed.Crafting
{
    /// <summary>
    /// The grindstone's look (CR-19), local to each machine (it reads the synced station): while someone polishes, the
    /// wheel spins up and sparks fly; the rust spots of the worn tray (children of its model named "Rust...") shrink
    /// with the progress, so the tray visibly gets cleaner; when it is done, a bright shine burst.
    /// </summary>
    public class GrindstoneEffects : MonoBehaviour
    {
        private const string RustPrefix = "Rust";

        [SerializeField] private Station _station;
        [Tooltip("The wheel model: it turns around its local X axis.")]
        [SerializeField] private Transform _wheel;
        [Tooltip("Wheel speed (degrees per second) while polishing.")]
        [SerializeField] private float _wheelSpeed = 600f;
        [Tooltip("Time (seconds) for the wheel to reach full speed or to stop.")]
        [SerializeField, Min(0.01f)] private float _spinUpTime = 0.35f;
        [SerializeField] private ParticleSystem _sparks;
        [Tooltip("Sparks per second at full speed.")]
        [SerializeField, Min(0f)] private float _sparkRate = 40f;
        [Tooltip("Played on the tray when it is clean again.")]
        [SerializeField] private ParticleSystem _shineBurst;

        private float _spin; // 0 stopped .. 1 full speed
        private readonly Dictionary<Transform, Vector3> _rustRest = new();
        private Transform _rustOwner;
        private readonly List<Transform> _scratch = new();

        private void OnEnable()
        {
            if (_station != null)
            {
                _station.WorkCompleted += HandleWorkCompleted;
            }
        }

        private void OnDisable()
        {
            if (_station != null)
            {
                _station.WorkCompleted -= HandleWorkCompleted;
            }
            RestoreRust();
        }

        private void HandleWorkCompleted(Station station)
        {
            if (_shineBurst == null)
            {
                return;
            }
            var item = station.Item;
            var position = item != null ? item.transform.position + Vector3.up * 0.3f : station.ItemPoint.position;
            Instantiate(_shineBurst, position, Quaternion.identity);
        }

        private void Update()
        {
            if (_station == null)
            {
                return;
            }
            var working = _station.IsSpawned && _station.IsWorking;
            _spin = Mathf.MoveTowards(_spin, working ? 1f : 0f, Time.deltaTime / _spinUpTime);
            if (_wheel != null && _spin > 0f)
            {
                _wheel.Rotate(Vector3.right, _wheelSpeed * _spin * Time.deltaTime, Space.Self);
            }
            if (_sparks != null)
            {
                var emission = _sparks.emission;
                emission.rateOverTime = working ? _sparkRate * _spin : 0f;
                if (!_sparks.isPlaying)
                {
                    _sparks.Play();
                }
            }
            UpdateRust();
        }

        // The rust spots of the tray on the grindstone shrink as the polishing goes on.
        private void UpdateRust()
        {
            var item = _station.IsSpawned ? _station.Item : null;
            var visual = item != null ? (item.VisualRoot != null ? item.VisualRoot : item.transform) : null;
            if (visual != _rustOwner || HasLostRust())
            {
                RestoreRust();
                _rustOwner = visual;
                if (visual != null)
                {
                    visual.GetComponentsInChildren(true, _scratch);
                    foreach (var child in _scratch)
                    {
                        if (child.name.StartsWith(RustPrefix))
                        {
                            _rustRest[child] = child.localScale;
                        }
                    }
                }
            }
            if (_rustRest.Count == 0)
            {
                return;
            }
            var left = Mathf.Lerp(1f, 0.05f, _station.Progress);
            foreach (var pair in _rustRest)
            {
                if (pair.Key != null)
                {
                    pair.Key.localScale = pair.Value * left;
                }
            }
        }

        // The tray's visual was rebuilt (it became clean): the old rust objects are gone.
        private bool HasLostRust()
        {
            foreach (var pair in _rustRest)
            {
                if (pair.Key == null)
                {
                    return true;
                }
            }
            return false;
        }

        private void RestoreRust()
        {
            foreach (var pair in _rustRest)
            {
                if (pair.Key != null)
                {
                    pair.Key.localScale = pair.Value;
                }
            }
            _rustRest.Clear();
            _rustOwner = null;
        }
    }
}
