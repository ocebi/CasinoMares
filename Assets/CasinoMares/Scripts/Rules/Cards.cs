namespace CasinoMares.Rules
{
    /// <summary>
    /// A playing card is one byte: 0..51, rank = card % 13 (0 = Ace ... 12 = King), suit = card / 13
    /// (0 = spades, 1 = hearts, 2 = diamonds, 3 = clubs). <see cref="None"/> marks an empty slot.
    /// </summary>
    public static class Cards
    {
        public const byte None = 255;
        public const int DeckSize = 52;
        public const int RanksPerSuit = 13;

        public const int Ace = 0;
        public const int Ten = 9;
        public const int Jack = 10;
        public const int Queen = 11;
        public const int King = 12;

        static readonly string[] RankLabels = { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };
        static readonly string[] SuitSymbols = { "♠", "♥", "♦", "♣" };

        public static byte Make(int rank, int suit) => (byte)(suit * RanksPerSuit + rank);

        public static int Rank(byte card) => card % RanksPerSuit;

        public static int Suit(byte card) => card / RanksPerSuit;

        public static bool IsRed(byte card) => Suit(card) is 1 or 2;

        public static string RankLabel(byte card) => RankLabels[Rank(card)];

        public static string SuitSymbol(byte card) => SuitSymbols[Suit(card)];

        public static string Label(byte card) => card == None ? "--" : RankLabel(card) + SuitSymbol(card);

        /// <summary>Blackjack value with aces counted as 11 (hand evaluation turns them into 1 as needed).</summary>
        public static int BlackjackValue(byte card)
        {
            var rank = Rank(card);
            if (rank == Ace)
                return 11;
            return rank >= Ten ? 10 : rank + 1;
        }
    }
}
