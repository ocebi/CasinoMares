using System;
using System.Collections.Generic;

namespace CasinoMares.Rules
{
    /// <summary>
    /// Yarn Tumble, the cascading slot machine: a 5×5 grid where a group of five or more of one symbol, touching side
    /// by side, pays. Winning groups get batted away, the symbols above fall into the gaps and new ones drop in from
    /// the top, often matching the one they land on, so wins tend to chain; every chain raises the multiplier by a
    /// half (×1, ×1.5, ×2 ... ×3.5). A spin is worked out from a seed, so every client replays the same cascade.
    /// A third of spins win (never less than the bet), one in eight chains at least twice, and the return to player
    /// is about 98%.
    /// </summary>
    public static class YarnTumbleRules
    {
        public const int Size = 5;
        public const int Cells = Size * Size;
        public const int MinGroup = 5;
        /// <summary>Groups pay more for each cell up to this size.</summary>
        public const int MaxPaidGroup = 10;
        /// <summary>The chance, in percent, that a new symbol drops in as a copy of the one it lands on.</summary>
        public const int RefillRepeat = 25;
        /// <summary>A safety stop; a spin practically never chains this often.</summary>
        public const int MaxCascades = 20;

        public const int BlueYarn = 0;
        public const int GreenYarn = 1;
        public const int PinkYarn = 2;
        public const int Fish = 3;
        public const int Mouse = 4;
        public const int GoldenPaw = 5;
        public const int SymbolCount = 6;

        public static readonly string[] Names = { "Blue yarn", "Green yarn", "Pink yarn", "Fish", "Mouse", "Golden paw" };

        /// <summary>Relative frequency of each symbol.</summary>
        public static readonly int[] Weights = { 28, 22, 18, 14, 11, 7 };

        /// <summary>What a group of five pays, in tenths of the bet. Each cell over five adds a quarter of that.</summary>
        public static readonly int[] GroupOfFive = { 10, 10, 10, 12, 14, 20 };

        /// <summary>Each chain's multiplier, in tenths.</summary>
        public static readonly int[] ChainMultipliers = { 10, 15, 20, 25, 30, 35 };

        /// <summary>A grid cell: columns left to right, rows from the bottom up.</summary>
        public static int Index(int column, int row) => column * Size + row;

        /// <summary>Chain <paramref name="cascade"/>'s multiplier, in tenths.</summary>
        public static int ChainMultiplier(int cascade) => ChainMultipliers[Math.Min(cascade, ChainMultipliers.Length - 1)];

        /// <summary>A multiplier in tenths as players read it: "1.5".</summary>
        public static string MultiplierText(int tenths) => tenths % 10 == 0 ? (tenths / 10).ToString() : $"{tenths / 10}.{tenths % 10}";

        /// <summary>A group's pay in tenths of the bet, before the chain multiplier.</summary>
        public static int GroupPay(int symbol, int size) =>
            size < MinGroup ? 0 : GroupOfFive[symbol] * (Math.Min(size, MaxPaidGroup) - 1) / 4;

        /// <summary>Chips for a spin that paid <paramref name="tenths"/> tenths of the bet.</summary>
        public static int Payout(int bet, int tenths) => bet * tenths / 10;

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

        /// <summary>One chain: the cells that won, and the grid once the rest fell and new symbols dropped in.</summary>
        public sealed class Cascade
        {
            public readonly bool[] Won = new bool[Cells];
            public readonly int[] After = new int[Cells];
            /// <summary>
            /// For each cell of <see cref="After"/>, the row it fell from: a row of the grid before, or
            /// <see cref="Size"/> and up for a new symbol (<see cref="Size"/> is just above the top row).
            /// </summary>
            public readonly int[] FromRow = new int[Cells];
            /// <summary>In tenths.</summary>
            public int Multiplier;
            /// <summary>This chain's pay in tenths of the bet, multiplier included.</summary>
            public int Pay;
        }

        public sealed class Spin
        {
            public readonly int[] Start = new int[Cells];
            public readonly List<Cascade> Cascades = new();
            /// <summary>The whole spin's pay in tenths of the bet.</summary>
            public int Pay;

