using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CasinoMares.Core
{
    /// <summary>
    /// Shortens the decorative lights on phones. A phone that has run hot is held up by pixel work, and under Forward+
    /// the cost of a pixel grows with every realtime light whose range reaches it: at the back wall by the roulette
    /// five do (the two lamps, the neon spill and two sconces). The scene builder names the sconces, the neon spill,
    /// the bar's shelf glow, the cat-wall lamp, the lanterns, the vestibule light and the lounge's fills with
    /// <see cref="Prefix"/>; on the Mobile quality level their range is cut to 70 %, which keeps the glow near each
    /// lamp and drops the faint tail that overlapped the next one. The lamps over the games are untouched. Only the
    /// range changes, in the loaded scene, so it's safe in the editor too (with the Mobile quality level selected).
    /// Each light's own range is remembered, so trimming twice (a scene loaded on top) doesn't shorten it again.
    /// <c>?lighttrim=0</c> turns it off, <c>?lighttrim=1</c> turns it on at any quality level, to compare.
    /// </summary>
    public static class MobileLightTrim
    {
        /// <summary>Name prefix the scene builder gives the ambience lights.</summary>
        public const string Prefix = "Ambience_";
        /// <summary>What's left of each ambience light's range.</summary>
        const float RangeFactor = 0.7f;

        /// <summary>Whether the trim is in force (for the debug overlay).</summary>
        public static bool Active { get; private set; }

        /// <summary>Each trimmed light's range before the trim.</summary>
        static readonly Dictionary<Light, float> _originalRange = new Dictionary<Light, float>();
        static readonly List<Light> _stale = new List<Light>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            var setting = Platform.QueryParameter("lighttrim");
            Active = setting != null ? setting != "0" : Platform.MobileQuality;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _originalRange.Clear();
            if (!Active)
                return;
            // The first scene is loaded already; SceneSelect may swap it for the other casino next.
            SceneManager.sceneLoaded += OnSceneLoaded;
            Trim();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Trim();

        static void Trim()
        {
            // Lights of an unloaded scene compare equal to null; forget them.
            _stale.Clear();
            foreach (var light in _originalRange.Keys)
                if (light == null)
                    _stale.Add(light);
            foreach (var light in _stale)
                _originalRange.Remove(light);

            var count = 0;
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (!light.name.StartsWith(Prefix, StringComparison.Ordinal))
                    continue;
                if (!_originalRange.TryGetValue(light, out var range))
                    _originalRange[light] = range = light.range;
                light.range = range * RangeFactor;
                count++;
            }

            Debug.Log($"[LightTrim] {count} ambience lights at x{RangeFactor:0.##} range");
        }
    }
}
