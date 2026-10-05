using CasinoMares.Core;
using CasinoMares.Rules;
using UnityEngine;

namespace CasinoMares.Slots
{
    /// <summary>
    /// Paints the Yarn Tumble art at runtime: an atlas of square cells (the six symbols on rounded tiles, the glow
    /// behind a winning symbol, and the chain multiplier lamps, unlit and lit) and the machine's name sign. Around
    /// each tile the atlas is transparent, for the grid's alpha-clipped material, so the glow shows behind a symbol.
    /// </summary>
    public static class YarnTumbleSymbols
    {
        public const int Columns = 8;
        public const int Rows = 3;
        const int Cell = 128;

        /// <summary>The glow tile drawn behind a winning symbol.</summary>
        public const int GlowCell = YarnTumbleRules.SymbolCount;
        /// <summary>Chain multiplier lamps: unlit from here, then lit.</summary>
        public static int LampCell(int chain, bool lit) => Columns + chain + (lit ? Columns : 0);

        /// <summary>The grid window's colour; transparent in the atlas.</summary>
        public static readonly Color32 Window = new(22, 20, 44, 255);
        static readonly Color Clear = new Color32(22, 20, 44, 0);

        static readonly Color32[] Tiles =
        {
            new(48, 70, 128, 255), // blue yarn
            new(40, 92, 74, 255), // green yarn
            new(112, 46, 86, 255), // pink yarn
            new(36, 96, 120, 255), // fish
            new(92, 84, 100, 255), // mouse
            new(120, 30, 46, 255), // golden paw
        };

        static readonly Color[] Yarns =
        {
            new(0.35f, 0.6f, 1f),
            new(0.35f, 0.9f, 0.5f),
            new(1f, 0.45f, 0.75f),
        };

        static Texture2D _atlas;
        static Texture2D _sign;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _atlas = _sign = null;

        public static Texture2D Atlas => _atlas ??= BuildAtlas();
        public static Texture2D Sign => _sign ??= BuildSign();

        /// <summary>The UV rectangle of a cell: x, y of its bottom left corner, then width, height.</summary>
        public static Rect Uv(int cell) =>
            new((cell % Columns) / (float)Columns, (Rows - 1 - cell / Columns) / (float)Rows, 1f / Columns, 1f / Rows);

        static Texture2D BuildAtlas()
        {
            var width = Cell * Columns;
            var height = Cell * Rows;
            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = Clear;

            for (var symbol = 0; symbol < YarnTumbleRules.SymbolCount; symbol++)
                Paint(pixels, width, symbol, (x, y) => SymbolPixel(symbol, x, y));
            Paint(pixels, width, GlowCell, GlowPixel);

            for (var chain = 0; chain < YarnTumbleRules.ChainMultipliers.Length; chain++)
            {
                var text = "×" + YarnTumbleRules.MultiplierText(YarnTumbleRules.ChainMultipliers[chain]);
                Lamp(pixels, width, LampCell(chain, false), text, new Color32(52, 40, 60, 255), new Color32(120, 100, 120, 255));
                Lamp(pixels, width, LampCell(chain, true), text, new Color32(255, 196, 70, 255), new Color32(70, 30, 10, 255));
            }

            return Finish("YarnTumbleAtlas", width, height, pixels);
        }

        /// <summary>Fills one cell; <paramref name="pixel"/> gets coordinates from 0 to 1 within it, y up.</summary>
        static void Paint(Color32[] pixels, int width, int cell, System.Func<float, float, Color?> pixel)
        {
            var left = cell % Columns * Cell;
            var bottom = (Rows - 1 - cell / Columns) * Cell;
            for (var y = 0; y < Cell; y++)
            for (var x = 0; x < Cell; x++)
            {
                var color = pixel((x + 0.5f) / Cell, (y + 0.5f) / Cell);
                if (color.HasValue)
                    pixels[(bottom + y) * width + left + x] = color.Value;
            }
        }

