using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace OverSmithed.UI
{
    /// <summary>
    /// One place for the colors, text styles and sprites of the UI. <see cref="ThemedText"/> and
    /// <see cref="ThemedImage"/> read their look from here: change a value, then press "Apply theme" on this
    /// asset, and every UI prefab and open scene is updated.
    /// </summary>
    [CreateAssetMenu(fileName = "UiTheme", menuName = "OverSmithed/UI Theme")]
    public class UiTheme : ScriptableObject
    {
        [Serializable]
        public struct ColorEntry
        {
            [HideInInspector] public string Name; // Only makes the list readable in the Inspector.
            public UiColor Key;
            public Color Value;
        }

        [Serializable]
        public struct TextStyleEntry
        {
            [HideInInspector] public string Name;
            public UiTextStyle Key;
            [Tooltip("Empty: keep the font set on the text.")]
            public TMP_FontAsset Font;
            [Tooltip("Optional material preset of the font (outline, shadow...). Empty: the font's default material.")]
            public Material Material;
            public float Size;
            public FontStyles Style;
        }

        [Serializable]
        public struct SpriteEntry
        {
            [HideInInspector] public string Name;
            public UiSprite Key;
            public Sprite Sprite;
        }

        [SerializeField] private List<ColorEntry> _colors = new();
        [SerializeField] private List<TextStyleEntry> _textStyles = new();
        [SerializeField] private List<SpriteEntry> _sprites = new();

        public Color GetColor(UiColor key)
        {
            foreach (var entry in _colors)
            {
                if (entry.Key == key)
                {
                    return entry.Value;
                }
            }
            return DefaultColor(key);
        }

        public bool TryGetTextStyle(UiTextStyle key, out TextStyleEntry style)
        {
            foreach (var entry in _textStyles)
            {
                if (entry.Key == key)
                {
                    style = entry;
                    return true;
                }
            }
            style = default;
            return false;
        }

        /// <summary>The sprite for this key, or null if none is set yet.</summary>
        public Sprite GetSprite(UiSprite key)
        {
            if (key == UiSprite.None)
            {
                return null;
            }
            foreach (var entry in _sprites)
            {
                if (entry.Key == key)
                {
                    return entry.Sprite;
                }
            }
            return null;
        }

        private void OnValidate()
        {
            FillMissingEntries();
        }

        /// <summary>
        /// Gives every key of the enums an entry (with a default value), so a new color, style or sprite shows up
        /// in the Inspector by itself.
        /// </summary>
        public void FillMissingEntries()
        {
            foreach (UiColor key in Enum.GetValues(typeof(UiColor)))
            {
                if (_colors.FindIndex(e => e.Key == key) < 0)
                {
                    _colors.Add(new ColorEntry { Key = key, Value = DefaultColor(key) });
                }
            }
            foreach (UiTextStyle key in Enum.GetValues(typeof(UiTextStyle)))
            {
                if (_textStyles.FindIndex(e => e.Key == key) < 0)
                {
                    _textStyles.Add(DefaultTextStyle(key));
                }
            }
            foreach (UiSprite key in Enum.GetValues(typeof(UiSprite)))
            {
                if (key != UiSprite.None && _sprites.FindIndex(e => e.Key == key) < 0)
                {
                    _sprites.Add(new SpriteEntry { Key = key });
                }
            }

            for (var i = 0; i < _colors.Count; i++)
            {
                var entry = _colors[i];
                entry.Name = entry.Key.ToString();
                _colors[i] = entry;
            }
            for (var i = 0; i < _textStyles.Count; i++)
            {
                var entry = _textStyles[i];
                entry.Name = entry.Key.ToString();
                _textStyles[i] = entry;
            }
            for (var i = 0; i < _sprites.Count; i++)
            {
                var entry = _sprites[i];
                entry.Name = entry.Key.ToString();
                _sprites[i] = entry;
            }
        }

        // Cartoon medieval palette: warm wood, parchment, gold accents and a brown "ink" for text.
        private static Color DefaultColor(UiColor key)
        {
            return key switch
            {
                UiColor.White => Color.white,
                UiColor.Wood => Hex(0x8B5A2B),
                UiColor.WoodDark => Hex(0x5A3A22),
                UiColor.WoodLight => Hex(0xB07A45),
                UiColor.Parchment => Hex(0xF2E3C2),
                UiColor.ParchmentDark => Hex(0xD9C49A),
                UiColor.Gold => Hex(0xF2B33D),
                UiColor.GoldDark => Hex(0xB8801F),
                UiColor.TextDark => Hex(0x3B2716),
                UiColor.TextLight => Hex(0xFFF6E0),
                UiColor.Danger => Hex(0xD9483B),
                UiColor.Success => Hex(0x6DBA4A),
                UiColor.Disabled => Hex(0x9A8F80),
                UiColor.Shadow => new Color(0f, 0f, 0f, 0.5f),
                _ => Color.magenta,
            };
        }

        private static TextStyleEntry DefaultTextStyle(UiTextStyle key)
        {
            var (size, style) = key switch
            {
                UiTextStyle.Title => (72f, FontStyles.Bold),
                UiTextStyle.Heading => (48f, FontStyles.Bold),
                UiTextStyle.Body => (32f, FontStyles.Normal),
                UiTextStyle.Small => (24f, FontStyles.Normal),
                UiTextStyle.Button => (36f, FontStyles.Bold),
                _ => (32f, FontStyles.Normal),
            };
            return new TextStyleEntry { Key = key, Size = size, Style = style };
        }

        private static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
        }
    }
}
