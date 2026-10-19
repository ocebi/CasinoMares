using CasinoMares.Player;
using UnityEngine;

namespace CasinoMares.Editor
{
    /// <summary>
    /// The victory cigar (GDD §5.8). <see cref="CatCigar"/> builds the cigar and its smoke from primitives at runtime
    /// on the first light, so the player prefab only needs the component and the materials it builds them with: the
    /// cigar's body, band and ember, and the dust motes' soft additive material for the smoke and the ember's glow.
    /// NPCs don't win, so only the player prefab gets one.
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        static void AddVictoryCigar(GameObject root)
        {
            var cigar = root.AddComponent<CatCigar>();
            var body = Lit("CigarBody", new Color(0.33f, 0.19f, 0.1f), 0.35f);
            var band = Lit("CigarBand", new Color(0.85f, 0.65f, 0.2f), 0.7f, 0.6f);
            var ember = Emissive("CigarEmber", new Color(0.9f, 0.3f, 0.08f), new Color(1f, 0.32f, 0.06f) * 2f);
            // The hall builder makes DustMote; it's only missing when the prefab is rebuilt on its own before any hall.
            var smoke = Load<Material>($"{Root}/Art/Materials/DustMote.mat");
            if (smoke == null)
                smoke = SoftAdditive("DustMote", SoftDotTexture(), new Color(1f, 0.85f, 0.65f) * 0.9f);
            SetFields(cigar, ("_bodyMaterial", body), ("_bandMaterial", band), ("_emberMaterial", ember), ("_smokeMaterial", smoke));
        }
    }
}