        static Color? SymbolPixel(int symbol, float x, float y)
        {
            Color tile = Tiles[symbol];
            var color = Over(Clear, tile, Inside(RoundedBox(x, y, 0.42f, 0.14f)));
            // A lighter rim around the top of the tile.
            color = Over(color, Color.Lerp(tile, Color.white, 0.25f), Inside(RoundedBox(x, y, 0.42f, 0.14f)) * (1f - Inside(RoundedBox(x, y - 0.025f, 0.4f, 0.13f))));

            switch (symbol)
            {
                case YarnTumbleRules.BlueYarn:
                case YarnTumbleRules.GreenYarn:
                case YarnTumbleRules.PinkYarn:
                    return YarnBall(color, Yarns[symbol], x, y, symbol);
                case YarnTumbleRules.Fish:
                    return Fish(color, x, y);
                case YarnTumbleRules.Mouse:
                    return Mouse(color, x, y);
                default:
                    return Paw(color, x, y);
            }
        }

        static Color YarnBall(Color color, Color yarn, float x, float y, int seed)
        {
            const float radius = 0.3f;
            var ball = Inside(Circle(x, y, 0.5f, 0.5f, radius));
            // Shade towards the bottom right, then wind strands of yarn round it.
            var shade = Mathf.Clamp01(0.55f + (y - x) * 0.8f);
            var ballColor = Color.Lerp(yarn * 0.55f, yarn, shade);
            ballColor.a = 1f;
            color = Over(color, ballColor, ball);
            var tilt = seed * 0.6f;
            for (var strand = 0; strand < 4; strand++)
            {
                var angle = tilt + strand * 0.8f;
                var cx = 0.5f + Mathf.Cos(angle) * 0.42f;
                var cy = 0.5f + Mathf.Sin(angle) * 0.42f;
                var line = Inside(Mathf.Abs(Circle(x, y, cx, cy, 0.36f + strand * 0.02f)) - 0.012f) * ball;
                color = Over(color, Color.Lerp(yarn, Color.white, 0.45f), line * 0.9f);
            }

            // The loose end trailing off.
            var tail = Inside(Mathf.Abs(Circle(x, y, 0.95f, 0.45f, 0.3f)) - 0.014f) * (x > 0.72f && y < 0.42f ? 1f : 0f);
            return Over(color, yarn, tail * (1f - ball));
        }

        static Color Fish(Color color, float x, float y)
        {
            var body = Inside(Ellipse(x, y, 0.44f, 0.5f, 0.27f, 0.17f));
            var tail = Inside(Mathf.Max(Mathf.Abs(y - 0.5f) - (x - 0.64f) * 0.9f, Mathf.Max(0.66f - x, x - 0.86f)));
            var fish = new Color(1f, 0.6f, 0.2f);
            color = Over(color, fish, Mathf.Max(body, tail));
            color = Over(color, new Color(1f, 0.85f, 0.55f), body * Inside(0.47f - y));
            color = Over(color, new Color(0.15f, 0.08f, 0.05f), Inside(Circle(x, y, 0.3f, 0.54f, 0.035f)));
            // Gill line.
            return Over(color, new Color(0.8f, 0.4f, 0.12f), body * Inside(Mathf.Abs(Circle(x, y, 0.25f, 0.5f, 0.14f)) - 0.008f) * (x > 0.36f ? 1f : 0f));
        }

        static Color Mouse(Color color, float x, float y)
        {
            var grey = new Color(0.78f, 0.78f, 0.82f);
            var pink = new Color(1f, 0.62f, 0.7f);
            foreach (var side in new[] { -1f, 1f })
            {
                color = Over(color, grey, Inside(Circle(x, y, 0.5f + side * 0.2f, 0.68f, 0.14f)));
                color = Over(color, pink, Inside(Circle(x, y, 0.5f + side * 0.2f, 0.68f, 0.08f)));
            }

            color = Over(color, grey, Inside(Ellipse(x, y, 0.5f, 0.45f, 0.24f, 0.21f)));
            var ink = new Color(0.1f, 0.08f, 0.12f);
            color = Over(color, ink, Inside(Circle(x, y, 0.42f, 0.5f, 0.03f)));
            color = Over(color, ink, Inside(Circle(x, y, 0.58f, 0.5f, 0.03f)));
            color = Over(color, pink, Inside(Circle(x, y, 0.5f, 0.4f, 0.035f)));
            // Whiskers.
            foreach (var side in new[] { -1f, 1f })
            foreach (var slope in new[] { -0.25f, 0.1f })
            {
                var dx = (x - 0.5f) * side;
                var line = Mathf.Abs(y - 0.39f - dx * slope) - 0.007f;
                color = Over(color, new Color(0.35f, 0.33f, 0.38f), Inside(line) * (dx > 0.1f && dx < 0.34f ? 1f : 0f));
            }

            return color;
        }

