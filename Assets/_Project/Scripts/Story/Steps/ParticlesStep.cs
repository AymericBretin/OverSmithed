using UnityEngine;

namespace OverSmithed.Story
{
    /// <summary>Starts or stops a particle system (fire embers, smoke...).</summary>
    public class ParticlesStep : CutsceneStep
    {
        [SerializeField] private ParticleSystem _particles;
        [SerializeField] private bool _play;

        protected override void Begin(bool skipping)
        {
            if (_particles == null)
            {
                return;
            }
            if (_play)
            {
                _particles.Play(true);
            }
            else
            {
                // Jumped over: nothing left in the air. Else the last particles finish their life.
                _particles.Stop(true, skipping ? ParticleSystemStopBehavior.StopEmittingAndClear
                    : ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }
}
