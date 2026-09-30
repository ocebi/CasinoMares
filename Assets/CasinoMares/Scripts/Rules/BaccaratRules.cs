using System;

namespace CasinoMares.Rules
{
    public enum BaccaratOutcome : byte
    {
        None,
        Player,
        Banker,
        Tie,
    }

    /// <summary>
    /// Punto Banco baccarat (GDD §13.4): fixed drawing rules, bets on Player (1:1), Banker (1:1 less 5%
    /// commission) or Tie (8:1). Player and Banker bets push on a tie.
    /// </summary>
    public static class BaccaratRules
    {
        public const int Decks = 8;
        public const int SpotPlayer = 0;
        public const int SpotBanker = 1;
        public const int SpotTie = 2;
        public const int Spots = 3;
        public const int MinBet = 10;
        public const int MaxTotalBet = 500;

        public static readonly string[] SpotNames = { "Player", "Banker", "Tie" };

        /// <summary>Ace = 1, 2-9 face value, tens and faces = 0.</summary>
        public static int CardValue(byte card)
        {
            var rank = Cards.Rank(card);
            return rank >= Cards.Ten ? 0 : rank + 1;
        }

        public static int Total(ReadOnlySpan<byte> cards)
        {
            var total = 0;
            foreach (var card in cards)
                total += CardValue(card);
            return total % 10;
        }

        public static bool IsNatural(int twoCardTotal) => twoCardTotal >= 8;

        /// <summary>The player draws a third card on 0-5 and stands on 6-7 (after checking for naturals).</summary>
        public static bool PlayerDraws(int playerTotal) => playerTotal <= 5;

        /// <summary>Banker drawing rules. <paramref name="playerThirdCard"/> is the card's value, or -1 if the player stood.</summary>
        public static bool BankerDraws(int bankerTotal, int playerThirdCard)
        {
            if (playerThirdCard < 0)
                return bankerTotal <= 5;

            return bankerTotal switch
            {
                <= 2 => true,
                3 => playerThirdCard != 8,
                4 => playerThirdCard is >= 2 and <= 7,
                5 => playerThirdCard is >= 4 and <= 7,
                6 => playerThirdCard is 6 or 7,
                _ => false,
            };
        }

        public static BaccaratOutcome Compare(int playerTotal, int bankerTotal) =>
            playerTotal > bankerTotal ? BaccaratOutcome.Player : bankerTotal > playerTotal ? BaccaratOutcome.Banker : BaccaratOutcome.Tie;

        /// <summary>Chips returned for a bet on a spot, stake included.</summary>
        public static int Payout(int spot, int amount, BaccaratOutcome outcome)
        {
            if (amount <= 0 || outcome == BaccaratOutcome.None)
                return 0;

            return spot switch
            {
                SpotPlayer => outcome == BaccaratOutcome.Player ? amount * 2 : outcome == BaccaratOutcome.Tie ? amount : 0,
                SpotBanker => outcome == BaccaratOutcome.Banker ? amount + amount * 95 / 100 : outcome == BaccaratOutcome.Tie ? amount : 0,
                SpotTie => outcome == BaccaratOutcome.Tie ? amount * 9 : 0,
                _ => 0,
            };
        }

        /// <summary>
        /// Deals a complete coup from the shoe in casino order (P, B, P, B, then third cards by the rules).
        /// Returns the card counts; cards are written into the given buffers (3 slots each).
        /// </summary>
        public static (int playerCount, int bankerCount) DealCoup(Shoe shoe, Span<byte> player, Span<byte> banker)
        {
            player[0] = shoe.Draw();
            banker[0] = shoe.Draw();
            player[1] = shoe.Draw();
            banker[1] = shoe.Draw();

            var playerTotal = Total(player.Slice(0, 2));
            var bankerTotal = Total(banker.Slice(0, 2));
            if (IsNatural(playerTotal) || IsNatural(bankerTotal))
                return (2, 2);

            var playerCount = 2;
            var playerThird = -1;
            if (PlayerDraws(playerTotal))
            {
                player[2] = shoe.Draw();
                playerThird = CardValue(player[2]);
                playerCount = 3;
            }

            var bankerCount = 2;
            if (BankerDraws(bankerTotal, playerThird))
            {
                banker[2] = shoe.Draw();
                bankerCount = 3;
            }

            return (playerCount, bankerCount);
        }
    }
}
