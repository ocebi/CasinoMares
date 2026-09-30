namespace CasinoMares.Rules
{
    /// <summary>
    /// Three-reel cat slot machine (GDD §13.3). Reels are weighted; three of a kind pays by symbol, paws pay on
    /// their own (one paw returns the bet, two pay triple). Return to player is about 98%.
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
        public static readonly int[] Weights = { 30, 24, 18, 12, 10, 6 };

        /// <summary>Multiplier (stake included) for three of a kind.</summary>
        public static readonly int[] ThreeOfAKind = { 8, 12, 20, 40, 60, 150 };

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
                2 => 3,
                1 => 1,
                _ => 0,
            };
        }

        public static int Payout(int bet, int a, int b, int c) => bet * Multiplier(a, b, c);
    }
}
