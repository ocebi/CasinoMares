using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CasinoMares.Editor
{
    /// <summary>
    /// The roulette table's top as a cat head seen from above, like the game's cat icon: a round head (a superellipse,
    /// so the end seats' chips still sit on the felt) in a wooden rim, two raised ears at the back away from the players,
    /// gold eyes with dark slit pupils, a gold nose and three gold whiskers each side. One generated mesh in the
    /// existing wood, felt, brass and dark wood materials, under 1.5k triangles. Only the look changes: the felt's box
    /// collider, the base, the wheel, the seats and every camera stay where <see cref="BuildRouletteTable"/> put them, so
    /// the NavMesh carve is the same. The face sits on the head's centre line; the wheel stays at the east end.
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        /// <summary>Half the head's width and depth at the rim: the old rim's footprint (2.9 × 1.4 m).</summary>
        const float CatHeadHalfWidth = 1.45f;
        const float CatHeadHalfDepth = 0.7f;
        /// <summary>
        /// The superellipse exponent: 2 is a plain ellipse, higher is squarer. 3.5 is the roundest that keeps the end
        /// seats' chip spots (±1.25, −0.42) on the felt.
        /// </summary>
        const float CatHeadRoundness = 3.5f;
        /// <summary>How far the wooden rim shows around the felt.</summary>
        const float CatHeadRimWidth = 0.05f;
        /// <summary>The ears: centre across, half the base's width, how far out past the head and how high the tip rises.</summary>
        const float CatEarX = 0.8f;
        const float CatEarHalfWidth = 0.3f;
        const float CatEarLength = 0.32f;
        const float CatEarLift = 0.12f;
        /// <summary>The tips lean outward by this much, so the ears splay like the icon's.</summary>
        const float CatEarLean = 0.12f;
        /// <summary>Eyes: the distance between their centres, how far back from the head's centre, and their half size.</summary>
        const float CatEyeSpacing = 0.6f;
        const float CatEyeZ = 0.27f;
        const float CatEyeHalfWidth = 0.13f;
        const float CatEyeHalfDepth = 0.085f;
        const float CatNoseZ = 0.07f;
        const float CatNoseHalfWidth = 0.06f;
        /// <summary>Whiskers start beside the nose and reach out this far (the right ones stop short of the wheel bowl).</summary>
        const float CatWhiskerInner = 0.14f;
        const float CatWhiskerOuter = 0.48f;
        const float CatWhiskerWidth = 0.012f;
        /// <summary>Inlays stand this proud of the felt.</summary>
        const float CatInlayHeight = 0.004f;
        const int CatHeadSegments = 96;

        // Heights from BuildRouletteTable: the old rim box spanned 0.51–0.55, the felt box 0.53–0.57.
        const float CatRimBottom = 0.51f;
        const float CatRimTop = 0.55f;
        const float CatFeltTop = 0.57f;

        /// <summary>
        /// Replaces the roulette table's rectangular rim and felt with the cat head. The felt keeps its box collider
        /// (cats and the NavMesh see the same table); only its renderer goes.
        /// </summary>
        static void BuildRouletteCatHead(Transform table, Materials m)
        {
            var rim = table.Find("Rim");
            if (rim != null)
                Object.DestroyImmediate(rim.gameObject);
            var felt = table.Find("Felt");
            if (felt != null)
            {
                Object.DestroyImmediate(felt.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(felt.GetComponent<MeshFilter>());
            }

            var head = new GameObject("CatHead");
            head.transform.SetParent(table, false);
            head.AddComponent<MeshFilter>().sharedMesh = SaveMesh(CatHeadMesh(m), "Roulette_CatHead");
            head.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.Wood, m.Felt, m.Brass, m.DarkWood };
            GameObjectUtility.SetStaticEditorFlags(head, StaticEditorFlags.BatchingStatic);
        }

        /// <summary>A point on the head's outline, <paramref name="inset"/> metres in from the rim's edge.</summary>
        static Vector2 CatHeadPoint(float angle, float inset)
        {
            var c = Mathf.Cos(angle);
            var s = Mathf.Sin(angle);
            var power = 2f / CatHeadRoundness;
            return new Vector2(
                Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), power) * (CatHeadHalfWidth - inset),
                Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), power) * (CatHeadHalfDepth - inset));
        }

        /// <summary>Where the back of the rim is, <paramref name="x"/> across.</summary>
        static float CatHeadBackZ(float x)
        {
            var t = Mathf.Clamp01(Mathf.Abs(x) / CatHeadHalfWidth);
            return CatHeadHalfDepth * Mathf.Pow(1f - Mathf.Pow(t, CatHeadRoundness), 1f / CatHeadRoundness);
        }

        static Mesh CatHeadMesh(Materials m)
        {
            var builder = new CatHeadBuilder(m);
            const int wood = 0, felt = 1, gold = 2, dark = 3;

            // Head: the rim's top ring, outer wall and underside, then the felt's wall and top.
            var rimOuter = new Vector2[CatHeadSegments];
            var feltEdge = new Vector2[CatHeadSegments];
            for (var i = 0; i < CatHeadSegments; i++)
            {
                var angle = i * Mathf.PI * 2f / CatHeadSegments;
                rimOuter[i] = CatHeadPoint(angle, 0f);
                feltEdge[i] = CatHeadPoint(angle, CatHeadRimWidth);
            }

            builder.Ring(wood, rimOuter, feltEdge, CatRimTop);
            builder.Wall(wood, rimOuter, CatRimBottom, CatRimTop);
            builder.Cap(wood, rimOuter, CatRimBottom, down: true);
            builder.Wall(felt, feltEdge, CatRimTop, CatFeltTop);
            builder.Cap(felt, feltEdge, CatFeltTop, down: false);

            // Ears: a wooden wedge hinged on the back of the rim, rising to the tip, with a felt inner ear on top.
            foreach (var side in new[] { -1f, 1f })
            {
                var inner = side * (CatEarX - CatEarHalfWidth);
                var outer = side * (CatEarX + CatEarHalfWidth);
                var tipX = side * (CatEarX + CatEarLean);
                var baseA = new Vector3(inner, CatRimTop + 0.002f, CatHeadBackZ(inner) - 0.03f);
                var baseB = new Vector3(outer, CatRimTop + 0.002f, CatHeadBackZ(outer) - 0.03f);
                var tip = new Vector3(tipX, CatRimTop + CatEarLift, CatHeadBackZ(side * CatEarX) + CatEarLength);
                builder.Wedge(wood, side > 0 ? new[] { baseA, tip, baseB } : new[] { baseB, tip, baseA }, 0.03f);

                var centre = (baseA + baseB + tip) / 3f;
                Vector3 Inset(Vector3 corner) => Vector3.Lerp(centre, corner, 0.62f) + Vector3.up * 0.004f;
                builder.Triangle(felt, side > 0 ? Inset(baseA) : Inset(baseB), Inset(tip), side > 0 ? Inset(baseB) : Inset(baseA));
            }

            // Face: almond eyes with slit pupils, a nose pointing at the players, three whiskers a side.
            var inlayTop = CatFeltTop + CatInlayHeight;
            foreach (var side in new[] { -1f, 1f })
            {
                var eye = new Vector2(side * CatEyeSpacing * 0.5f, CatEyeZ);
                builder.Disc(gold, eye, new Vector2(CatEyeHalfWidth, CatEyeHalfDepth), CatFeltTop, inlayTop, 24);
                builder.Disc(dark, eye, new Vector2(CatEyeHalfWidth * 0.18f, CatEyeHalfDepth * 0.85f), inlayTop, inlayTop + 0.002f, 16);

                for (var w = -1; w <= 1; w++)
                {
                    var from = new Vector2(side * CatWhiskerInner, CatNoseZ - 0.05f + w * 0.03f);
                    var to = new Vector2(side * CatWhiskerOuter, CatNoseZ - 0.05f + w * 0.11f);
                    builder.Strip(gold, from, to, CatWhiskerWidth, CatFeltTop, inlayTop);
                }
            }

            builder.Prism(gold, new[]
            {
                new Vector2(-CatNoseHalfWidth, CatNoseZ + CatNoseHalfWidth * 0.5f),
                new Vector2(0f, CatNoseZ - CatNoseHalfWidth * 0.8f),
                new Vector2(CatNoseHalfWidth, CatNoseZ + CatNoseHalfWidth * 0.5f),
            }, CatFeltTop, inlayTop + 0.001f);

            return builder.Build("Roulette_CatHead");
        }

        /// <summary>Flat-shaded faces into submeshes (one per material), with UVs the felt and wood textures tile on.</summary>
        sealed class CatHeadBuilder
        {
            readonly List<Vector3> _vertices = new();
            readonly List<Vector3> _normals = new();
            readonly List<Vector2> _uvs = new();
            readonly List<int>[] _triangles = { new(), new(), new(), new() };
            readonly float[] _tiles;

            public CatHeadBuilder(Materials m)
            {
                Material[] materials = { m.Wood, m.Felt, m.Brass, m.DarkWood };
                _tiles = new float[materials.Length];
                for (var i = 0; i < materials.Length; i++)
                    _tiles[i] = materials[i] != null && WorldUvTiles.TryGetValue(materials[i], out var tile) ? tile : 1f;
            }

            /// <summary>A flat polygon (convex, corners in order) facing <paramref name="normal"/>, fanned from its first corner.</summary>
            void Polygon(int submesh, IReadOnlyList<Vector3> corners, Vector3 normal)
            {
                var start = _vertices.Count;
                foreach (var corner in corners)
                {
                    _vertices.Add(corner);
                    _normals.Add(normal);
                    // Tops by their place on the table, walls by their run along it and their height.
                    var uv = Mathf.Abs(normal.y) > 0.5f ? new Vector2(corner.x, corner.z) : new Vector2(corner.x + corner.z, corner.y);
                    _uvs.Add(uv / _tiles[submesh]);
                }

                var triangles = _triangles[submesh];
                for (var i = 1; i < corners.Count - 1; i++)
                {
                    triangles.Add(start);
                    triangles.Add(start + i);
                    triangles.Add(start + i + 1);
                }
            }

            static Vector3 Normal(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a).normalized;

            /// <summary>A triangle wound clockwise seen from where it faces (Unity's front).</summary>
            public void Triangle(int submesh, Vector3 a, Vector3 b, Vector3 c) => Polygon(submesh, new[] { a, b, c }, Normal(a, b, c));

            void Quad(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d) => Polygon(submesh, new[] { a, b, c, d }, Normal(a, b, c));

            /// <summary>The flat band between two outlines (counterclockwise from above) at one height, facing up.</summary>
            public void Ring(int submesh, Vector2[] outer, Vector2[] inner, float y)
            {
                for (var i = 0; i < outer.Length; i++)
                {
                    var j = (i + 1) % outer.Length;
                    Quad(submesh, At(outer[i], y), At(inner[i], y), At(inner[j], y), At(outer[j], y));
                }
            }

            /// <summary>The upright wall around an outline (counterclockwise from above), facing out.</summary>
            public void Wall(int submesh, Vector2[] outline, float bottom, float top)
            {
                for (var i = 0; i < outline.Length; i++)
                {
                    var j = (i + 1) % outline.Length;
                    Quad(submesh, At(outline[i], bottom), At(outline[i], top), At(outline[j], top), At(outline[j], bottom));
                }
            }

            /// <summary>An outline (counterclockwise from above) filled in at one height, facing up or down.</summary>
            public void Cap(int submesh, Vector2[] outline, float y, bool down)
            {
                var corners = new Vector3[outline.Length];
                for (var i = 0; i < outline.Length; i++)
                    corners[down ? i : outline.Length - 1 - i] = At(outline[i], y);
                Polygon(submesh, corners, down ? Vector3.down : Vector3.up);
            }

            /// <summary>An inlay: an outline (counterclockwise from above) standing from <paramref name="bottom"/> to <paramref name="top"/>.</summary>
            public void Prism(int submesh, Vector2[] outline, float bottom, float top)
            {
                Cap(submesh, outline, top, down: false);
                Wall(submesh, outline, bottom, top);
            }

            /// <summary>An elliptical inlay.</summary>
            public void Disc(int submesh, Vector2 centre, Vector2 radius, float bottom, float top, int segments)
            {
                var outline = new Vector2[segments];
                for (var i = 0; i < segments; i++)
                {
                    var angle = i * Mathf.PI * 2f / segments;
                    outline[i] = centre + new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y);
                }

                Prism(submesh, outline, bottom, top);
            }

            /// <summary>A thin straight inlay from one point to another.</summary>
            public void Strip(int submesh, Vector2 from, Vector2 to, float width, float bottom, float top)
            {
                var along = (to - from).normalized;
                var across = new Vector2(-along.y, along.x) * (width * 0.5f);
                Prism(submesh, new[] { from - across, to - across, to + across, from + across }, bottom, top);
            }

            /// <summary>
            /// A triangular slab <paramref name="thickness"/> deep under its top face (corners wound clockwise from
            /// above): the top, the underside and the three edges.
            /// </summary>
            public void Wedge(int submesh, Vector3[] top, float thickness)
            {
                var down = Vector3.down * thickness;
                Triangle(submesh, top[0], top[1], top[2]);
                Triangle(submesh, top[2] + down, top[1] + down, top[0] + down);
                for (var i = 0; i < 3; i++)
                {
                    var a = top[i];
                    var b = top[(i + 1) % 3];
                    // The top runs clockwise, so walk each edge backwards to face out.
                    Quad(submesh, b + down, b, a, a + down);
                }
            }

            static Vector3 At(Vector2 point, float y) => new(point.x, y, point.y);

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uvs);
                mesh.subMeshCount = _triangles.Length;
                for (var i = 0; i < _triangles.Length; i++)
                    mesh.SetTriangles(_triangles[i], i);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }
    }
}
