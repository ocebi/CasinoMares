using System;
using System.Collections.Generic;

namespace CasinoMares.Core
{
    /// <summary>
    /// A tiny 5x7 pixel font plus 7x7 card suits, used to paint card faces, the roulette wheel and slot reels
    /// into runtime textures without any font or art assets.
    /// </summary>
    public static class PixelFont
    {
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;

        static readonly Dictionary<char, string[]> Glyphs = new()
        {
            ['0'] = new[] { ".XXX.", "X...X", "X..XX", "X.X.X", "XX..X", "X...X", ".XXX." },
            ['1'] = new[] { "..X..", ".XX..", "..X..", "..X..", "..X..", "..X..", ".XXX." },
            ['2'] = new[] { ".XXX.", "X...X", "....X", "...X.", "..X..", ".X...", "XXXXX" },
            ['3'] = new[] { "XXXX.", "....X", "....X", ".XXX.", "....X", "....X", "XXXX." },
            ['4'] = new[] { "...X.", "..XX.", ".X.X.", "X..X.", "XXXXX", "...X.", "...X." },
            ['5'] = new[] { "XXXXX", "X....", "XXXX.", "....X", "....X", "X...X", ".XXX." },
            ['6'] = new[] { "..XX.", ".X...", "X....", "XXXX.", "X...X", "X...X", ".XXX." },
            ['7'] = new[] { "XXXXX", "....X", "...X.", "..X..", ".X...", ".X...", ".X..." },
            ['8'] = new[] { ".XXX.", "X...X", "X...X", ".XXX.", "X...X", "X...X", ".XXX." },
            ['9'] = new[] { ".XXX.", "X...X", "X...X", ".XXXX", "....X", "...X.", ".XX.." },
            ['A'] = new[] { ".XXX.", "X...X", "X...X", "XXXXX", "X...X", "X...X", "X...X" },
            ['B'] = new[] { "XXXX.", "X...X", "X...X", "XXXX.", "X...X", "X...X", "XXXX." },
            ['C'] = new[] { ".XXX.", "X...X", "X....", "X....", "X....", "X...X", ".XXX." },
            ['E'] = new[] { "XXXXX", "X....", "X....", "XXXX.", "X....", "X....", "XXXXX" },
            ['F'] = new[] { "XXXXX", "X....", "X....", "XXXX.", "X....", "X....", "X...." },
            ['H'] = new[] { "X...X", "X...X", "X...X", "XXXXX", "X...X", "X...X", "X...X" },
            ['I'] = new[] { ".XXX.", "..X..", "..X..", "..X..", "..X..", "..X..", ".XXX." },
            ['J'] = new[] { "..XXX", "...X.", "...X.", "...X.", "...X.", "X..X.", ".XX.." },
            ['K'] = new[] { "X...X", "X..X.", "X.X..", "XX...", "X.X..", "X..X.", "X...X" },
            ['L'] = new[] { "X....", "X....", "X....", "X....", "X....", "X....", "XXXXX" },
            ['M'] = new[] { "X...X", "XX.XX", "X.X.X", "X.X.X", "X...X", "X...X", "X...X" },
            ['N'] = new[] { "X...X", "XX..X", "X.X.X", "X..XX", "X...X", "X...X", "X...X" },
            ['O'] = new[] { ".XXX.", "X...X", "X...X", "X...X", "X...X", "X...X", ".XXX." },
            ['P'] = new[] { "XXXX.", "X...X", "X...X", "XXXX.", "X....", "X....", "X...." },
            ['Q'] = new[] { ".XXX.", "X...X", "X...X", "X...X", "X.X.X", "X..X.", ".XX.X" },
            ['R'] = new[] { "XXXX.", "X...X", "X...X", "XXXX.", "X.X..", "X..X.", "X...X" },
            ['S'] = new[] { ".XXXX", "X....", "X....", ".XXX.", "....X", "....X", "XXXX." },
            ['T'] = new[] { "XXXXX", "..X..", "..X..", "..X..", "..X..", "..X..", "..X.." },
            ['U'] = new[] { "X...X", "X...X", "X...X", "X...X", "X...X", "X...X", ".XXX." },
            ['W'] = new[] { "X...X", "X...X", "X...X", "X.X.X", "X.X.X", "XX.XX", "X...X" },
            ['Y'] = new[] { "X...X", "X...X", ".X.X.", "..X..", "..X..", "..X..", "..X.." },
        };

        /// <summary>7x7 suits in card suit order: spades, hearts, diamonds, clubs.</summary>
        public static readonly string[][] Suits =
        {
            new[] { "...X...", "..XXX..", ".XXXXX.", "XXXXXXX", "XXXXXXX", ".X.X.X.", "...X..." },
            new[] { ".XX.XX.", "XXXXXXX", "XXXXXXX", "XXXXXXX", ".XXXXX.", "..XXX..", "...X..." },
            new[] { "...X...", "..XXX..", ".XXXXX.", "XXXXXXX", ".XXXXX.", "..XXX..", "...X..." },
            new[] { "..XXX..", "..XXX..", "XX.X.XX", "XXXXXXX", "XX.X.XX", "...X...", "..XXX.." },
        };

        public static string[] Glyph(char c) => Glyphs.TryGetValue(char.ToUpperInvariant(c), out var glyph) ? glyph : null;

        /// <summary>Width in pixels of a string at a scale (1 px gap between glyphs).</summary>
        public static int MeasureWidth(string text, int scale) =>
            text.Length == 0 ? 0 : text.Length * (GlyphWidth + 1) * scale - scale;

        /// <summary>Draw a bitmap glyph; <paramref name="plot"/> receives pixel coordinates, y pointing down.</summary>
        public static void Draw(Action<int, int> plot, string[] glyph, int x, int y, int scale)
        {
            if (glyph == null)
                return;
            for (var gy = 0; gy < glyph.Length; gy++)
            for (var gx = 0; gx < glyph[gy].Length; gx++)
            {
                if (glyph[gy][gx] != 'X')
                    continue;
                for (var sy = 0; sy < scale; sy++)
                for (var sx = 0; sx < scale; sx++)
                    plot(x + gx * scale + sx, y + gy * scale + sy);
            }
        }

        public static void DrawText(Action<int, int> plot, string text, int x, int y, int scale)
        {
            foreach (var c in text)
            {
                Draw(plot, Glyph(c), x, y, scale);
                x += (GlyphWidth + 1) * scale;
            }
        }
    }
}
