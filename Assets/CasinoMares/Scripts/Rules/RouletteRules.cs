using System;

namespace CasinoMares.Rules
{
    /// <summary>
    /// European single-zero roulette (GDD §13.1). A bet is placed on a "spot": 0-36 are straight-up numbers,
    /// the rest are outside bets. Zero loses every outside bet (no en prison / la partage).
    /// </summary>
    public static class RouletteRules
    {
        public const int Numbers = 37;
        public const int Red = 37;
        public const int Black = 38;
        public const int Odd = 39;
        public const int Even = 40;
        public const int Low = 41;
        public const int High = 42;
        public const int Dozen1 = 43;
        public const int Column1 = 46;
        public const int SpotCount = 49;

        public const int MinBet = 5;
        public const int MaxTotalBet = 500;

        /// <summary>Pocket order clockwise around a European wheel, starting at zero.</summary>
        public static readonly int[] WheelOrder =
        {
            0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10,
            5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26,
        };

        static readonly bool[] RedNumbers = BuildRed();

        static bool[] BuildRed()
        {
            var red = new bool[Numbers];
            foreach (var n in new[] { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 })
                red[n] = true;
            return red;
        }

        public static bool IsRed(int number) => number > 0 && number < Numbers && RedNumbers[number];

        public static bool IsBlack(int number) => number > 0 && number < Numbers && !RedNumbers[number];

        public static int PocketIndex(int number) => Array.IndexOf(WheelOrder, number);

        public static bool Wins(int spot, int result)
        {
            if (spot < Numbers)
                return spot == result;
            if (result == 0)
                return false;

            return spot switch
            {
                Red => IsRed(result),
                Black => IsBlack(result),
                Odd => result % 2 == 1,
                Even => result % 2 == 0,
                Low => result <= 18,
                High => result >= 19,
                >= Dozen1 and < Dozen1 + 3 => (result - 1) / 12 == spot - Dozen1,
                >= Column1 and < Column1 + 3 => (result - 1) % 3 == spot - Column1,
                _ => false,
            };
        }

        /// <summary>Chips returned for a winning bet, stake included: 35:1 straight, 2:1 dozens/columns, 1:1 even-money.</summary>
        public static int Payout(int spot, int amount, int result)
        {
            if (!Wins(spot, result))
                return 0;
            if (spot < Numbers)
                return amount * 36;
            return spot >= Dozen1 ? amount * 3 : amount * 2;
        }

        public static string SpotLabel(int spot) => spot switch
        {
            < Numbers => spot.ToString(),
            Red => "Red",
            Black => "Black",
            Odd => "Odd",
            Even => "Even",
            Low => "1-18",
            High => "19-36",
            >= Dozen1 and < Dozen1 + 3 => $"{(spot - Dozen1) * 12 + 1}-{(spot - Dozen1 + 1) * 12}",
            >= Column1 and < Column1 + 3 => $"Column {spot - Column1 + 1}",
            _ => "?",
        };

        public static string ColorName(int number) => number == 0 ? "Green" : IsRed(number) ? "Red" : "Black";
    }
}
