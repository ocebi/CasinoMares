using CasinoMares.Core;
using CasinoMares.Rules;
using UnityEngine;

namespace CasinoMares.Roulette
{
    /// <summary>Paints the European wheel face (pockets, colors, numbers) into a texture at runtime.</summary>
    public static class RouletteWheelTexture
    {
        const int Size = 512;
        static Texture2D _texture;

        static readonly Color32 Red = new(190, 30, 40, 255);
        static readonly Color32 Black = new(24, 22, 26, 255);
        static readonly Color32 Green = new(20, 120, 60, 255);
        static readonly Color32 Brass = new(214, 170, 90, 255);
        static readonly Color32 Wood = new(84, 48, 28, 255);
        static readonly Color32 White = new(245, 240, 230, 255);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _texture = null;

        /// <summary>
        /// Texture u axis maps to the wheel's local +X and v to +Z (see the disc mesh). Pocket i is centred at
        /// angle i * 360/37 degrees, measured from +Z towards +X (clockwise seen from above).
        /// </summary>
        public static Texture2D Texture => _texture ??= Build();

        public static float PocketAngle(int number) => RouletteRules.PocketIndex(number) * 360f / RouletteRules.Numbers;

        static Texture2D Build()
        {
            var pixels = new Color32[Size * Size];
            const float center = Size / 2f;
            const float outer = Size / 2f - 2f;
            const float rim = outer * 0.93f;
            const float pocketsOuter = outer * 0.86f;
            const float pocketsInner = outer * 0.62f;
            const float cone = outer * 0.5f;
            const float step = 360f / RouletteRules.Numbers;

            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var r = Mathf.Sqrt(dx * dx + dy * dy);
                Color32 color;
                if (r > outer)
                    color = new Color32(0, 0, 0, 0);
                else if (r > rim)
                    color = Wood;
                else if (r > pocketsOuter)
                    color = Brass;
                else if (r > pocketsInner)
                {
                    // Angle from +Z (texture up) towards +X (texture right).
                    var angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                    if (angle < 0f)
                        angle += 360f;
                    var index = Mathf.FloorToInt((angle + step / 2f) / step) % RouletteRules.Numbers;
                    var number = RouletteRules.WheelOrder[index];
                    var edge = Mathf.Abs(((angle + step / 2f) % step) - step / 2f) > step / 2f - 0.8f;
                    color = edge ? Brass : number == 0 ? Green : RouletteRules.IsRed(number) ? Red : Black;
                }
                else if (r > cone)
                    color = Wood;
                else
                    color = r < 14f ? Brass : new Color32(110, 66, 38, 255);

                pixels[y * Size + x] = color;
            }

            // Numbers, upright, centred in each pocket.
            for (var i = 0; i < RouletteRules.Numbers; i++)
            {
                var number = RouletteRules.WheelOrder[i];
                var angle = i * step * Mathf.Deg2Rad;
                var radius = (pocketsOuter + pocketsInner) / 2f + 18f;
                var text = number.ToString();
                var width = PixelFont.MeasureWidth(text, 1);
                var left = Mathf.RoundToInt(center + Mathf.Sin(angle) * radius - width / 2f);
                var top = Mathf.RoundToInt(center + Mathf.Cos(angle) * radius + PixelFont.GlyphHeight / 2f);
                // PixelFont rows grow downwards; texture v grows upwards, so row n lands at top - n.
                PixelFont.DrawText((gx, row) =>
                {
                    var ty = top - row;
                    if (gx >= 0 && ty >= 0 && gx < Size && ty < Size)
                        pixels[ty * Size + gx] = White;
                }, text, left, 0, 1);
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "RouletteWheel",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>A flat disc (XZ plane, facing up) whose UVs match the texture layout above.</summary>
        public static Mesh DiscMesh(float radius)
        {
            const int segments = 64;
            var vertices = new Vector3[segments + 1];
            var uvs = new Vector2[segments + 1];
            var triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (var i = 0; i < segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var x = Mathf.Sin(angle);
                var z = Mathf.Cos(angle);
                vertices[i + 1] = new Vector3(x * radius, 0f, z * radius);
                uvs[i + 1] = new Vector2(0.5f + x * 0.5f, 0.5f + z * 0.5f);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }

            var mesh = new Mesh { name = "WheelDisc", vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
