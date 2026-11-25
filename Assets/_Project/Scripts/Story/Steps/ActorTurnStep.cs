using UnityEngine;

namespace OverSmithed.Story
{
    /// <summary>An actor turns on the spot to face <see cref="_target"/> (e.g. looks at another apprentice).</summary>
    public class ActorTurnStep : CutsceneStep
    {
        [SerializeField] private CutsceneActor _actor;
        [SerializeField] private Transform _target;
        [SerializeField, Min(0.01f)] private float _duration = 0.4f;

        public override CutsceneActor FocusActor => _actor;

        private Quaternion _from;
        private Quaternion _to;

        public override float Length => _duration;

        protected override void Begin(bool skipping)
        {
            if (_actor == null || !_actor.IsPresent || _target == null)
            {
                return;
            }
            _from = _actor.transform.rotation;
            var toTarget = _target.position - _actor.transform.position;
            toTarget.y = 0f;
            _to = toTarget.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toTarget) : _from;
        }

        protected override void Evaluate(float t, float deltaTime)
        {
            if (_actor != null && _actor.IsPresent && _target != null)
            {
                _actor.transform.rotation = Quaternion.Slerp(_from, _to, Smooth(t));
            }
        }
    }
}
