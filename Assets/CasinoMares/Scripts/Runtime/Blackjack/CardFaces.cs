using CasinoMares.Core;
using CasinoMares.Rules;
using UnityEngine;

namespace CasinoMares.Blackjack
{
    /// <summary>
    /// Builds the 52 card faces plus a card back into one texture at runtime, using a tiny built-in pixel font,
    /// so no card art or font assets are needed. Crisp retro look with point filtering. The faces use a jumbo
    /// index (a large rank and suit in two corners, a small pip between) so they read on a small screen.
    /// </summary>
    public static class CardFaces
    {
        public const int CellWidth = 50;
        public const int CellHeight = 70;
        const int AtlasWidth = 1024;
        const int AtlasHeight = 512;

        static Texture2D _atlas;
        static Mesh[] _faceMeshes;
        static Mesh _backMesh;

        public static readonly Vector2 CardSize = new(0.16f, 0.224f);

        static readonly Color32 Paper = new(250, 247, 238, 255);
        static readonly Color32 Border = new(170, 160, 150, 255);
        static readonly Color32 Red = new(200, 40, 50, 255);
        static readonly Color32 Black = new(30, 28, 34, 255);
        static readonly Color32 BackColor = new(120, 28, 44, 255);
        static readonly Color32 BackPattern = new(160, 52, 66, 255);
        static readonly Color32 Gold = new(226, 180, 90, 255);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _atlas = null;
            _faceMeshes = null;
            _backMesh = null;
        }

        public static Texture2D Atlas
        {
            get
            {
                if (_atlas == null)
                    _atlas = BuildAtlas();
                return _atlas;
            }
        }

        /// <summary>Flat card mesh (XZ plane) showing this card's face on top.</summary>
        public static Mesh FaceMesh(byte card)
        {
            _faceMeshes ??= new Mesh[Cards.DeckSize];
            return _faceMeshes[card] ??= BuildQuad(Cards.Rank(card), Cards.Suit(card), faceUp: true);
        }

        /// <summary>Flat card mesh (XZ plane) showing the back on the underside.</summary>
        public static Mesh BackMesh => _backMesh ??= BuildQuad(0, 4, faceUp: false);

        static Mesh BuildQuad(int column, int row, bool faceUp)
        {
            var halfW = CardSize.x / 2f;
            var halfL = CardSize.y / 2f;
            var y = faceUp ? 0.0006f : -0.0006f;

            var u0 = column * CellWidth / (float)AtlasWidth;
            var u1 = (column + 1) * CellWidth / (float)AtlasWidth;
            // Row 0 sits at the top of the texture (texture v grows upwards).
            var v1 = 1f - row * CellHeight / (float)AtlasHeight;
            var v0 = 1f - (row + 1) * CellHeight / (float)AtlasHeight;

            var mesh = new Mesh { name = faceUp ? $"CardFace_{row}_{column}" : "CardBack" };
            mesh.vertices = new[]
            {
                new Vector3(-halfW, y, -halfL),
                new Vector3(-halfW, y, halfL),
                new Vector3(halfW, y, halfL),
                new Vector3(halfW, y, -halfL),
            };
            mesh.uv = new[] { new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0) };
            mesh.triangles = faceUp ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Texture2D BuildAtlas()
        {
            var pixels = new Color32[AtlasWidth * AtlasHeight];
            for (var suit = 0; suit < 4; suit++)
            {
                for (var rank = 0; rank < Cards.RanksPerSuit; rank++)
                    DrawFace(pixels, rank, suit);
            }

            DrawBack(pixels);

            var texture = new Texture2D(AtlasWidth, AtlasHeight, TextureFormat.RGBA32, false)
            {
                name = "CardAtlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        // Cell origin in pixel coordinates with y pointing down from the top of the cell.
        static void Set(Color32[] pixels, int column, int row, int x, int y, Color32 color)
        {
            if (x < 0 || y < 0 || x >= CellWidth || y >= CellHeight)
                return;
            var px = column * CellWidth + x;
            var py = AtlasHeight - 1 - (row * CellHeight + y);
            pixels[py * AtlasWidth + px] = color;
        }

        static void Fill(Color32[] pixels, int column, int row, Color32 color, int inset = 0)
        {
            for (var y = inset; y < CellHeight - inset; y++)
            for (var x = inset; x < CellWidth - inset; x++)
                Set(pixels, column, row, x, y, color);
        }

        /// <summary>
        /// The rank's pixel scale in the corner index: 21 px of the 70 px card, with the suit under it the index fills
        /// about 40% of the height ("jumbo index"), so the rank still reads where the card covers only a few screen
        /// pixels (a phone at a low render scale).
        /// </summary>
        const int IndexRankScale = 3;
        /// <summary>The suit under the rank, and the reduced pip in the middle.</summary>
        const int IndexSuitScale = 1;
        const int PipScale = 2;

        static void DrawFace(Color32[] pixels, int rank, int suit)
        {
            Fill(pixels, rank, suit, Border);
            Fill(pixels, rank, suit, Paper, 1);

            var color = suit is 1 or 2 ? Red : Black;
            var suitGlyph = PixelFont.Suits[suit];
            void Plot(int x, int y) => Set(pixels, rank, suit, x, y, color);
            // The same index again in the bottom-right corner, turned half round, as on real cards.
            void PlotTurned(int x, int y) => Set(pixels, rank, suit, CellWidth - 1 - x, CellHeight - 1 - y, color);

            var label = Cards.RankLabel(Cards.Make(rank, suit));
            const int left = 3;
            const int top = 3;
            const int suitTop = top + PixelFont.GlyphHeight * IndexRankScale + 2;
            foreach (var plot in new System.Action<int, int>[] { Plot, PlotTurned })
            {
                PixelFont.DrawText(plot, label, left, top, IndexRankScale);
                PixelFont.Draw(plot, suitGlyph, left + 1, suitTop, IndexSuitScale);
            }

            // A smaller suit in the middle, between the two indexes.
            const int pip = 7 * PipScale;
            PixelFont.Draw(Plot, suitGlyph, (CellWidth - pip) / 2, (CellHeight - pip) / 2, PipScale);
        }

        static void DrawBack(Color32[] pixels)
        {
            const int column = 0;
            const int row = 4;
            Fill(pixels, column, row, Gold);
            Fill(pixels, column, row, BackColor, 3);
            for (var y = 5; y < CellHeight - 5; y++)
            for (var x = 5; x < CellWidth - 5; x++)
            {
                if ((x + y) % 6 == 0 || (x - y + 600) % 6 == 0)
                    Set(pixels, column, row, x, y, BackPattern);
            }

            PixelFont.Draw((x, y) => Set(pixels, column, row, x, y, Gold), PixelFont.Suits[1], (CellWidth - 14) / 2, (CellHeight - 14) / 2, 2);
        }
    }
}
