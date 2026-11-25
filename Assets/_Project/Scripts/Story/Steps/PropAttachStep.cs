using UnityEngine;

namespace OverSmithed.Story
{
    /// <summary>
    /// Moves a prop (the hammer) into an actor's hand bone or onto a place in the scene, ending at the given local
    /// pose. <see cref="_moveTime"/> 0 = at once, else it slides there.
    /// </summary>
    public class PropAttachStep : CutsceneStep
    {
        [SerializeField] private Transform _prop;
        [Tooltip("Attach to this actor's bone...")]
        [SerializeField] private CutsceneActor _actor;
        [SerializeField] private string _bone = "handslot.r";
        [Tooltip("...or, with no actor, to this place in the scene.")]
        [SerializeField] private Transform _place;
        [Header("Pose in the new parent")]
        [SerializeField] private Vector3 _localPosition;
        [SerializeField] private Vector3 _localEuler;
        [SerializeField] private Vector3 _localScale = Vector3.one;
        [SerializeField, Min(0f)] private float _moveTime;

        private Vector3 _fromPosition;
        private Quaternion _fromRotation;
        private Vector3 _fromScale;
        private bool _attached;

        public override float Length => _moveTime;

        protected override void Begin(bool skipping)
        {
            _attached = false;
            if (_prop == null)
            {
                return;
            }
            Transform parent;
            if (_actor != null)
            {
                parent = _actor.IsPresent ? _actor.FindBone(_bone) : null;
            }
            else
            {
                parent = _place;
            }
            if (parent == null)
            {
                return;
            }
            _prop.SetParent(parent, true);
            _fromPosition = _prop.localPosition;
            _fromRotation = _prop.localRotation;
            _fromScale = _prop.localScale;
            _attached = true;
        }

        protected override void Evaluate(float t, float deltaTime)
        {
            if (!_attached)
            {
                return;
            }
            var k = deltaTime > 0f ? Smooth(t) : 1f;
            _prop.localPosition = Vector3.Lerp(_fromPosition, _localPosition, k);
            _prop.localRotation = Quaternion.Slerp(_fromRotation, Quaternion.Euler(_localEuler), k);
            _prop.localScale = Vector3.Lerp(_fromScale, _localScale, k);
        }
    }
}
