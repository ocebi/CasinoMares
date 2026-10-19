using System;
using UnityEngine;

namespace CasinoMares.Core
{
    /// <summary>
    /// The fur colors a player can pick (GDD §3.2). The index is what gets networked and saved. The black fur ships in
    /// the build; the recolored ones are Addressables (<see cref="LazyContent"/>), so a cat shows the black fur until
    /// its own has loaded.
    /// </summary>
    [CreateAssetMenu(menuName = "Casino Mares/Cat Fur Palette")]
    public class CatFurPalette : ScriptableObject
    {
        [Serializable]
        public struct FurOption
        {
            public string Name;
            [Tooltip("Shown until the Address has loaded (the black fur for the recolored ones).")]
            public Material Material;
            [Tooltip("Addressables address of the fur material; empty when Material is the fur itself.")]
            public string Address;
            [Tooltip("Color used for UI swatches and nameplate accents.")]
            public Color Swatch;
        }

        [SerializeField] FurOption[] _options = Array.Empty<FurOption>();

        public int Count => _options.Length;

        public FurOption Get(int index) => _options[Mathf.Clamp(index, 0, _options.Length - 1)];

        public int Clamp(int index) => Mathf.Clamp(index, 0, _options.Length - 1);

        /// <summary>
        /// The fur material to show now: the real one if it has loaded, else the fallback. If it hasn't loaded yet, starts
        /// the load and calls <paramref name="loaded"/> when it arrives.
        /// </summary>
        public Material Resolve(int index, Action<Material> loaded)
        {
            var option = Get(index);
            var material = LazyContent.Get<Material>(option.Address);
            if (material != null)
                return material;
            LazyContent.Load(option.Address, loaded);
            return option.Material;
        }

        /// <summary>Starts loading a fur ahead of use (the menu's pick, before the player spawns).</summary>
        public void Preload(int index) => LazyContent.Load<Material>(Get(index).Address);
    }
}
