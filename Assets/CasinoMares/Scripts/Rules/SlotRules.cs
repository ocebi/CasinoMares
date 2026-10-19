namespace CasinoMares.Rules
{
    /// <summary>How a slot win is celebrated, by how many times the bet it pays.</summary>
    public enum SlotWin
    {
        None,
        /// <summary>Less than ten times the bet: a chime and a hop.</summary>
        Small,
        /// <summary>Ten times the bet or more: coins and a BIG WIN banner.</summary>
        Big,
        /// <summary>Fifty times the bet or more: a bell the whole room hears and a line in the chat.</summary>
        Top,
    }

    /// <summary>
    /// Three-reel cat slot machine (GDD §13.3). Reels are weighted; three of a kind pays by symbol, and paws pay on
    /// their own (one paw hands the bet back, two pay four times). Return to player is about 73.6%.
    /// </summary>
    public static class SlotRules
    {
        public const int Reels = 3;
        public const int Fish = 0;
        public const int Milk = 1;
        public const int Yarn = 2;
        public const int Mouse = 3;
        public const int Paw = 4;
        public const int Seven = 5;
        public const int SymbolCount = 6;

        public static readonly int[] BetOptions = { 5, 10, 25, 50, 100 };

        public static readonly string[] Names = { "FISH", "MILK", "YARN", "MOUSE", "PAW", "7" };

        /// <summary>Relative frequency of each symbol on a reel.</summary>
        public static readonly int[] Weights = { 36, 24, 14, 8, 10, 4 };

        /// <summary>Multiplier (stake included) for three of a kind.</summary>
        public static readonly int[] ThreeOfAKind = { 3, 6, 12, 24, 50, 120 };

        public const int OnePaw = 1;
        public const int TwoPaws = 4;

        public static int WeightTotal
        {
            get
            {
                var total = 0;
                foreach (var weight in Weights)
                    total += weight;
                return total;
            }
        }

        /// <summary>Map a roll in [0, WeightTotal) to a symbol.</summary>
        public static int SymbolForRoll(int roll)
        {
            for (var symbol = 0; symbol < SymbolCount; symbol++)
            {
                roll -= Weights[symbol];
                if (roll < 0)
                    return symbol;
            }

            return SymbolCount - 1;
        }

        public static int Multiplier(int a, int b, int c)
        {
            if (a == b && b == c)
                return ThreeOfAKind[a];

            var paws = (a == Paw ? 1 : 0) + (b == Paw ? 1 : 0) + (c == Paw ? 1 : 0);
            return paws switch
            {
                2 => TwoPaws,
                1 => OnePaw,
                _ => 0,
            };
        }

        public static int Payout(int bet, int a, int b, int c) => bet * Multiplier(a, b, c);

        /// <summary>The first two reels match, so the third could make three of a kind: it slows down to tease.</summary>
        public static bool Teases(int a, int b) => a == b;

        public static SlotWin Tier(int bet, int payout)
        {
            if (bet <= 0 || payout <= bet)
                return SlotWin.None;
            if (payout >= bet * 50)
                return SlotWin.Top;
            return payout >= bet * 10 ? SlotWin.Big : SlotWin.Small;
        }
    }
}
