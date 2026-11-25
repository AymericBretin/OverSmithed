using UnityEngine;

namespace OverSmithed.Story
{
    /// <summary>
    /// An actor plays an Animator state (Hammer, Interact, PickUp, Cheer...). One-shot states go back to Move by
    /// themselves (controller transitions). When the step is jumped over, a one-shot is not played (it would play in
    /// the next shot); a looping state (<see cref="_loops"/>) is.
    /// </summary>
    public class ActorAnimStep : CutsceneStep
    {
        [SerializeField] private CutsceneActor _actor;
        [SerializeField] private string _state = "Interact";
        [SerializeField, Min(0f)] private float _fade = 0.15f;
        [Tooltip("On for a state that keeps playing (Hammer): it is still started when the step is jumped over.")]
        [SerializeField] private bool _loops;

        public override CutsceneActor FocusActor => _actor;
        public override float FocusLength => _loops ? 0f : 1.2f;

        protected override void Begin(bool skipping)
        {
            if (_actor != null && _actor.IsPresent && (!skipping || _loops))
            {
                _actor.Play(_state, skipping ? 0f : _fade);
            }
        }
    }
}
