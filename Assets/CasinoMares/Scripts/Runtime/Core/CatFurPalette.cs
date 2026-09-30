using System;
using UnityEngine;

namespace CasinoMares.Core
{
    /// <summary>The fur colors a player can pick (GDD §3.2). The index is what gets networked and saved.</summary>
    [CreateAssetMenu(menuName = "Casino Mares/Cat Fur Palette")]
    public class CatFurPalette : ScriptableObject
    {
        [Serializable]
        public struct FurOption
        {
            public string Name;
            public Material Material;
            [Tooltip("Color used for UI swatches and nameplate accents.")]
            public Color Swatch;
        }

        [SerializeField] FurOption[] _options = Array.Empty<FurOption>();

        public int Count => _options.Length;

        public FurOption Get(int index) => _options[Mathf.Clamp(index, 0, _options.Length - 1)];

        public int Clamp(int index) => Mathf.Clamp(index, 0, _options.Length - 1);
    }
}
