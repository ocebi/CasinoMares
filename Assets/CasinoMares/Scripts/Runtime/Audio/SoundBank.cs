using System;
using UnityEngine;

namespace CasinoMares.Audio
{
    public enum SoundId
    {
        CardSlide,
        CardPlace,
        Shuffle,
        ChipBet,
        ChipPayout,
        UiClick,
        Win,
        Lose,
        Tick,
        Notify,
        Meow,
        WheelSpin,
        BallDrop,

        // Synthesized in code (Sfx / CatVoice), not in the bank.
        Purr,
        Hiss,
        Chirp,
        Chatter,
        Yowl,
        Swish,
        PropClink,
        PropThud,
        BallTick,
        SlotChime,
        SlotBell,
        Coin,
    }

    /// <summary>Clip variations per sound (GDD §11). Built by the scene builder from the Kenney CC0 packs.</summary>
    [CreateAssetMenu(menuName = "Casino Mares/Sound Bank")]
    public class SoundBank : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public SoundId Id;
            public AudioClip[] Clips;
            [Range(0f, 1f)] public float Volume;
            public Vector2 PitchRange;
        }

        [SerializeField] Entry[] _entries = Array.Empty<Entry>();

        public bool TryGet(SoundId id, out Entry entry)
        {
            foreach (var candidate in _entries)
            {
                if (candidate.Id == id && candidate.Clips != null && candidate.Clips.Length > 0)
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = default;
            return false;
        }
    }
}
