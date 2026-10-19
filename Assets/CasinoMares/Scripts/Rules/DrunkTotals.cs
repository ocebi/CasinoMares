using System;

namespace CasinoMares.Rules
{
    /// <summary>
    /// The two totals a tipsy cat reads on its own blackjack hand (GDD §5.8): the real one and a plausible decoy,
    /// e.g. "17 or 21?". Pure and deterministic: the decoy and the order come from a hash of what identifies the hand
    /// (table, round, seat, hand, card count), so the label holds still until a card is added. The decoy is never the
    /// real total and never a bust value, so it can't tip off a bust hand; those show "Bust" as usual.
    /// </summary>
    public static class DrunkTotals
    {
        public const int MinTotal = 4;
        public const int MaxTotal = 21;
        /// <summary>How far from the real total the decoy may land, either way.</summary>
        public const int Spread = 4;

        /// <summary>A stable hash of a hand's identity (FNV-1a over the fields).</summary>
        public static int Hash(uint tableId, int roundId, int seat, int hand, int cardCount)
        {
            unchecked
            {
                var h = 2166136261u;
                h = (h ^ tableId) * 16777619u;
                h = (h ^ (uint)roundId) * 16777619u;
                h = (h ^ (uint)seat) * 16777619u;
                h = (h ^ (uint)hand) * 16777619u;
                h = (h ^ (uint)cardCount) * 16777619u;
                // Fold the top bits down so the low bits (which pick the decoy) see every field.
                h ^= h >> 16;
                return (int)h;
            }
        }

        /// <summary>
        /// The two candidates for a hand worth <paramref name="trueTotal"/> (4..21), in the order to show them. The
        /// real total is first about half the time.
        /// </summary>
        public static (int first, int second) Candidates(int trueTotal, int hash)
        {
            trueTotal = Math.Max(MinTotal, Math.Min(MaxTotal, trueTotal));
            var low = Math.Max(MinTotal, trueTotal - Spread);
            var high = Math.Min(MaxTotal, trueTotal + Spread);
            // Every value in low..high except the real one.
            var options = high - low;
            var bits = (uint)hash;
            var decoy = low + (int)(bits % (uint)options);
            if (decoy >= trueTotal)
                decoy++;

            var trueFirst = ((bits >> 24) & 1u) == 0u;
            return trueFirst ? (trueTotal, decoy) : (decoy, trueTotal);
        }
    }
}
