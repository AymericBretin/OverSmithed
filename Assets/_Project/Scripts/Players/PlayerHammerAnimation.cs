using System;
using OverSmithed.Crafting;
using UnityEngine;

namespace OverSmithed.Players
{
    /// <summary>
    /// Every machine poses the accepted worker of a hand-work station from the station's replicated clock: the hammer
    /// loop at the anvil, the saw loop at the workbench (<see cref="HandWorkEffects.Tool"/>). The tool is a local
    /// prop, attached to the current skin's hand; neither owner input nor local animation events decide who is
    /// working. (Named after its first tool, the hammer; Ewan's anvil tests use that name.)
    /// </summary>
    [DefaultExecutionOrder(20)]
    [RequireComponent(typeof(NetworkPlayer), typeof(PlayerWork))]
    public class PlayerHammerAnimation : MonoBehaviour
    {
        [Serializable]
        private struct SawSettings
        {
            public GameObject Prefab;
            public Vector3 Position;
            public Vector3 Euler;
            public Vector3 Scale;
        }

        private const string HandPath = "Rig_Medium/root/hips/spine/chest/upperarm.r/lowerarm.r/wrist.r/hand.r/handslot.r";
        // The "Hammer" layer holds one state per tool, each driven by its own phase parameter.
        private static readonly int HammerState = Animator.StringToHash("Hammer");
        private static readonly int SawState = Animator.StringToHash("Saw");
        private static readonly int PhaseId = Animator.StringToHash("HammerPhase");
        private static readonly int SawPhaseId = Animator.StringToHash("SawPhase");

        [SerializeField] private GameObject _hammerPrefab;
        [SerializeField] private Vector3 _hammerPosition;
        [SerializeField] private Vector3 _hammerEuler;
        [SerializeField] private Vector3 _hammerScale = Vector3.one;
        [Tooltip("The saw held at the workbench (CR-10).")]
        [SerializeField] private SawSettings _saw = new() { Scale = Vector3.one };

        private NetworkPlayer _player;
        private PlayerWork _work;
        private Animator _animator;
        private int _layer = -1;
        private Transform _hand;
        private GameObject _hammer;
        private GameObject _sawProp;
        private Quaternion _restRotation;
        private AnimatorCullingMode _restCulling;
        private HandTool? _playing;

        /// <summary>True while this player works at any hand-work station (hammer or saw).</summary>
        public bool IsWorking { get; private set; }

        /// <summary>True while this player hammers at the anvil.</summary>
        public bool IsHammering => IsWorking && _playing == HandTool.Hammer;

        public GameObject Hammer => _hammer;

        private void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
            _work = GetComponent<PlayerWork>();
        }

        private void OnDisable()
        {
            StopWorking();
        }

        private void OnDestroy()
        {
            if (_hammer != null) Destroy(_hammer);
            if (_sawProp != null) Destroy(_sawProp);
        }

        private void Update()
        {
            var animator = _player.VisualAnimator;
            if (animator != _animator)
            {
                StopWorking();
                if (_hammer != null) Destroy(_hammer);
                if (_sawProp != null) Destroy(_sawProp);
                _hammer = null;
                _sawProp = null;
                _animator = animator;
                _layer = animator != null ? animator.GetLayerIndex("Hammer") : -1;
                _hand = animator != null ? animator.transform.Find(HandPath) : null;
                if (animator != null)
                {
                    _restRotation = animator.transform.localRotation;
                    _restCulling = animator.cullingMode;
                }
            }

            HandWorkEffects active = null;
            if (_work.IsSpawned && _animator != null && _layer >= 0 && _hand != null)
            {
                foreach (var effects in HandWorkEffects.All)
                {
                    if (effects.Station != null && effects.Station.ActiveWorker == _work)
                    {
                        active = effects;
                        break;
                    }
                }
            }
            if (active == null)
            {
                StopWorking();
                return;
            }

            var tool = active.Tool;
            if (_playing != tool || !IsWorking)
            {
                // Start (or change) the tool's state at once: no blend from another tool's pose.
                _animator.Play(tool == HandTool.Saw ? SawState : HammerState, _layer, 0f);
                _playing = tool;
            }
            ShowProp(tool);
            IsWorking = true;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.SetLayerWeight(_layer, 1f);
            _animator.SetFloat(tool == HandTool.Saw ? SawPhaseId : PhaseId, active.AnimationPhase);

            // Face the work with the skin only: player movement and network transform remain authoritative.
            var forward = active.Station.ItemPoint.position - transform.position;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.001f)
                _animator.transform.rotation = Quaternion.LookRotation(forward);
        }

        // The prop of the tool in the hand, the other one hidden.
        private void ShowProp(HandTool tool)
        {
            if (tool == HandTool.Hammer && _hammer == null && _hammerPrefab != null)
            {
                _hammer = CreateProp(_hammerPrefab, "WorkHammer", _hammerPosition, _hammerEuler, _hammerScale);
            }
            if (tool == HandTool.Saw && _sawProp == null && _saw.Prefab != null)
            {
                _sawProp = CreateProp(_saw.Prefab, "WorkSaw", _saw.Position, _saw.Euler, _saw.Scale);
            }
            if (_hammer != null) _hammer.SetActive(tool == HandTool.Hammer);
            if (_sawProp != null) _sawProp.SetActive(tool == HandTool.Saw);
        }

        private GameObject CreateProp(GameObject prefab, string propName, Vector3 position, Vector3 euler, Vector3 scale)
        {
            var prop = Instantiate(prefab, _hand, false);
            prop.name = propName;
            prop.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(euler));
            prop.transform.localScale = scale;
            return prop;
        }

        private void StopWorking()
        {
            if (_animator != null && _layer >= 0)
            {
                _animator.SetLayerWeight(_layer, 0f);
                if (IsWorking)
                {
                    _animator.transform.localRotation = _restRotation;
                    _animator.cullingMode = _restCulling;
                }
            }
            IsWorking = false;
            if (_hammer != null) _hammer.SetActive(false);
            if (_sawProp != null) _sawProp.SetActive(false);
        }
    }
}
