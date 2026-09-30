using CasinoMares.Core;
using CasinoMares.Rules;
using UnityEngine;

namespace CasinoMares.Slots
{
    /// <summary>
    /// Paints the reel strip (one cell per symbol, stacked vertically, symbol 0 at the bottom) at runtime.
    /// A reel shows symbol s with texture scale (1, 1/count) and offset (0, s/count).
    /// </summary>
    public static class SlotSymbols
    {
        const int Cell = 128;
        static Texture2D _strip;

        static readonly Color32[] Backgrounds =
        {
            new(70, 130, 190, 255), // fish
            new(235, 235, 225, 255), // milk
            new(200, 70, 130, 255), // yarn
            new(140, 140, 150, 255), // mouse
            new(230, 150, 60, 255), // paw
            new(200, 30, 40, 255), // seven
        };

        static readonly Color32[] Inks =
        {
            new(250, 250, 255, 255),
            new(50, 90, 170, 255),
            new(255, 240, 250, 255),
            new(30, 30, 36, 255),
            new(60, 30, 10, 255),
            new(255, 220, 90, 255),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _strip = null;

        public static Texture2D Strip => _strip ??= Build();

        public static Vector4 ScaleOffset(float symbolPosition) =>
            new(1f, 1f / SlotRules.SymbolCount, 0f, symbolPosition / SlotRules.SymbolCount);

        static Texture2D Build()
        {
            var height = Cell * SlotRules.SymbolCount;
            var pixels = new Color32[Cell * height];

            for (var symbol = 0; symbol < SlotRules.SymbolCount; symbol++)
            {
                var baseY = symbol * Cell;
                for (var y = 0; y < Cell; y++)
                for (var x = 0; x < Cell; x++)
                {
                    var border = x < 4 || x >= Cell - 4 || y < 4 || y >= Cell - 4;
                    pixels[(baseY + y) * Cell + x] = border ? new Color32(40, 24, 16, 255) : Backgrounds[symbol];
                }

                var text = SlotRules.Names[symbol];
                var scale = text.Length == 1 ? 10 : text.Length <= 4 ? 4 : 3;
                var width = PixelFont.MeasureWidth(text, scale);
                var left = (Cell - width) / 2;
                var top = baseY + (Cell + PixelFont.GlyphHeight * scale) / 2;
                var ink = Inks[symbol];
                PixelFont.DrawText((x, row) =>
                {
                    var y = top - row;
                    if (x >= 0 && x < Cell && y >= baseY && y < baseY + Cell)
                        pixels[y * Cell + x] = ink;
                }, text, left, 0, scale);
            }

            var texture = new Texture2D(Cell, height, TextureFormat.RGBA32, false)
            {
                name = "SlotStrip",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
