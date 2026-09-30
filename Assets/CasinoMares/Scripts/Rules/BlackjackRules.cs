using System;

namespace CasinoMares.Rules
{
    public enum HandOutcome : byte
    {
        None,
        Blackjack,
        Win,
        Push,
        Lose,
        Bust,
    }

    public enum BlackjackAction : byte
    {
        Hit,
        Stand,
        Double,
        Split,
    }

    public readonly struct HandValue
    {
        public readonly int Total;
        public readonly bool IsSoft;

        public HandValue(int total, bool isSoft)
        {
            Total = total;
            IsSoft = isSoft;
        }

        public bool IsBust => Total > 21;

        public override string ToString() => IsSoft && Total < 21 ? $"Soft {Total}" : Total.ToString();
    }

    /// <summary>
    /// The house rules from GDD §5.2: 6 decks, dealer stands on soft 17, blackjack pays 3:2, double on any two
    /// cards, double after split, split up to 4 hands, split aces get one card each and can't be resplit,
    /// no insurance or surrender.
    /// </summary>
    public static class BlackjackRules
    {
        public const int Decks = 6;
        public const double Penetration = 0.75;
        public const int MaxHands = 4;
        public const int MaxCardsPerHand = 12;
        public const int MinBet = 10;
        public const int MaxBet = 500;

        public static HandValue Evaluate(ReadOnlySpan<byte> cards)
        {
            var total = 0;
            var aces = 0;
            foreach (var card in cards)
            {
                if (card == Cards.None)
                    continue;
                total += Cards.BlackjackValue(card);
                if (Cards.Rank(card) == Cards.Ace)
                    aces++;
            }

            while (total > 21 && aces > 0)
            {
                total -= 10;
                aces--;
            }

            return new HandValue(total, aces > 0);
        }

        /// <summary>Two-card 21 that didn't come from a split.</summary>
        public static bool IsNatural(ReadOnlySpan<byte> cards, bool fromSplit) =>
            !fromSplit && cards.Length == 2 && Evaluate(cards).Total == 21;

        public static bool CanDouble(ReadOnlySpan<byte> cards, bool splitAces) =>
            cards.Length == 2 && !splitAces && Evaluate(cards).Total < 21;

        public static bool CanSplit(ReadOnlySpan<byte> cards, int handCount, bool splitAces) =>
            cards.Length == 2
            && handCount < MaxHands
            && !splitAces
            && Cards.BlackjackValue(cards[0]) == Cards.BlackjackValue(cards[1]);

        /// <summary>S17: the dealer hits 16 or less and stands on every 17, soft or hard.</summary>
        public static bool DealerShouldHit(ReadOnlySpan<byte> cards) => Evaluate(cards).Total < 17;

        /// <summary>US hole card: the dealer checks for blackjack when showing an ace or a ten-value card.</summary>
        public static bool DealerShouldPeek(byte upCard) => Cards.BlackjackValue(upCard) >= 10;

        public static HandOutcome Settle(ReadOnlySpan<byte> player, bool fromSplit, ReadOnlySpan<byte> dealer)
        {
            var playerValue = Evaluate(player);
            if (playerValue.IsBust)
                return HandOutcome.Bust;

            var playerNatural = IsNatural(player, fromSplit);
            var dealerNatural = IsNatural(dealer, false);
            if (playerNatural)
                return dealerNatural ? HandOutcome.Push : HandOutcome.Blackjack;
            if (dealerNatural)
                return HandOutcome.Lose;

            var dealerValue = Evaluate(dealer);
            if (dealerValue.IsBust || playerValue.Total > dealerValue.Total)
                return HandOutcome.Win;
            return playerValue.Total == dealerValue.Total ? HandOutcome.Push : HandOutcome.Lose;
        }

        /// <summary>Chips returned to the player for a settled hand, stake included (0 when the hand lost).</summary>
        public static int Payout(HandOutcome outcome, int bet) => outcome switch
        {
            HandOutcome.Blackjack => bet + bet * 3 / 2,
            HandOutcome.Win => bet * 2,
            HandOutcome.Push => bet,
            _ => 0,
        };
    }
}
