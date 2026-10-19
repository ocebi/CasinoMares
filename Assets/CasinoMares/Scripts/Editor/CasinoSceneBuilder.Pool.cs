using System;
using System.Collections.Generic;
using CasinoMares.Social;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CasinoMares.Editor
{
    /// <summary>
    /// The seaside terrace's pool (plan §11.2): the deck runs on <see cref="DeckExtension"/> metres out to sea (the glass
    /// railing moves out with it), and in its corner toward the islands and the sunset a stone pool, visually
    /// <see cref="PoolDepth"/> deep, its water a little below the deck. Shallow steps at the hall end are the way in
    /// and out; a <see cref="SwimmingPool"/> box over the rest makes a cat swim. Two sun loungers stand beside it. One
    /// mesh each for the extension's deck and the pool's stone, a flat transparent Lit plane for the water (cheaper on
    /// phones than the sea's shader), no new shaders. Sea scene only.
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        /// <summary>How far the deck now runs on past the old terrace edge (<see cref="TerraceEast"/>), out to sea.</summary>
        const float DeckExtension = 8f;
        const float DeckEast = TerraceEast + DeckExtension;
        /// <summary>The pool's water, east–west and north–south, and its gap to the deck's east and north edges.</summary>
        const float PoolLength = 6f;
        const float PoolWidth = 4f;
        const float PoolEdgeMargin = 0.7f;
        const float PoolEast = DeckEast - PoolEdgeMargin;
        const float PoolWest = PoolEast - PoolLength;
        const float PoolNorth = TerraceNorth - PoolEdgeMargin;
        const float PoolSouth = PoolNorth - PoolWidth;
        /// <summary>The floor, below the deck (whose top is 0).</summary>
        const float PoolDepth = 1.2f;
        /// <summary>The water surface: where a swimming cat's feet float (its model sinks 0.25 m more, CatReactions).</summary>
        const float PoolWaterLevel = -0.15f;
        /// <summary>
        /// The steps down from the hall end, full width. Each rises less than the cat's step offset (0.15), so cats walk
        /// up them; the top one stays just dry.
        /// </summary>
        const int PoolSteps = 5;
        const float PoolStepRise = 0.12f;
        const float PoolStepTread = 0.35f;
        /// <summary>
        /// The water a cat swims in starts over this step (counting from the top, 0): above it, a cat walks. A swimming
        /// cat that reaches step 1 (0.24 down) drops onto it and walks out.
        /// </summary>
        const int PoolSwimFromStep = 2;
        /// <summary>
        /// The stone edge round the other three sides. Its top is 0.18 above a floating cat's feet, more than it can
        /// step, so cats only climb out by the steps. At the steps it's a flat lip.
        /// </summary>
        const float PoolCopingWidth = 0.3f;
        const float PoolCopingHeight = 0.03f;
        const float PoolLipHeight = 0.01f;
        /// <summary>The deck and the stone base stop this far short of the basin's walls, so their faces don't fight.</summary>
        const float PoolHoleGap = 0.02f;
        const float DeckThickness = 0.25f;

        /// <summary>The extension's deck, the pool and the loungers. The café table beside it is in <see cref="TerraceTables"/>.</summary>
        static void BuildPool(Transform terrace, Material stone, Material teak)
        {
            BuildDeckExtension(terrace, teak);

            var pool = new GameObject("Pool").transform;
            pool.SetParent(terrace, false);
            BuildPoolBasin(pool, stone);

            var water = GameObject.CreatePrimitive(PrimitiveType.Quad);
            water.name = "Water";
            water.transform.SetParent(pool, false);
            water.transform.SetLocalPositionAndRotation(new Vector3((PoolWest + PoolEast) / 2f, PoolWaterLevel, (PoolSouth + PoolNorth) / 2f), Quaternion.Euler(90f, 0f, 0f));
            water.transform.localScale = new Vector3(PoolLength, PoolWidth, 1f);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.GetComponent<MeshRenderer>().sharedMaterial = TransparentLit("Sea_PoolWater", new Color(0.3f, 0.72f, 0.78f, 0.5f), 0.95f);
            NoShadows(water);
            GameObjectUtility.SetStaticEditorFlags(water, StaticEditorFlags.BatchingStatic);

            // The swimmable water: from the swim step to the east wall, floor up to a little over the deck (a cat
            // hopping in from the edge is caught on the way down).
            var swimWest = PoolWest + PoolSwimFromStep * PoolStepTread;
            const float top = 0.5f;
            var center = new Vector3((swimWest + PoolEast) / 2f, (top - PoolDepth) / 2f, (PoolSouth + PoolNorth) / 2f);
            var area = Child(pool, "SwimArea", center).gameObject.AddComponent<SwimmingPool>();
            var serialized = new SerializedObject(area);
            serialized.FindProperty("_size").vector3Value = new Vector3(PoolEast - swimWest, top + PoolDepth, PoolWidth);
            serialized.FindProperty("_surface").floatValue = PoolWaterLevel - center.y;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Patrons never path into the pool (or down its steps).
            NotWalkable(pool.gameObject);

            var pad = Lit("Sea_LoungerPad", new Color(0.9f, 0.93f, 0.95f), 0.1f);
            Lounger(terrace, "Lounger_1", new Vector3(PoolWest + 3f, 0f, PoolSouth - 1.05f), teak, pad);
            Lounger(terrace, "Lounger_2", new Vector3(PoolWest + 3f, 0f, PoolSouth - 2.05f), teak, pad);
        }

        /// <summary>
        /// The deck's new part round the pool's hole, its planks lined up with the old deck's (the same world-tied UVs),
        /// with the sea-facing edges. The base under it is <see cref="PlinthAroundPool"/>.
        /// </summary>
        static void BuildDeckExtension(Transform terrace, Material teak)
        {
            var deck = new GameObject("DeckExtension");
            deck.transform.SetParent(terrace, false);
            var mesh = new BlockMesh(WorldUvTiles.TryGetValue(teak, out var tile) ? tile : 1f, new Vector2(HallWidth / 2f, TerraceSouth));
            const float bottom = -DeckThickness;
            const float holeWest = PoolWest - PoolHoleGap;
            const float holeEast = PoolEast + PoolHoleGap;
            const float holeSouth = PoolSouth - PoolHoleGap;
            const float holeNorth = PoolNorth + PoolHoleGap;
            foreach (var (min, max, faces) in new[]
                     {
                         (new Vector3(TerraceEast, bottom, TerraceSouth), new Vector3(DeckEast, 0f, holeSouth), Faces.Top | Faces.East | Faces.South),
                         (new Vector3(TerraceEast, bottom, holeNorth), new Vector3(DeckEast, 0f, TerraceNorth), Faces.Top | Faces.East | Faces.North),
                         (new Vector3(TerraceEast, bottom, holeSouth), new Vector3(holeWest, 0f, holeNorth), Faces.Top),
                         (new Vector3(holeEast, bottom, holeSouth), new Vector3(DeckEast, 0f, holeNorth), Faces.Top | Faces.East),
                     })
            {
                mesh.Box(min, max, faces);
                Solid(deck, min, max);
            }

            deck.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh.Build("Sea_DeckExtension"), "Sea_DeckExtension");
            deck.AddComponent<MeshRenderer>().sharedMaterial = teak;
            GameObjectUtility.SetStaticEditorFlags(deck, StaticEditorFlags.BatchingStatic);
        }

        /// <summary>The floor, the inner walls, the steps and the coping: one stone mesh, with box colliders.</summary>
        static void BuildPoolBasin(Transform pool, Material stone)
        {
            var basin = new GameObject("Basin");
            basin.transform.SetParent(pool, false);
            var mesh = new BlockMesh(WorldUvTiles.TryGetValue(stone, out var tile) ? tile : 1f, Vector2.zero);
            const float floor = -PoolDepth;

            mesh.Quad(Vector3.up, new Vector3(PoolWest, floor, PoolSouth), new Vector3(PoolEast, floor, PoolSouth), new Vector3(PoolEast, floor, PoolNorth), new Vector3(PoolWest, floor, PoolNorth));
            mesh.Quad(Vector3.right, new Vector3(PoolWest, floor, PoolSouth), new Vector3(PoolWest, 0f, PoolSouth), new Vector3(PoolWest, 0f, PoolNorth), new Vector3(PoolWest, floor, PoolNorth));
            mesh.Quad(Vector3.left, new Vector3(PoolEast, floor, PoolSouth), new Vector3(PoolEast, 0f, PoolSouth), new Vector3(PoolEast, 0f, PoolNorth), new Vector3(PoolEast, floor, PoolNorth));
            mesh.Quad(Vector3.forward, new Vector3(PoolWest, floor, PoolSouth), new Vector3(PoolEast, floor, PoolSouth), new Vector3(PoolEast, 0f, PoolSouth), new Vector3(PoolWest, 0f, PoolSouth));
            mesh.Quad(Vector3.back, new Vector3(PoolWest, floor, PoolNorth), new Vector3(PoolEast, floor, PoolNorth), new Vector3(PoolEast, 0f, PoolNorth), new Vector3(PoolWest, 0f, PoolNorth));

            // Solid behind the walls and under the floor, so a cat that stops swimming down there stays in the pool.
            const float wall = 0.2f;
            Solid(basin, new Vector3(PoolWest - wall, floor - wall, PoolSouth - wall), new Vector3(PoolEast + wall, floor, PoolNorth + wall));
            Solid(basin, new Vector3(PoolWest - wall, floor, PoolSouth), new Vector3(PoolWest, 0f, PoolNorth));
            Solid(basin, new Vector3(PoolEast, floor, PoolSouth), new Vector3(PoolEast + wall, 0f, PoolNorth));
            Solid(basin, new Vector3(PoolWest - wall, floor, PoolSouth - wall), new Vector3(PoolEast + wall, 0f, PoolSouth));
            Solid(basin, new Vector3(PoolWest - wall, floor, PoolNorth), new Vector3(PoolEast + wall, 0f, PoolNorth + wall));

            for (var i = 0; i < PoolSteps; i++)
            {
                var x0 = PoolWest + i * PoolStepTread;
                var x1 = x0 + PoolStepTread;
                var stepTop = -(i + 1) * PoolStepRise;
                // Each riser shows from the next step's tread up; the last one from the floor.
                var riserFoot = i == PoolSteps - 1 ? floor : -(i + 2) * PoolStepRise;
                mesh.Quad(Vector3.up, new Vector3(x0, stepTop, PoolSouth), new Vector3(x1, stepTop, PoolSouth), new Vector3(x1, stepTop, PoolNorth), new Vector3(x0, stepTop, PoolNorth));
                mesh.Quad(Vector3.right, new Vector3(x1, riserFoot, PoolSouth), new Vector3(x1, stepTop, PoolSouth), new Vector3(x1, stepTop, PoolNorth), new Vector3(x1, riserFoot, PoolNorth));
                Solid(basin, new Vector3(x0, floor, PoolSouth), new Vector3(x1, stepTop, PoolNorth));
            }

            const float cw = PoolCopingWidth;
            const Faces sides = Faces.East | Faces.West | Faces.North | Faces.South;
            foreach (var (min, max) in new[]
                     {
                         (new Vector3(PoolWest - cw, 0f, PoolNorth), new Vector3(PoolEast + cw, PoolCopingHeight, PoolNorth + cw)),
                         (new Vector3(PoolWest - cw, 0f, PoolSouth - cw), new Vector3(PoolEast + cw, PoolCopingHeight, PoolSouth)),
                         (new Vector3(PoolEast, 0f, PoolSouth), new Vector3(PoolEast + cw, PoolCopingHeight, PoolNorth)),
                     })
            {
                mesh.Box(min, max, Faces.Top | sides);
                Solid(basin, min, max);
            }

            // The lip at the steps is too thin to trip on, so it needs no collider.
            mesh.Box(new Vector3(PoolWest - cw, 0f, PoolSouth), new Vector3(PoolWest, PoolLipHeight, PoolNorth), Faces.Top | Faces.East | Faces.West);

            basin.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh.Build("Sea_Pool"), "Sea_Pool");
            basin.AddComponent<MeshRenderer>().sharedMaterial = stone;
            GameObjectUtility.SetStaticEditorFlags(basin, StaticEditorFlags.BatchingStatic);
        }

        /// <summary>
        /// <see cref="PlinthBlocks"/>' terrace block, split round the pool's basin (the base's top would otherwise
        /// cover the water), plus the block under the basin's floor.
        /// </summary>
        static IEnumerable<(string part, float x0, float x1, float z0, float z1, float top)> PlinthAroundPool(float x0, float x1, float z0, float z1)
        {
            const float holeWest = PoolWest - PoolHoleGap;
            const float holeEast = PoolEast + PoolHoleGap;
            const float holeSouth = PoolSouth - PoolHoleGap;
            const float holeNorth = PoolNorth + PoolHoleGap;
            yield return ("_South", x0, x1, z0, holeSouth, PlinthTop);
            yield return ("_North", x0, x1, holeNorth, z1, PlinthTop);
            yield return ("_West", x0, holeWest, holeSouth, holeNorth, PlinthTop);
            yield return ("_East", holeEast, x1, holeSouth, holeNorth, PlinthTop);
            yield return ("_UnderPool", holeWest, holeEast, holeSouth, holeNorth, -PoolDepth - PoolHoleGap);
        }

        /// <summary>A teak sun lounger with a pad, its back raised at the hall end so it faces the sea (east).</summary>
        static void Lounger(Transform parent, string name, Vector3 foot, Material teak, Material pad)
        {
            var lounger = Child(parent, name, foot);
            foreach (var (x, z) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
                Box("Leg", lounger, new Vector3(x * 0.85f, 0.12f, z * 0.27f), new Vector3(0.05f, 0.24f, 0.05f), teak, collider: false);
            Box("Frame", lounger, new Vector3(0f, 0.265f, 0f), new Vector3(1.9f, 0.05f, 0.65f), teak, collider: false);
            Box("Pad", lounger, new Vector3(0.25f, 0.32f, 0f), new Vector3(1.35f, 0.06f, 0.58f), pad, collider: false);
            var back = Child(lounger, "Back", new Vector3(-0.42f, 0.3f, 0f), Quaternion.Euler(0f, 0f, -35f));
            Box("BackFrame", back, new Vector3(-0.31f, -0.005f, 0f), new Vector3(0.62f, 0.03f, 0.62f), teak, collider: false);
            Box("BackPad", back, new Vector3(-0.3f, 0.04f, 0f), new Vector3(0.6f, 0.06f, 0.58f), pad, collider: false);

            var body = lounger.gameObject.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, 0.25f, 0f);
            body.size = new Vector3(1.9f, 0.5f, 0.65f);
            NotWalkable(lounger.gameObject);
        }

        /// <summary>A box collider on <paramref name="go"/> (at the origin, unrotated) filling min..max.</summary>
        static void Solid(GameObject go, Vector3 min, Vector3 max)
        {
            var box = go.AddComponent<BoxCollider>();
            box.center = (min + max) / 2f;
            box.size = max - min;
        }

        [Flags]
        enum Faces
        {
            Top = 1,
            East = 2,
            West = 4,
            North = 8,
            South = 16,
        }

        /// <summary>
        /// Flat-shaded quads in world space, textured in tiles of a fixed size tied to the world: tops by x and z from
        /// an origin (so they line up with a <see cref="WorldUvCube"/> deck that starts there), walls by their run and
        /// height.
        /// </summary>
        sealed class BlockMesh
        {
            readonly List<Vector3> _vertices = new();
            readonly List<Vector3> _normals = new();
            readonly List<Vector2> _uvs = new();
            readonly List<int> _triangles = new();
            readonly float _tile;
            readonly Vector2 _origin;

            public BlockMesh(float tile, Vector2 origin)
            {
                _tile = tile;
                _origin = origin;
            }

            /// <summary>A quad facing <paramref name="normal"/>, its corners in order either way round.</summary>
            public void Quad(Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                // Unity's front faces wind clockwise seen from the front, which makes this cross product point out.
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
                    (b, d) = (d, b);
                var start = _vertices.Count;
                foreach (var corner in new[] { a, b, c, d })
                {
                    _vertices.Add(corner);
                    _normals.Add(normal);
                    var uv = Mathf.Abs(normal.y) > 0.5f ? new Vector2(corner.x - _origin.x, corner.z - _origin.y)
                        : Mathf.Abs(normal.x) > 0.5f ? new Vector2(corner.z, corner.y) : new Vector2(corner.x, corner.y);
                    _uvs.Add(uv / _tile);
                }

                _triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }

            public void Box(Vector3 min, Vector3 max, Faces faces)
            {
                if ((faces & Faces.Top) != 0)
                    Quad(Vector3.up, new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z));
                if ((faces & Faces.East) != 0)
                    Quad(Vector3.right, new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, min.y, max.z));
                if ((faces & Faces.West) != 0)
                    Quad(Vector3.left, new Vector3(min.x, min.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, min.y, max.z));
                if ((faces & Faces.North) != 0)
                    Quad(Vector3.forward, new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z));
                if ((faces & Faces.South) != 0)
                    Quad(Vector3.back, new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z));
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
