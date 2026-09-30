using CasinoMares.Audio;

namespace CasinoMares.Player
{
    /// <summary>
    /// Emotes on the emote wheel. The value is networked (a byte in an RPC) and is also the Animator's "Emote"
    /// parameter, so don't reorder.
    /// </summary>
    public enum CatEmote : byte
    {
        None,
        Meow,
        Purr,
        Groom,
        Pounce,
        Hiss,
        Wiggle,
        PlayDead,
        Happy,
    }

    public static class CatEmotes
    {
        /// <summary>Wheel order, clockwise from the top.</summary>
        public static readonly CatEmote[] Wheel =
        {
            CatEmote.Meow, CatEmote.Purr, CatEmote.Groom, CatEmote.Happy,
            CatEmote.Pounce, CatEmote.Wiggle, CatEmote.Hiss, CatEmote.PlayDead,
        };

        public static string Label(CatEmote emote) => emote switch
        {
            CatEmote.Meow => "Meow",
            CatEmote.Purr => "Sit & purr",
            CatEmote.Groom => "Groom",
            CatEmote.Pounce => "Pounce",
            CatEmote.Hiss => "Hiss",
            CatEmote.Wiggle => "Butt wiggle",
            CatEmote.PlayDead => "Play dead",
            CatEmote.Happy => "Happy hop",
            _ => "",
        };

        /// <summary>The emote's sound and when it starts, in seconds after the emote begins.</summary>
        public static bool TryGetSound(CatEmote emote, out SoundId sound, out float delay, out float volume)
        {
            (sound, delay, volume) = emote switch
            {
                // The sitting emotes sit down first (about 0.8 s).
                CatEmote.Meow => (SoundId.Meow, 0.85f, 1f),
                CatEmote.Purr => (SoundId.Purr, 0.8f, 1f),
                CatEmote.Groom => (SoundId.Purr, 1.2f, 0.6f),
                CatEmote.Pounce => (SoundId.Chirp, 0.35f, 1f),
                CatEmote.Hiss => (SoundId.Hiss, 0.05f, 1f),
                CatEmote.Wiggle => (SoundId.Chatter, 0.5f, 1f),
                CatEmote.PlayDead => (SoundId.Yowl, 0f, 1f),
                _ => (default, 0f, 0f),
            };
            return volume > 0f;
        }
    }
}
