using System;

namespace CasinoMares.Rules
{
    public enum PokerAdvice : byte
    {
        Fold,
        Check,
        Call,
        Raise,
        AllIn,
    }

    public enum StartingHandTier : byte
    {
        Weak,
        Marginal,
        Playable,
        Strong,
        Premium,
    }

    /// <summary>
    /// The teaching mode's poker math (GDD §13.2 coach): win chances by simulation, starting-hand strength by the
    /// Chen formula, and a simple suggestion that weighs the win chance against the price of calling. It's a
    /// beginner's guide, not a solver: no bluffs, no position play, no reading opponents.
    /// </summary>
    public static class PokerCoach
    {
        /// <summary>
        /// Chance to win the pot (a tie counts as a share) holding these two cards against
        /// <paramref name="opponents"/> random hands, estimated from <paramref name="iterations"/> random run-outs.
        /// Thread safe: uses no shared state.
        /// </summary>
        public static float Equity(byte hole1, byte hole2, ReadOnlySpan<byte> board, int opponents, int iterations, int seed)
        {
            opponents = Math.Clamp(opponents, 1, 8);
            if (iterations <= 0)
                return 0f;

            Span<bool> used = stackalloc bool[Cards.DeckSize];
            used[hole1] = true;
            used[hole2] = true;
            foreach (var card in board)
                used[card] = true;

            Span<byte> deck = stackalloc byte[Cards.DeckSize];
            var deckSize = 0;
            for (var card = 0; card < Cards.DeckSize; card++)
            {
                if (!used[card])
                    deck[deckSize++] = (byte)card;
            }

            var boardNeeded = 5 - board.Length;
            var draw = boardNeeded + opponents * 2;
            if (draw > deckSize)
                return 0f;

            Span<byte> seven = stackalloc byte[7];
            var rng = new XorShift32(seed);
            var won = 0.0;
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                // Partial shuffle: only the cards this run-out needs.
                for (var k = 0; k < draw; k++)
                {
                    var j = k + rng.Next(deckSize - k);
                    (deck[k], deck[j]) = (deck[j], deck[k]);
                }

                for (var i = 0; i < board.Length; i++)
                    seven[i] = board[i];
                for (var i = 0; i < boardNeeded; i++)
                    seven[board.Length + i] = deck[i];

                seven[5] = hole1;
                seven[6] = hole2;
                var mine = PokerRules.Evaluate(seven);

                var beaten = false;
                var ties = 0;
                for (var o = 0; o < opponents && !beaten; o++)
                {
                    seven[5] = deck[boardNeeded + o * 2];
                    seven[6] = deck[boardNeeded + o * 2 + 1];
                    var theirs = PokerRules.Evaluate(seven);
                    if (theirs > mine)
                        beaten = true;
                    else if (theirs.Score == mine.Score)
                        ties++;
                }

                if (!beaten)
                    won += 1.0 / (ties + 1);
            }

            return (float)(won / iterations);
        }

        /// <summary>
        /// Bill Chen's quick score for two starting cards: roughly 12+ is a monster, under 5 is usually a fold.
        /// </summary>
        public static int ChenScore(byte a, byte b)
        {
            var high = Math.Max(PokerRules.Rank(a), PokerRules.Rank(b));
            var low = Math.Min(PokerRules.Rank(a), PokerRules.Rank(b));

            double score = high switch
            {
                14 => 10,
                13 => 8,
                12 => 7,
                11 => 6,
                _ => high / 2.0,
            };

            if (high == low)
                return (int)Math.Ceiling(Math.Max(5, score * 2));

            if (Cards.Suit(a) == Cards.Suit(b))
                score += 2;

            var gap = high - low - 1;
            score -= gap switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                3 => 4,
                _ => 5,
            };

            // Connected or one-gapped cards below a queen can still make straights both ways.
            if (gap <= 1 && high < 12)
                score += 1;

            return (int)Math.Ceiling(score);
        }

        public static StartingHandTier Tier(int chenScore) => chenScore switch
        {
            >= 12 => StartingHandTier.Premium,
            >= 9 => StartingHandTier.Strong,
            >= 7 => StartingHandTier.Playable,
            >= 5 => StartingHandTier.Marginal,
            _ => StartingHandTier.Weak,
        };

        /// <summary>The share of the pot you must win for a call to break even: price / (pot after calling).</summary>
        public static float PotOdds(int toCall, int pot) => toCall <= 0 ? 0f : toCall / (float)(pot + toCall);

        /// <summary>
        /// A beginner-friendly suggestion. <paramref name="pot"/> is everything in the middle including this street's
        /// bets. Returns the action and, for a raise, the total to raise to.
        /// </summary>
        public static PokerAdvice Suggest(float equity, int opponents, bool preFlop, int toCall, int pot, int stack,
            int roundBet, int currentBet, int minRaiseTo, int bigBlind, out int raiseTo)
        {
            raiseTo = 0;
            var fairShare = 1f / (Math.Max(1, opponents) + 1);
            var ahead = equity >= Math.Max(0.5f, fairShare * 1.6f);

            if (ahead && stack > toCall)
            {
                // Pre-flop open to 3 big blinds (or 3x a raise); after the flop bet about two thirds of the pot.
                var target = preFlop
                    ? Math.Max(bigBlind * 3, currentBet * 3)
                    : currentBet + (int)Math.Round((pot + toCall) * 0.67);
                raiseTo = Math.Max(target, minRaiseTo);
                var maxTo = roundBet + stack;
                if (raiseTo >= maxTo * 0.8f)
                {
                    raiseTo = maxTo;
                    return PokerAdvice.AllIn;
                }

                return PokerAdvice.Raise;
            }

            if (toCall <= 0)
                return PokerAdvice.Check;

            // Calls that cost most of the stack need a bit more than break-even to be worth it.
            var margin = toCall >= stack * 0.5f ? 0.08f : 0.03f;
            return equity >= PotOdds(toCall, pot) + margin ? PokerAdvice.Call : PokerAdvice.Fold;
        }
    }
}
