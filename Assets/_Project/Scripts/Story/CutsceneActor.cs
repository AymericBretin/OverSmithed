using OverSmithed.Networking;
using OverSmithed.Players;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.Story
{
    /// <summary>
    /// A character of a cutscene. Its look is either a fixed prefab (the old master: change <see cref="_visualPrefab"/>
    /// to give him a new model) or the skin of one of the players (apprentices: <see cref="_playerOrder"/> 0 = the
    /// leader, then the others in roster order; hidden when there is no such player). Every machine builds it from data
    /// it already has (roster + scene): nothing goes on the network. <see cref="CutsceneStep"/>s move and animate it.
    /// A new model must use the KayKit Rig_Medium skeleton (same bone names), else the animations don't play.
    /// </summary>
    public class CutsceneActor : MonoBehaviour
    {
        public enum LookSource
        {
            /// <summary><see cref="_visualPrefab"/>.</summary>
            Fixed,
            /// <summary>The skin of the player number <see cref="_playerOrder"/> (0 = leader).</summary>
            Player
        }

        [SerializeField] private LookSource _look = LookSource.Fixed;
        [Tooltip("Fixed look: the model (prefab with an Animator, KayKit Rig_Medium skeleton, facing +Z).")]
        [SerializeField] private GameObject _visualPrefab;
        [Tooltip("Fixed look: name shown in the dialogue box when this actor speaks.")]
        [SerializeField] private string _displayName = "Master";
        [Tooltip("Player look: 0 = the leader (first player of the host machine), 1..3 = the next players.")]
        [SerializeField, Range(0, 3)] private int _playerOrder;
        [Tooltip("Player look, used when there is no session (scene played straight from the editor).")]
        [SerializeField] private SkinLibrary _fallbackSkins;
        [Tooltip("Animator controller of every cutscene actor (states: Move blend tree on Speed, Hammer, Interact...).")]
        [SerializeField] private RuntimeAnimatorController _controller;

        [Header("Hammer hit (optional, Hammer state)")]
        [SerializeField] private ParticleSystem _hitSparks;
        [SerializeField, Min(0)] private int _sparkCount = 14;
        [SerializeField] private Light _hitLight;
        [SerializeField, Min(0f)] private float _flashIntensity = 4f;
        [SerializeField, Min(0.01f)] private float _flashTime = 0.18f;

        private static readonly int SpeedId = Animator.StringToHash("Speed");

        private GameObject _visual;
        private Animator _animator;
        private float _flash;

        /// <summary>False when this apprentice has no player (fewer than 4 players): steps leave it alone.</summary>
        public bool IsPresent => _visual != null;

        /// <summary>True while the actor is shown (present and not hidden by a step).</summary>
        public bool IsVisible => _visual != null && _visual.activeSelf;

        /// <summary>Name for the dialogue box: the skin name of the player, or the fixed name.</summary>
        public string DisplayName { get; private set; }

        public Animator Animator => _animator;

        private void Awake()
        {
            DisplayName = _displayName;
            BuildVisual();
            if (_hitLight != null)
            {
                _hitLight.intensity = 0f;
            }
        }

        private void BuildVisual()
        {
            var prefab = _visualPrefab;
            if (_look == LookSource.Player)
            {
                prefab = null;
                var skin = FindPlayerSkin();
                if (skin != null)
                {
                    prefab = skin.Prefab;
                    DisplayName = skin.DisplayName;
                }
            }
            if (prefab == null)
            {
                return;
            }
            _visual = Instantiate(prefab, transform);
            _visual.name = "Visual";
            _visual.transform.localPosition = Vector3.zero;
            _visual.transform.localRotation = Quaternion.identity;
            _animator = _visual.GetComponentInChildren<Animator>();
            if (_animator != null)
            {
                if (_controller != null)
                {
                    _animator.runtimeAnimatorController = _controller;
                }
                _animator.applyRootMotion = false;
                // Animation events (OnHammerHit) are sent to the Animator's object.
                _animator.gameObject.AddComponent<CutsceneActorEvents>().Setup(this);
            }
        }

        // The leader = first roster entry of the host machine (like the world map walker), then the others in order.
        private SkinDefinition FindPlayerSkin()
        {
            var roster = SessionRoster.Instance;
            if (roster == null || roster.Skins == null)
            {
                return _fallbackSkins != null && _playerOrder < _fallbackSkins.Count ? _fallbackSkins.Get(_playerOrder) : null;
            }
            var leader = -1;
            for (var i = 0; i < roster.Count; i++)
            {
                if (roster[i].ClientId == NetworkManager.ServerClientId)
                {
                    leader = i;
                    break;
                }
            }
            if (leader < 0)
            {
                leader = 0;
            }
            var order = 0;
            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < roster.Count; i++)
                {
                    var isLeader = i == leader;
                    if ((pass == 0) != isLeader)
                    {
                        continue;
                    }
                    if (order == _playerOrder)
                    {
                        return roster.Skins.Get(roster[i].SkinIndex);
                    }
                    order++;
                }
            }
            return null;
        }

        /// <summary>Crossfades to an Animator state (e.g. "Move", "Hammer", "Interact").</summary>
        public void Play(string state, float fade = 0.15f)
        {
            // A hidden actor has no running Animator: it starts from Move when it shows again.
            if (_animator == null || !_animator.isActiveAndEnabled || string.IsNullOrEmpty(state))
            {
                return;
            }
            if (fade <= 0f)
            {
                _animator.Play(state, 0, 0f);
            }
            else
            {
                _animator.CrossFadeInFixedTime(state, fade, 0);
            }
        }

        /// <summary>
        /// Speed of the Move blend tree: 0 idle, 0.4 walk, 1 run. Damped over 0.1 s; <paramref name="deltaTime"/> 0 = at once.
        /// </summary>
        public void SetSpeed(float speed, float deltaTime)
        {
            if (_animator == null || !_animator.isActiveAndEnabled)
            {
                return;
            }
            if (deltaTime > 0f)
            {
                _animator.SetFloat(SpeedId, speed, 0.1f, deltaTime);
            }
            else
            {
                _animator.SetFloat(SpeedId, speed);
            }
        }

        public void SetVisible(bool visible)
        {
            if (_visual != null && _visual.activeSelf != visible)
            {
                _visual.SetActive(visible);
            }
        }

        /// <summary>A bone of the model by name (e.g. "handslot.r"), null when not found.</summary>
        public Transform FindBone(string boneName)
        {
            if (_visual == null || string.IsNullOrEmpty(boneName))
            {
                return null;
            }
            foreach (var bone in _visual.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == boneName)
                {
                    return bone;
                }
            }
            return null;
        }

        internal void HandleHammerHit()
        {
            if (_hitSparks != null)
            {
                _hitSparks.Emit(_sparkCount);
            }
            _flash = 1f;
        }

        private void Update()
        {
            if (_flash <= 0f || _hitLight == null)
            {
                return;
            }
            _flash = Mathf.Max(0f, _flash - Time.deltaTime / _flashTime);
            _hitLight.intensity = _flashIntensity * _flash * _flash;
        }
    }
}
