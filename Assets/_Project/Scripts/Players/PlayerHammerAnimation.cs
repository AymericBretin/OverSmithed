using OverSmithed.Crafting;
using UnityEngine;

namespace OverSmithed.Players
{
    /// <summary>
    /// Every machine poses the accepted worker from the station's replicated clock. The hammer is a local prop,
    /// attached to the current skin's hand; neither owner input nor local animation events decide who is working.
    /// </summary>
    [DefaultExecutionOrder(20)]
    [RequireComponent(typeof(NetworkPlayer), typeof(PlayerWork))]
    public class PlayerHammerAnimation : MonoBehaviour
    {
        private const string HandPath = "Rig_Medium/root/hips/spine/chest/upperarm.r/lowerarm.r/wrist.r/hand.r/handslot.r";
        private static readonly int PhaseId = Animator.StringToHash("HammerPhase");

        [SerializeField] private GameObject _hammerPrefab;
        [SerializeField] private Vector3 _hammerPosition;
        [SerializeField] private Vector3 _hammerEuler;
        [SerializeField] private Vector3 _hammerScale = Vector3.one;

        private NetworkPlayer _player;
        private PlayerWork _work;
        private Animator _animator;
        private int _layer = -1;
        private Transform _hand;
        private GameObject _hammer;
        private Quaternion _restRotation;
        private AnimatorCullingMode _restCulling;

        public bool IsHammering { get; private set; }
        public GameObject Hammer => _hammer;

        private void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
            _work = GetComponent<PlayerWork>();
        }

        private void OnDisable()
        {
            StopHammering();
        }

        private void OnDestroy()
        {
            if (_hammer != null) Destroy(_hammer);
        }

        private void Update()
        {
            var animator = _player.VisualAnimator;
            if (animator != _animator)
            {
                StopHammering();
                if (_hammer != null) Destroy(_hammer);
                _hammer = null;
                _animator = animator;
                _layer = animator != null ? animator.GetLayerIndex("Hammer") : -1;
                _hand = animator != null ? animator.transform.Find(HandPath) : null;
                if (animator != null)
                {
                    _restRotation = animator.transform.localRotation;
                    _restCulling = animator.cullingMode;
                }
            }

            AnvilEffects active = null;
            if (_work.IsSpawned && _animator != null && _layer >= 0 && _hand != null)
            {
                foreach (var effects in AnvilEffects.All)
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
                StopHammering();
                return;
            }

            if (_hammer == null && _hammerPrefab != null)
            {
                _hammer = Instantiate(_hammerPrefab, _hand, false);
                _hammer.name = "WorkHammer";
                _hammer.transform.SetLocalPositionAndRotation(_hammerPosition, Quaternion.Euler(_hammerEuler));
                _hammer.transform.localScale = _hammerScale;
            }
            IsHammering = true;
            if (_hammer != null) _hammer.SetActive(true);
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.SetLayerWeight(_layer, 1f);
            _animator.SetFloat(PhaseId, active.AnimationPhase);

            // Face the work with the skin only: player movement and network transform remain authoritative.
            var forward = active.Station.ItemPoint.position - transform.position;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.001f)
                _animator.transform.rotation = Quaternion.LookRotation(forward);
        }

        private void StopHammering()
        {
            if (_animator != null && _layer >= 0)
            {
                _animator.SetLayerWeight(_layer, 0f);
                if (IsHammering)
                {
                    _animator.transform.localRotation = _restRotation;
                    _animator.cullingMode = _restCulling;
                }
            }
            IsHammering = false;
            if (_hammer != null) _hammer.SetActive(false);
        }
    }
}
