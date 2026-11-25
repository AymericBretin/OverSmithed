using UnityEngine;

namespace OverSmithed.Story
{
    /// <summary>Shows or hides an object (the note appears on the anvil).</summary>
    public class ActiveStep : CutsceneStep
    {
        [SerializeField] private GameObject _target;
        [SerializeField] private bool _active = true;

        protected override void Begin(bool skipping)
        {
            if (_target != null)
            {
                _target.SetActive(_active);
            }
        }
    }
}