        static Color Paw(Color color, float x, float y)
        {
            var gold = new Color(1f, 0.82f, 0.3f);
            var shine = new Color(1f, 0.95f, 0.7f);
            var pad = Inside(Ellipse(x, y, 0.5f, 0.4f, 0.17f, 0.14f));
            var toes = 0f;
            foreach (var (tx, ty) in new[] { (0.29f, 0.6f), (0.42f, 0.71f), (0.58f, 0.71f), (0.71f, 0.6f) })
                toes = Mathf.Max(toes, Inside(Ellipse(x, y, tx, ty, 0.075f, 0.09f)));
            var paw = Mathf.Max(pad, toes);
            color = Over(color, Color.Lerp(gold * 0.75f, gold, Mathf.Clamp01(0.4f + y)), paw);
            return Over(color, shine, paw * Inside(Circle(x, y, 0.44f, 0.46f, 0.04f)));
        }

        static Color? GlowPixel(float x, float y)
        {
            var edge = RoundedBox(x, y, 0.47f, 0.16f);
            var center = Mathf.Clamp01(1f - Mathf.Max(Mathf.Abs(x - 0.5f), Mathf.Abs(y - 0.5f)) * 2f);
            var glow = Color.Lerp(new Color(1f, 0.62f, 0.2f), new Color(1f, 0.97f, 0.75f), center * center);
            return Over(Clear, glow, Inside(edge));
        }

        static void Lamp(Color32[] pixels, int width, int cell, string text, Color32 lamp, Color32 ink)
        {
            Paint(pixels, width, cell, (x, y) => Over(Clear, lamp, Inside(RoundedBox(x, y, 0.48f, 0.16f))));
            // Small enough for "×3.5".
            const int scale = 5;
            var left = cell % Columns * Cell + (Cell - PixelFont.MeasureWidth(text, scale)) / 2;
            var top = (Rows - 1 - cell / Columns) * Cell + (Cell + PixelFont.GlyphHeight * scale) / 2;
            PixelFont.DrawText((x, row) => pixels[(top - row) * width + x] = ink, text, left, 0, scale);
        }

        static Texture2D BuildSign()
        {
            const int width = 512;
            const int height = 96;
            const int scale = 7;
            const string text = "YARN TUMBLE";
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var border = x < 6 || x >= width - 6 || y < 6 || y >= height - 6;
                pixels[y * width + x] = border ? new Color32(255, 196, 70, 255) : new Color32(90, 24, 70, 255);
            }

            var left = (width - PixelFont.MeasureWidth(text, scale)) / 2;
            var top = (height + PixelFont.GlyphHeight * scale) / 2;
            // A dark drop shadow, then the letters in yarn pink.
            PixelFont.DrawText((x, row) => pixels[(top - row - 4) * width + x + 4] = new Color32(40, 8, 30, 255), text, left, 0, scale);
            PixelFont.DrawText((x, row) => pixels[(top - row) * width + x] = new Color32(255, 150, 210, 255), text, left, 0, scale);
            return Finish("YarnTumbleSign", width, height, pixels);
        }

        static Texture2D Finish(string name, int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 2,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        // ---------------------------------------------------------------- Shapes (signed distances, in cell units)

        static float Circle(float x, float y, float cx, float cy, float radius) =>
            Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - radius;

        static float Ellipse(float x, float y, float cx, float cy, float rx, float ry)
        {
            var dx = (x - cx) / rx;
            var dy = (y - cy) / ry;
            // Close enough to a distance for antialiasing at these sizes.
            return (Mathf.Sqrt(dx * dx + dy * dy) - 1f) * Mathf.Min(rx, ry);
        }

        /// <summary>A square centred in the cell, <paramref name="half"/> wide each way, with rounded corners.</summary>
        static float RoundedBox(float x, float y, float half, float radius)
        {
            var dx = Mathf.Max(Mathf.Abs(x - 0.5f) - (half - radius), 0f);
            var dy = Mathf.Max(Mathf.Abs(y - 0.5f) - (half - radius), 0f);
            return Mathf.Sqrt(dx * dx + dy * dy) - radius;
        }

        /// <summary>Coverage of a shape at a signed distance, smoothed over about a pixel.</summary>
        static float Inside(float distance) => Mathf.Clamp01(0.5f - distance * Cell);

        static Color Over(Color below, Color above, float amount) => Color.Lerp(below, above, Mathf.Clamp01(amount));
    }
}
