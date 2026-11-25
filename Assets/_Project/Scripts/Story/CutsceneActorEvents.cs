using UnityEngine;

namespace OverSmithed.Story
{
    /// <summary>Receives the animation events of a cutscene actor's model (added by <see cref="CutsceneActor"/>).</summary>
    public class CutsceneActorEvents : MonoBehaviour
    {
        private CutsceneActor _actor;

        public void Setup(CutsceneActor actor)
        {
            _actor = actor;
        }

        // Animation event of Menu_HammerLoop.
        private void OnHammerHit()
        {
            if (_actor != null)
            {
                _actor.HandleHammerHit();
            }
        }
    }
}
