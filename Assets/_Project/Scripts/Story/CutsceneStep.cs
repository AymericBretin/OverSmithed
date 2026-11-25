using UnityEngine;

namespace OverSmithed.Story
{
    /// <summary>
    /// One thing that happens in a cutscene shot, at <see cref="_startTime"/> seconds after the shot starts: an actor
    /// walks, plays an animation, a light fades, an object appears... Put step components on children of a
    /// <see cref="CutsceneShot"/>; they run in hierarchy order. Looks only, on every machine: only the shot number
    /// travels on the network.
    /// <see cref="Complete"/> jumps a step to its end (the players went to the next shot early, or a client joined
    /// late), so the next shot always starts from the same state.
    /// </summary>
    public abstract class CutsceneStep : MonoBehaviour
    {
        [Tooltip("Seconds after the start of the shot.")]
        [SerializeField, Min(0f)] private float _startTime;

        private bool _begun;
        private bool _done;

        public float StartTime => _startTime;

        /// <summary>Seconds the step lasts (0 = instant).</summary>
        public virtual float Length => 0f;

        public float EndTime => _startTime + Length;

        /// <summary>
        /// The actor this step makes act (walk, animate, turn), or null. While it acts, the camera should show it
        /// (checked by the framing test).
        /// </summary>
        public virtual CutsceneActor FocusActor => null;

        /// <summary>Seconds the actor acts (a one-shot animation lasts longer than its instant step).</summary>
        public virtual float FocusLength => Length;

        public void ResetStep()
        {
            _begun = false;
            _done = false;
        }

        /// <summary>Plays the step at this time of its shot (s since the shot started).</summary>
        public void Tick(float shotTime, float deltaTime)
        {
            if (_done || shotTime < _startTime)
            {
                return;
            }
            if (!_begun)
            {
                _begun = true;
                Begin(false);
            }
            var length = Length;
            var t = length <= 0f ? 1f : Mathf.Clamp01((shotTime - _startTime) / length);
            Evaluate(t, deltaTime);
            if (t >= 1f)
            {
                _done = true;
                Finish();
            }
        }

        /// <summary>Jumps to the end of the step (start it first if needed).</summary>
        public void Complete()
        {
            if (_done)
            {
                return;
            }
            if (!_begun)
            {
                _begun = true;
                Begin(true);
            }
            Evaluate(1f, 0f);
            _done = true;
            Finish();
        }

        /// <summary>The step starts. <paramref name="skipping"/>: it starts only to be jumped to its end.</summary>
        protected virtual void Begin(bool skipping) { }

        /// <summary>
        /// <paramref name="t"/> 0..1 through the step. <paramref name="deltaTime"/> is 0 when the step jumps to its end.
        /// </summary>
        protected virtual void Evaluate(float t, float deltaTime) { }

        protected virtual void Finish() { }

        protected static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
