using System;
using System.Collections.Generic;

namespace CasinoMares.Rules
{
    public enum PokerCategory : byte
    {
        HighCard,
        Pair,
        TwoPair,
        Trips,
        Straight,
        Flush,
        FullHouse,
        Quads,
        StraightFlush,
    }

    /// <summary>A comparable poker hand strength: category in the high bits, then up to five kicker ranks.</summary>
    public readonly struct PokerHandValue : IComparable<PokerHandValue>
    {
        public readonly int Score;

        public PokerHandValue(int score) => Score = score;

        public PokerCategory Category => (PokerCategory)(Score >> 20);

        public int CompareTo(PokerHandValue other) => Score.CompareTo(other.Score);

        public static bool operator >(PokerHandValue a, PokerHandValue b) => a.Score > b.Score;
        public static bool operator <(PokerHandValue a, PokerHandValue b) => a.Score < b.Score;

        /// <summary>Rank (2..14) of the n-th kicker slot.</summary>
        public int Kicker(int index) => (Score >> (16 - index * 4)) & 0xF;

        public override string ToString()
        {
            var top = RankName(Kicker(0));
            return Category switch
            {
                PokerCategory.HighCard => $"{top} high",
                PokerCategory.Pair => $"Pair of {top}s",
                PokerCategory.TwoPair => $"Two pair, {top}s and {RankName(Kicker(1))}s",
                PokerCategory.Trips => $"Three {top}s",
                PokerCategory.Straight => $"Straight to {top}",
                PokerCategory.Flush => $"Flush, {top} high",
                PokerCategory.FullHouse => $"Full house, {top}s full of {RankName(Kicker(1))}s",
                PokerCategory.Quads => $"Four {top}s",
                PokerCategory.StraightFlush => Kicker(0) == 14 ? "Royal flush" : $"Straight flush to {top}",
                _ => "?",
            };
        }

        static string RankName(int rank) => rank switch
        {
            14 => "Ace",
            13 => "King",
            12 => "Queen",
            11 => "Jack",
            10 => "Ten",
            _ => rank.ToString(),
        };
    }

    /// <summary>No-limit Texas Hold'em helpers (GDD §13.2): hand evaluation and side pots.</summary>
    public static class PokerRules
    {
        /// <summary>Poker rank of a card: 2..14 with aces high.</summary>
        public static int Rank(byte card)
        {
            var rank = Cards.Rank(card);
            return rank == Cards.Ace ? 14 : rank + 1;
        }

        /// <summary>Best five-card hand out of 5 to 7 cards.</summary>
        public static PokerHandValue Evaluate(ReadOnlySpan<byte> cards)
        {
            if (cards.Length < 5)
                throw new ArgumentException("Need at least five cards.");

            var best = new PokerHandValue(-1);
            Span<byte> five = stackalloc byte[5];
            var n = cards.Length;
            for (var a = 0; a < n - 4; a++)
            for (var b = a + 1; b < n - 3; b++)
            for (var c = b + 1; c < n - 2; c++)
            for (var d = c + 1; d < n - 1; d++)
            for (var e = d + 1; e < n; e++)
            {
                five[0] = cards[a];
                five[1] = cards[b];
                five[2] = cards[c];
                five[3] = cards[d];
                five[4] = cards[e];
                var value = EvaluateFive(five);
                if (value > best)
                    best = value;
            }

            return best;
        }

        static PokerHandValue EvaluateFive(ReadOnlySpan<byte> cards)
        {
            Span<int> counts = stackalloc int[15];
            var flush = true;
            var suit = Cards.Suit(cards[0]);
            foreach (var card in cards)
            {
                counts[Rank(card)]++;
                if (Cards.Suit(card) != suit)
                    flush = false;
            }

            // Straight: five distinct consecutive ranks, or the wheel A-2-3-4-5.
            var straightHigh = 0;
            for (var high = 14; high >= 5; high--)
            {
                var run = true;
                for (var r = high; r > high - 5; r--)
                {
                    var rank = r == 1 ? 14 : r;
                    if (counts[rank] != 1)
                    {
                        run = false;
                        break;
                    }
                }

                if (run)
                {
                    straightHigh = high;
                    break;
                }
            }

            if (straightHigh == 0 && counts[14] == 1 && counts[2] == 1 && counts[3] == 1 && counts[4] == 1 && counts[5] == 1)
                straightHigh = 5;

            if (straightHigh > 0)
                return Make(flush ? PokerCategory.StraightFlush : PokerCategory.Straight, straightHigh);

            // Group ranks by count (desc) then rank (desc).
            Span<int> order = stackalloc int[5];
            var groups = 0;
            for (var count = 4; count >= 1; count--)
            for (var rank = 14; rank >= 2; rank--)
            {
                if (counts[rank] == count)
                    order[groups++] = rank;
            }

            var first = counts[order[0]];
            var second = groups > 1 ? counts[order[1]] : 0;

            if (flush)
                return Make(PokerCategory.Flush, order[0], order[1], order[2], order[3], order[4]);

            return first switch
            {
                4 => Make(PokerCategory.Quads, order[0], order[1]),
                3 when second == 2 => Make(PokerCategory.FullHouse, order[0], order[1]),
                3 => Make(PokerCategory.Trips, order[0], order[1], order[2]),
                2 when second == 2 => Make(PokerCategory.TwoPair, order[0], order[1], order[2]),
                2 => Make(PokerCategory.Pair, order[0], order[1], order[2], order[3]),
                _ => Make(PokerCategory.HighCard, order[0], order[1], order[2], order[3], order[4]),
            };
        }

        static PokerHandValue Make(PokerCategory category, int k0 = 0, int k1 = 0, int k2 = 0, int k3 = 0, int k4 = 0) =>
            new(((int)category << 20) | (k0 << 16) | (k1 << 12) | (k2 << 8) | (k3 << 4) | k4);

        public readonly struct Pot
        {
            public readonly int Amount;
            public readonly List<int> Eligible;

            public Pot(int amount, List<int> eligible)
            {
                Amount = amount;
                Eligible = eligible;
            }
        }

        /// <summary>
        /// Splits everyone's contributions into a main pot and side pots. Only players who haven't folded are
        /// eligible, and only for pots up to what they put in.
        /// </summary>
        public static List<Pot> BuildPots(IReadOnlyList<int> contributed, IReadOnlyList<bool> folded)
        {
            var levels = new SortedSet<int>();
            for (var i = 0; i < contributed.Count; i++)
            {
                if (contributed[i] > 0 && !folded[i])
                    levels.Add(contributed[i]);
            }

            var pots = new List<Pot>();
            var previous = 0;
            foreach (var level in levels)
            {
                var amount = 0;
                var eligible = new List<int>();
                for (var i = 0; i < contributed.Count; i++)
                {
                    amount += Math.Min(contributed[i], level) - Math.Min(contributed[i], previous);
                    if (!folded[i] && contributed[i] >= level)
                        eligible.Add(i);
                }

                if (amount > 0)
                    pots.Add(new Pot(amount, eligible));
                previous = level;
            }

            // Chips from folded players above every live player's level go to the last pot.
            var leftover = 0;
            for (var i = 0; i < contributed.Count; i++)
                leftover += Math.Max(0, contributed[i] - previous);
            if (leftover > 0 && pots.Count > 0)
            {
                var last = pots[^1];
                pots[^1] = new Pot(last.Amount + leftover, last.Eligible);
            }

            return pots;
        }
    }
}
