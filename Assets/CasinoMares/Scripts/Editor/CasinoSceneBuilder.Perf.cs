using CasinoMares.Core;
using UnityEngine;

namespace CasinoMares.Editor
{
    /// <summary>
    /// Phone performance (plan 2026-10-19 §9): a throttled phone is pixel-bound and the per-pixel cost is how many
    /// realtime lights reach each pixel. Marks the decorative point lights (sconces, the neon spill, the shelf glow,
    /// the cat-wall lamp, the lanterns, the vestibule and the lounge's fills) with the <see cref="MobileLightTrim"/>
    /// name prefix, so phones draw them with a shorter range; the pendant spots over the games and the slot machines'
    /// own lights (off on phones already, see SlotCelebration) keep their names. Also trims the two lights that still
    /// put a fourth light on a pixel on phones with the runtime trim in place: the lounge's centre fill (at the bar)
    /// and the neon's spill (at the roulette's far edge).
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        /// <summary>The lounge's centre fill reached the bar at 12 m; at 10 m × 0.7 (phones) it stops short of it (7.2 m).</summary>
        const float FillCenterRange = 10f;
        /// <summary>A touch brighter, so its middle stays as it was over the tables.</summary>
        const float FillCenterIntensity = 1.6f;
        /// <summary>
        /// The neon's spill reached the roulette's far edge (2.1 m) and the wall under the sign, where the two roulette
        /// lamps and two sconces already meet; at 2.6 m it still tints the wall and the floor under the sign, and on
        /// phones (× 0.7) it stops at the wall.
        /// </summary>
        const float NeonSpillRange = 2.6f;
        const float NeonSpillIntensity = 1.6f;

        /// <summary>
        /// Every point light that isn't a slot machine's is ambience: the lamps over the games are spots. Called once
        /// the whole scene (and the seaside) is built.
        /// </summary>
        static void TagAmbienceLights()
        {
            var count = 0;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (light.type != LightType.Point || light.name == "MachineLight" || light.name.StartsWith(MobileLightTrim.Prefix))
                    continue;
                switch (light.name)
                {
                    case "Fill_Center":
                        light.range = FillCenterRange;
                        light.intensity = FillCenterIntensity;
                        break;
                    case "Spill":
                        light.range = NeonSpillRange;
                        light.intensity = NeonSpillIntensity;
                        break;
                }

                light.name = MobileLightTrim.Prefix + light.name;
                count++;
            }

            Debug.Log($"[CasinoSceneBuilder] {count} ambience lights tagged for the phone light trim");
        }
    }
}
