using System;
using System.Collections.Generic;
using OverSmithed.Counters;
using OverSmithed.Items;
using UnityEngine;

namespace OverSmithed.Crafting
{
    /// <summary>
    /// The workbench looks (CR-10): the worker saws (<see cref="HandTool.Saw"/>), each push of the saw throws a puff
    /// of sawdust high enough to be seen above the worker's head, colored by the item being worked (light wood, brown
    /// leather), the item rocks on each stroke, and the saw lying on the bench is
    /// hidden while a player holds it. Like the anvil, the rhythm follows the station's replicated work time, so
    /// every machine shows the same strokes. Local only.
    /// </summary>
    public class WorkbenchEffects : HandWorkEffects
    {
        [Serializable]
        private struct DustColor
        {
            public ItemDefinition Item;
            public Color Color;
        }

        [Tooltip("Seconds for one saw stroke (push + pull). The animation loop has two strokes.")]
        [SerializeField, Min(0.05f)] private float _strokeTime = 0.5f;
        [Tooltip("Moment of each stroke (0..1) when the saw bites, matching the push in Player_SawLoop.")]
        [SerializeField, Range(0f, 1f)] private float _cutPhase = 0.35f;
        [SerializeField] private ParticleSystem _dust;
        [SerializeField, Min(1)] private int _dustPerStroke = 10;
        [SerializeField, Min(1)] private int _dustOnComplete = 26;
        [Tooltip("Height above ItemPoint where the dust comes out (the top of the item).")]
        [SerializeField, Min(0f)] private float _dustHeight = 0.12f;
        [SerializeField] private Color _defaultDustColor = new(0.86f, 0.72f, 0.5f);
        [Tooltip("How much the item squashes and rocks on each stroke (0 = not at all).")]
        [SerializeField, Range(0f, 0.4f)] private float _itemWobble = 0.14f;
        [SerializeField] private List<DustColor> _dustColors = new();
        [Tooltip("The saw lying on the bench: hidden while a player saws with it.")]
        [SerializeField] private GameObject _benchSaw;

        private int _lastStroke = int.MinValue;
        private Color _currentColor;

        public override HandTool Tool => HandTool.Saw;

        public override float AnimationPhase => Mathf.Repeat(_station.WorkSeconds / (2f * _strokeTime), 1f);

        protected override void OnEnable()
        {
            base.OnEnable();
            _lastStroke = int.MinValue;
            if (_station != null)
            {
                _station.ItemChanged += HandleItemChanged;
                _station.WorkCompleted += HandleWorkCompleted;
                HandleItemChanged(_station);
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (_station != null)
            {
                _station.ItemChanged -= HandleItemChanged;
                _station.WorkCompleted -= HandleWorkCompleted;
            }
            if (_dust != null)
            {
                _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (_benchSaw != null)
            {
                _benchSaw.SetActive(true);
            }
        }

        private void HandleItemChanged(Counter counter)
        {
            _lastStroke = int.MinValue;
            // The color of the item put here: the output that replaces it keeps the dust of what was cut.
            var recipe = _station.CurrentRecipe;
            if (recipe != null && recipe.Inputs.Count > 0)
            {
                _currentColor = ColorFor(recipe.Inputs[0]);
            }
        }

        private void HandleWorkCompleted(Station station)
        {
            Emit(_dustOnComplete);
            _lastStroke = int.MinValue;
        }

        private void Update()
        {
            if (_benchSaw != null)
            {
                var sawInHand = _station != null && _station.IsSpawned && _station.ActiveWorker != null;
                if (_benchSaw.activeSelf == sawInHand)
                {
                    _benchSaw.SetActive(!sawInHand);
                }
            }
            if (_station == null || !_station.IsSpawned || !_station.IsWorking || _station.CurrentRecipe == null
                || Time.deltaTime <= 0f)
            {
                return;
            }
            var stroke = Mathf.FloorToInt(_station.WorkSeconds / _strokeTime - _cutPhase);
            if (stroke != _lastStroke)
            {
                // A late observer shows the current stroke, never a backlog of old dust.
                var first = _lastStroke == int.MinValue;
                _lastStroke = stroke;
                if (!first || _station.WorkSeconds > _cutPhase * _strokeTime)
                {
                    Emit(_dustPerStroke);
                    WobbleItem();
                }
            }
        }

        // The item on the bench jumps a little on each stroke (its visual only, see ItemVisualMotion).
        private void WobbleItem()
        {
            var item = _station.Item;
            if (item != null && _itemWobble > 0f && item.TryGetComponent<ItemVisualMotion>(out var motion))
            {
                motion.Wobble(_itemWobble);
            }
        }

        private Color ColorFor(ItemDefinition item)
        {
            foreach (var entry in _dustColors)
            {
                if (entry.Item == item)
                {
                    return entry.Color;
                }
            }
            return _defaultDustColor;
        }

        private void Emit(int count)
        {
            if (_dust == null || _station == null)
            {
                return;
            }
            _dust.transform.position = _station.ItemPoint.position + Vector3.up * _dustHeight;
            var color = _currentColor.a > 0f ? _currentColor : _defaultDustColor;
            var emit = new ParticleSystem.EmitParams { startColor = color, applyShapeToPosition = true };
            _dust.Emit(emit, count);
        }
    }
}
