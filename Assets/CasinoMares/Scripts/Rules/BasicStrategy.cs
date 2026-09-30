using System;

namespace CasinoMares.Rules
{
    /// <summary>
    /// Basic strategy for the house rules (6 decks, S17, double after split, no surrender). Used by NPC players.
    /// </summary>
    public static class BasicStrategy
    {
        public static BlackjackAction Decide(ReadOnlySpan<byte> hand, byte dealerUpCard, bool canDouble, bool canSplit)
        {
            var up = Cards.BlackjackValue(dealerUpCard); // 2..11

            if (canSplit && ShouldSplit(Cards.BlackjackValue(hand[0]), up))
                return BlackjackAction.Split;

            var value = BlackjackRules.Evaluate(hand);
            return value.IsSoft ? Soft(value.Total, up, canDouble) : Hard(value.Total, up, canDouble);
        }

        static bool ShouldSplit(int pairValue, int up) => pairValue switch
        {
            11 => true,
            10 => false,
            9 => up is >= 2 and <= 9 && up != 7,
            8 => true,
            7 => up <= 7,
            6 => up <= 6,
            5 => false,
            4 => up is 5 or 6,
            _ => up <= 7, // 2s and 3s
        };

        static BlackjackAction Soft(int total, int up, bool canDouble)
        {
            switch (total)
            {
                case >= 19:
                    return BlackjackAction.Stand;
                case 18:
                    if (up is >= 3 and <= 6)
                        return canDouble ? BlackjackAction.Double : BlackjackAction.Stand;
                    return up is 2 or 7 or 8 ? BlackjackAction.Stand : BlackjackAction.Hit;
                case 17:
                    return DoubleOrHit(up is >= 3 and <= 6, canDouble);
                case 16:
                case 15:
                    return DoubleOrHit(up is >= 4 and <= 6, canDouble);
                default: // soft 13-14
                    return DoubleOrHit(up is 5 or 6, canDouble);
            }
        }

        static BlackjackAction Hard(int total, int up, bool canDouble)
        {
            switch (total)
            {
                case >= 17:
                    return BlackjackAction.Stand;
                case >= 13:
                    return up <= 6 ? BlackjackAction.Stand : BlackjackAction.Hit;
                case 12:
                    return up is >= 4 and <= 6 ? BlackjackAction.Stand : BlackjackAction.Hit;
                case 11:
                    return DoubleOrHit(up <= 10, canDouble);
                case 10:
                    return DoubleOrHit(up <= 9, canDouble);
                case 9:
                    return DoubleOrHit(up is >= 3 and <= 6, canDouble);
                default:
                    return BlackjackAction.Hit;
            }
        }

        static BlackjackAction DoubleOrHit(bool wantsDouble, bool canDouble) =>
            wantsDouble && canDouble ? BlackjackAction.Double : BlackjackAction.Hit;
    }
}
