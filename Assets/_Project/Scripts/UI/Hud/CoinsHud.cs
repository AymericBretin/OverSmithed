using TMPro;
using UnityEngine;

namespace OverSmithed.UI
{
    /// <summary>
    /// The team's coins at the bottom left of the screen. <see cref="SetCoins"/> shows a new value with a small pop.
    /// Fake for now (always 0): the real team coins come with CR-6 (#28), which calls <see cref="SetCoins"/>.
    /// </summary>
    public class CoinsHud : MonoBehaviour
    {
        [SerializeField] private TMP_Text _coinsText;
        [Tooltip("Grows on each change (its pivot should be its center).")]
        [SerializeField] private RectTransform _popTarget;
        [SerializeField] private float _popScale = 1.35f;
        [SerializeField] private float _popDuration = 0.3f;

        private int _coins;
        private float _popLeft;

        public int Coins => _coins;

        private void Start()
        {
            _coinsText.text = _coins.ToString();
        }

        /// <summary>Shows the team's coins (pops when the value changes).</summary>
        public void SetCoins(int coins)
        {
            if (coins == _coins)
            {
                return;
            }
            _coins = coins;
            _coinsText.text = coins.ToString();
            _popLeft = _popDuration;
        }

        private void Update()
        {
            if (_popTarget == null || _popLeft <= 0f)
            {
                return;
            }
            _popLeft = Mathf.Max(0f, _popLeft - Time.deltaTime);
            var t = _popDuration > 0f ? _popLeft / _popDuration : 0f;
            _popTarget.localScale = Vector3.one * (1f + (_popScale - 1f) * t * t);
        }

        [ContextMenu("Add 20 coins (test)")]
        private void AddTestCoins()
        {
            SetCoins(_coins + 20);
        }
    }
}