            /// <summary>The grid before chain <paramref name="cascade"/> (or at rest, after the last one).</summary>
            public int[] Before(int cascade) => cascade <= 0 ? Start : Cascades[Math.Min(cascade, Cascades.Count) - 1].After;

            public int[] Final => Before(Cascades.Count);

            /// <summary>Pay in tenths from the first <paramref name="cascades"/> chains.</summary>
            public int PayThrough(int cascades)
            {
                var pay = 0;
                for (var i = 0; i < cascades && i < Cascades.Count; i++)
                    pay += Cascades[i].Pay;
                return pay;
            }
        }

        public static Spin Play(int seed)
        {
            var random = new Rng(seed);
            var spin = new Spin();
            for (var cell = 0; cell < Cells; cell++)
                spin.Start[cell] = Roll(ref random);

            var grid = spin.Start;
            var seen = new bool[Cells];
            var stack = new int[Cells];
            var group = new int[Cells];
            while (spin.Cascades.Count < MaxCascades)
            {
                var cascade = new Cascade { Multiplier = ChainMultiplier(spin.Cascades.Count) };
                var pay = FindGroups(grid, cascade.Won, seen, stack, group);
                if (pay == 0)
                    break;

                cascade.Pay = pay * cascade.Multiplier / 10;
                spin.Pay += cascade.Pay;
                for (var column = 0; column < Size; column++)
                {
                    var row = 0;
                    for (var from = 0; from < Size; from++)
                    {
                        if (cascade.Won[Index(column, from)])
                            continue;
                        cascade.After[Index(column, row)] = grid[Index(column, from)];
                        cascade.FromRow[Index(column, row)] = from;
                        row++;
                    }

                    for (var fresh = Size; row < Size; row++, fresh++)
                    {
                        var repeat = row > 0 && random.Next(100) < RefillRepeat;
                        cascade.After[Index(column, row)] = repeat ? cascade.After[Index(column, row - 1)] : Roll(ref random);
                        cascade.FromRow[Index(column, row)] = fresh;
                    }
                }

                spin.Cascades.Add(cascade);
                grid = cascade.After;
            }

            return spin;
        }

        static readonly int[] StepColumn = { -1, 1, 0, 0 };
        static readonly int[] StepRow = { 0, 0, -1, 1 };

        /// <summary>Marks every group of <see cref="MinGroup"/> or more in <paramref name="won"/> and returns their pay.</summary>
        static int FindGroups(int[] grid, bool[] won, bool[] seen, int[] stack, int[] group)
        {
            Array.Clear(seen, 0, seen.Length);
            var pay = 0;
            for (var start = 0; start < Cells; start++)
            {
                if (seen[start])
                    continue;

                var symbol = grid[start];
                var size = 0;
                var top = 0;
                stack[top++] = start;
                seen[start] = true;
                while (top > 0)
                {
                    var cell = stack[--top];
                    group[size++] = cell;
                    for (var step = 0; step < 4; step++)
                    {
                        var column = cell / Size + StepColumn[step];
                        var row = cell % Size + StepRow[step];
                        if (column < 0 || column >= Size || row < 0 || row >= Size)
                            continue;
                        var next = Index(column, row);
                        if (seen[next] || grid[next] != symbol)
                            continue;
                        seen[next] = true;
                        stack[top++] = next;
                    }
                }

                if (size < MinGroup)
                    continue;
                pay += GroupPay(symbol, size);
                for (var i = 0; i < size; i++)
                    won[group[i]] = true;
            }

            return pay;
        }

        static int Roll(ref Rng random) => SymbolForRoll(random.Next(WeightTotal));

        /// <summary>xorshift32: the same numbers on every platform, unlike System.Random, whose algorithm may change.</summary>
        struct Rng
        {
            uint _state;

            public Rng(int seed)
            {
                _state = unchecked((uint)seed * 2654435761u) ^ 0x9E3779B9u;
                if (_state == 0)
                    _state = 1;
                // Neighbouring seeds start out alike; a few rounds spread them apart.
                for (var i = 0; i < 4; i++)
                    Next(2);
            }

            public int Next(int max)
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return (int)(_state % (uint)max);
            }
        }
    }
}
