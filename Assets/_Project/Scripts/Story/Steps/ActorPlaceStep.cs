using UnityEngine;

namespace OverSmithed.Story
{
    /// <summary>Puts an actor on a spot (position + facing of <see cref="_spot"/>), shows / hides it, starts a state.</summary>
    public class ActorPlaceStep : CutsceneStep
    {
        [SerializeField] private CutsceneActor _actor;
        [Tooltip("Where the actor stands (its forward = where the actor looks). Empty: stays where it is.")]
        [SerializeField] private Transform _spot;
        [SerializeField] private bool _visible = true;
        [Tooltip("Animator state started at once (Move = idle / walk / run blend). Empty: no change.")]
        [SerializeField] private string _state = "Move";

        protected override void Begin(bool skipping)
        {
            if (_actor == null || !_actor.IsPresent)
            {
                return;
            }
            if (_spot != null)
            {
                _actor.transform.SetPositionAndRotation(_spot.position, Quaternion.Euler(0f, _spot.eulerAngles.y, 0f));
            }
            _actor.SetVisible(_visible);
            _actor.SetSpeed(0f, 0f);
            _actor.Play(_state, 0f);
        }
    }
}
