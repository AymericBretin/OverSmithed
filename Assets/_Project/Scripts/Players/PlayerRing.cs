using UnityEngine;

namespace OverSmithed.Players
{
    /// <summary>
    /// Colored ring on the floor under the players of this machine, so each person finds their own character.
    /// In couch play every player has one; online, the players of other machines have none.
    /// The color comes from the player's local index (1st player of the machine blue, 2nd red...).
    /// Purely local: it reads values the network already syncs, nothing is sent.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class PlayerRing : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("Flat quad under the feet, with the PlayerRing material.")]
        [SerializeField] private Renderer _ring;
        [Tooltip("Color of each player of this machine, by local index (1st, 2nd, 3rd, 4th).")]
        [SerializeField] private Color[] _colors =
        {
            new(0.20f, 0.55f, 1.00f),
            new(1.00f, 0.27f, 0.25f),
            new(0.30f, 0.85f, 0.35f),
            new(1.00f, 0.85f, 0.20f),
        };

        private NetworkPlayer _player;
        private MaterialPropertyBlock _block;
        private bool _shown;
        private int _colorIndex = -1;

        private void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
            _ring.enabled = false;
        }

        // Checked every frame (cheap): the owner and the local index are only known once the player is spawned.
        private void LateUpdate()
        {
            var show = _player.IsSpawned && _player.IsOwner && _colors.Length > 0;
            if (show != _shown)
            {
                _shown = show;
                _ring.enabled = show;
            }
            if (!show)
            {
                return;
            }

            var colorIndex = _player.LocalIndex % _colors.Length;
            if (colorIndex == _colorIndex)
            {
                return;
            }
            _colorIndex = colorIndex;
            // A property block changes this ring only, not the shared material of every ring.
            _block ??= new MaterialPropertyBlock();
            _block.SetColor(BaseColorId, _colors[colorIndex]);
            _ring.SetPropertyBlock(_block);
        }
    }
}
