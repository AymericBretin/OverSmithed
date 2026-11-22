using UnityEngine;

namespace OverSmithed.Core
{
    /// <summary>A level boundary that blocks players without blocking loose or thrown items.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class PlayerBarrier : MonoBehaviour
    {
        public const string LayerName = "PlayerBarrier";

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (Application.isPlaying || !TryGetComponent<BoxCollider>(out var box))
                return;

            var oldColor = Gizmos.color;
            var oldMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.45f, 0.08f, 0.22f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(1f, 0.45f, 0.08f, 1f);
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = oldMatrix;
            Gizmos.color = oldColor;
        }
#endif
    }
}
