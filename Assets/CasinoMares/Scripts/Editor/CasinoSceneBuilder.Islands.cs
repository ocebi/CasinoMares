using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CasinoMares.Editor
{
    /// <summary>
    /// The land the seaside casino looks out on: islands with cliffs, beaches and Mediterranean trees, a whitewashed
    /// village with a church and a lighthouse on the nearest one, and a mountainous coast with a harbour town across
    /// the water to the south-east. It's all generated terrain (noise shaped into hills, then worn down by simulated
    /// rain) with the colours baked into the vertices, drawn by Landscape.shader.
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        /// <summary>The far coast reaches this far from the terrace; the sea and every camera go a little further.</summary>
        const float FarCoastReach = 2300f;
        const float FarClip = 2500f;

        /// <summary>
        /// The islands: where they lie from the terrace (yaw from north toward east, distance), their radius and
        /// height, and what's on them. The sailboats' loops (<see cref="Boats"/>) keep clear of them.
        /// </summary>
        static readonly (string name, float yaw, float distance, float radius, float peak, int seed, IslandLife life)[] Islands =
        {
            ("Island_Chapel", 34f, 520f, 70f, 32f, 11, IslandLife.Chapel),
            ("Island_Cliffs", 86f, 700f, 85f, 44f, 23, IslandLife.Wild),
            ("Island_North", -16f, 780f, 100f, 56f, 31, IslandLife.Wild),
            ("Island_Village", 125f, 280f, 80f, 46f, 47, IslandLife.Village),
        };

        enum IslandLife
        {
            Wild,
            Chapel,
            Village,
        }

        static Vector2 FromTerrace(float yaw, float distance) =>
            SeaCenter + new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad)) * distance;

        static float YawFrom(Vector2 from, Vector2 to) => Mathf.Atan2(to.x - from.x, to.y - from.y) * Mathf.Rad2Deg;

        static void BuildIslands(Transform parent)
        {
            var material = LandscapeMaterial();
            var root = new GameObject("Islands").transform;
            root.SetParent(parent, false);

            foreach (var (name, yaw, distance, radius, peak, seed, life) in Islands)
            {
                var center = FromTerrace(yaw, distance);
                var terrain = IslandTerrain(center, radius, peak, seed);
                var mesh = new LandMesh();
                mesh.Terrain(terrain, seed, 28f);
                var random = new System.Random(seed);
                var buildings = new List<(Vector2 position, float radius)>();
                var homeward = YawFrom(center, SeaCenter);
                mesh.Building = true;
                switch (life)
                {
                    case IslandLife.Village:
                        BuildVillage(mesh, terrain, center, radius, homeward, random, buildings);
                        break;
                    case IslandLife.Chapel:
                        BuildHamlet(mesh, terrain, center, radius, homeward, random, buildings);
                        break;
                }

                mesh.Building = false;

                Plant(mesh, terrain, center, radius, random, buildings);
                AddLand(root, name, mesh, new Vector3(center.x, SeaLevel, center.y), material);
            }

            BuildFarCoast(parent, material);
        }

        static void AddLand(Transform parent, string name, LandMesh mesh, Vector3 origin, Material material)
        {
            var land = new GameObject(name);
            land.transform.SetParent(parent, false);
            land.transform.localPosition = origin;
            var built = mesh.ToMesh(name, origin);
            // Quantised to a few centimetres in the build, which is plenty this far away and halves the download.
            MeshUtility.SetMeshCompression(built, ModelImporterMeshCompression.Medium);
            land.AddComponent<MeshFilter>().sharedMesh = SaveMesh(built, "Sea_" + name);
            var renderer = land.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        static Material LandscapeMaterial()
        {
            var material = MaterialAsset("Sea_Landscape", "CasinoMares/Landscape");
            material.SetTexture("_DetailTex", GeneratedTexture("SeaLandDetail", 256, 256, (u, v) =>
            {
                var value = 0.5f + (Fbm(u, v, 8, 5, 91) - 0.5f) * 1.8f;
                return new Color(value, value, value);
            }, configure: importer => importer.sRGBTexture = false));
            material.SetVector("_Shore", new Vector4(SeaLevel, 0.45f, 1.6f, 0.7f));
            material.SetVector("_Haze", new Vector4(RenderSettings.fogDensity, 100f, 1f, 0f));
            SetSkyParameters(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------------------------------------------------------------- Terrain

        /// <summary>Heights in metres above the sea on a grid of square cells laid on the world's x/z.</summary>
        sealed class Heightfield
        {
            public readonly int Width;
            public readonly int Depth;
            public readonly float Cell;
            public readonly Vector2 Origin;
            public readonly float[] Heights;

            public Heightfield(Vector2 origin, int width, int depth, float cell)
            {
                Origin = origin;
                Width = width;
                Depth = depth;
                Cell = cell;
                Heights = new float[width * depth];
            }

            public Vector2 Position(int x, int z) => Origin + new Vector2(x, z) * Cell;

            public void Fill(System.Func<Vector2, float> height)
            {
                for (var z = 0; z < Depth; z++)
                {
                    for (var x = 0; x < Width; x++)
                        Heights[z * Width + x] = height(Position(x, z));
                }
            }

            public float Sample(Vector2 position)
            {
                var gx = Mathf.Clamp((position.x - Origin.x) / Cell, 0f, Width - 1.001f);
                var gz = Mathf.Clamp((position.y - Origin.y) / Cell, 0f, Depth - 1.001f);
                var x = (int)gx;
                var z = (int)gz;
                var fx = gx - x;
                var fz = gz - z;
                var i = z * Width + x;
                return Mathf.Lerp(Mathf.Lerp(Heights[i], Heights[i + 1], fx), Mathf.Lerp(Heights[i + Width], Heights[i + Width + 1], fx), fz);
            }

            public Vector3 Normal(Vector2 position)
            {
                var e = Cell;
                var dx = Sample(position + new Vector2(e, 0f)) - Sample(position - new Vector2(e, 0f));
                var dz = Sample(position + new Vector2(0f, e)) - Sample(position - new Vector2(0f, e));
                return new Vector3(-dx, 2f * e, -dz).normalized;
            }

            /// <summary>
            /// Rain wearing the land down (Sebastian Lague's droplet erosion, on heights divided by
            /// <paramref name="scale"/>): each drop runs downhill, picking up soil while it speeds up and dropping it
            /// as it slows, which cuts gullies down the slopes and spreads fans of silt at their feet.
            /// </summary>
            public void Erode(int droplets, float scale, int seed)
            {
                const int radius = 3;
                const float inertia = 0.05f;
                const float capacityFactor = 4f;
                const float minCapacity = 0.01f;
                const float erodeSpeed = 0.3f;
                const float depositSpeed = 0.3f;
                const float evaporation = 0.02f;
                const float gravity = 4f;
                const int lifetime = 30;

                var brush = new List<(int dx, int dz, float weight)>();
                var total = 0f;
                for (var dz = -radius; dz <= radius; dz++)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var weight = 1f - Mathf.Sqrt(dx * dx + dz * dz) / radius;
                        if (weight <= 0f)
                            continue;
                        brush.Add((dx, dz, weight));
                        total += weight;
                    }
                }

                var map = Heights;
                for (var i = 0; i < map.Length; i++)
                    map[i] /= scale;

                (float height, float gx, float gz) Surface(float px, float pz)
                {
                    var x = (int)px;
                    var z = (int)pz;
                    var fx = px - x;
                    var fz = pz - z;
                    var i = z * Width + x;
                    float a = map[i], b = map[i + 1], c = map[i + Width], d = map[i + Width + 1];
                    return (a * (1 - fx) * (1 - fz) + b * fx * (1 - fz) + c * (1 - fx) * fz + d * fx * fz,
                        (b - a) * (1 - fz) + (d - c) * fz, (c - a) * (1 - fx) + (d - b) * fx);
                }

                var random = new System.Random(seed);
                for (var drop = 0; drop < droplets; drop++)
                {
                    var px = (float)random.NextDouble() * (Width - 2);
                    var pz = (float)random.NextDouble() * (Depth - 2);
                    float dirX = 0f, dirZ = 0f, speed = 1f, water = 1f, sediment = 0f;
                    for (var step = 0; step < lifetime; step++)
                    {
                        var cx = (int)px;
                        var cz = (int)pz;
                        var ox = px - cx;
                        var oz = pz - cz;
                        var (height, gx, gz) = Surface(px, pz);
                        dirX = dirX * inertia - gx * (1f - inertia);
                        dirZ = dirZ * inertia - gz * (1f - inertia);
                        var length = Mathf.Sqrt(dirX * dirX + dirZ * dirZ);
                        if (length < 1e-6f)
                            break;
                        dirX /= length;
                        dirZ /= length;
                        px += dirX;
                        pz += dirZ;
                        if (px < 0f || pz < 0f || px >= Width - 1 || pz >= Depth - 1)
                            break;

                        var fall = Surface(px, pz).height - height;
                        var capacity = Mathf.Max(-fall * speed * water * capacityFactor, minCapacity);
                        var i = cz * Width + cx;
                        if (sediment > capacity || fall > 0f)
                        {
                            var deposit = fall > 0f ? Mathf.Min(fall, sediment) : (sediment - capacity) * depositSpeed;
                            sediment -= deposit;
                            map[i] += deposit * (1 - ox) * (1 - oz);
                            map[i + 1] += deposit * ox * (1 - oz);
                            map[i + Width] += deposit * (1 - ox) * oz;
                            map[i + Width + 1] += deposit * ox * oz;
                        }
                        else
                        {
                            var erode = Mathf.Min((capacity - sediment) * erodeSpeed, -fall);
                            foreach (var (dx, dz, weight) in brush)
                            {
                                var bx = cx + dx;
                                var bz = cz + dz;
                                if (bx >= 0 && bz >= 0 && bx < Width && bz < Depth)
                                    map[bz * Width + bx] -= erode * weight / total;
                            }

                            sediment += erode;
                        }

                        speed = Mathf.Sqrt(Mathf.Max(0f, speed * speed - fall * gravity));
                        water *= 1f - evaporation;
                    }
                }

                for (var i = 0; i < map.Length; i++)
                    map[i] *= scale;
            }
        }

        static float Perlin(float x, float y) => Mathf.PerlinNoise(x, y);

        /// <summary>
        /// Ridged noise: sharp crests where Perlin noise crosses its middle, each octave weighted by the last so the
        /// detail gathers on the ridges, like real mountains. About 0..1.
        /// </summary>
        static float Ridged(Vector2 p, int octaves, float seed)
        {
            float sum = 0f, amplitude = 0.5f, weight = 1f, total = 0f;
            for (var i = 0; i < octaves; i++)
            {
                var n = 1f - Mathf.Abs(Perlin(p.x + seed + i * 31.7f, p.y - seed + i * 17.3f) * 2f - 1f);
                n *= n * weight;
                weight = Mathf.Clamp01(n * 1.6f);
                sum += n * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                p = new Vector2(p.x * 1.6f - p.y * 1.2f, p.x * 1.2f + p.y * 1.6f);
            }

            return sum / total;
        }

        static float Fbm2(Vector2 p, int octaves, float seed)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (var i = 0; i < octaves; i++)
            {
                sum += Perlin(p.x + seed + i * 13.1f, p.y + seed * 0.7f - i * 7.9f) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                p *= 2.03f;
            }

            return sum / total;
        }

        /// <summary>
        /// An island: a hill off to one side of the middle, ridged and worn by rain, with a wavy coastline that drops
        /// away under the sea.
        /// </summary>
        static Heightfield IslandTerrain(Vector2 center, float radius, float peak, int seed)
        {
            var cells = Mathf.Clamp(Mathf.RoundToInt(radius * 1.15f), 40, 110);
            var cell = radius * 2.7f / cells;
            var field = new Heightfield(center - Vector2.one * (cells * cell / 2f), cells + 1, cells + 1, cell);
            var o = seed * 7.31f;
            var top = new Vector2(Perlin(o, 0.5f) - 0.5f, Perlin(0.5f, o) - 0.5f) * radius * 0.6f;
            field.Fill(p =>
            {
                var local = p - center;
                var angle = Mathf.Atan2(local.y, local.x);
                var coast = radius * (1f + 0.42f * (Perlin(Mathf.Cos(angle) * 1.4f + o, Mathf.Sin(angle) * 1.4f + o) - 0.5f) + 0.08f * Mathf.Sin(3f * angle + o));
                var outward = local.magnitude / coast;
                var hill = Mathf.Pow(Mathf.Clamp01(1f - (local - top).magnitude / (coast * 1.15f)), 1.3f);
                var ridges = Ridged(p / (radius * 0.8f), 5, o);
                var land = peak * hill * (0.45f + 0.75f * ridges) + 3f + 3f * Fbm2(p / 18f, 3, o);
                return Mathf.Lerp(-9f, land, Edge(1.06f, 0.84f, outward));
            });
            field.Erode(Mathf.RoundToInt(cells * cells * 1.6f), peak, seed);
            return field;
        }

        // ---------------------------------------------------------------- Meshes with baked colours

        /// <summary>
        /// A mesh for the Landscape shader: positions, normals and sRGB colours whose alpha says what it is (0 ground
        /// and trees, 128 buildings, 255 lit windows). Built in world space; <see cref="ToMesh"/> moves it to the
        /// object's origin.
        /// </summary>
        sealed class LandMesh
        {
            readonly List<Vector3> _vertices = new();
            readonly List<Vector3> _normals = new();
            readonly List<Color32> _colors = new();
            readonly List<int> _triangles = new();

            /// <summary>While set, what's added is a building (the shader keeps the ground's detail noise off it).</summary>
            public bool Building;

            public int Add(Vector3 position, Vector3 normal, Color32 color)
            {
                if (Building)
                    color.a = (byte)Mathf.Max(color.a, 128);
                _vertices.Add(position);
                _normals.Add(normal);
                _colors.Add(color);
                return _vertices.Count - 1;
            }

            /// <summary>A triangle of existing vertices, turned to face where their normals point.</summary>
            public void Triangle(int a, int b, int c)
            {
                var facing = Vector3.Cross(_vertices[b] - _vertices[a], _vertices[c] - _vertices[a]);
                if (Vector3.Dot(facing, _normals[a] + _normals[b] + _normals[c]) < 0f)
                    (b, c) = (c, b);
                _triangles.Add(a);
                _triangles.Add(b);
                _triangles.Add(c);
            }

            /// <summary>A flat face (a triangle, or a quad given round its edge) facing <paramref name="outward"/>.</summary>
            public void Face(Color32 color, Vector3 outward, params Vector3[] corners)
            {
                var normal = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]).normalized;
                if (Vector3.Dot(normal, outward) < 0f)
                    normal = -normal;
                var first = _vertices.Count;
                foreach (var corner in corners)
                    Add(corner, normal, color);
                for (var i = 1; i < corners.Length - 1; i++)
                    Triangle(first, first + i, first + i + 1);
            }

            /// <summary>A box standing on <paramref name="foot"/>, turned by <paramref name="yaw"/>, without its bottom.</summary>
            public void Box(Vector3 foot, Vector3 size, float yaw, Color32 color)
            {
                var rotation = Quaternion.Euler(0f, yaw, 0f);
                var half = new Vector3(size.x / 2f, 0f, size.z / 2f);
                Vector3 P(float x, float y, float z) => foot + rotation * new Vector3(x * half.x, y * size.y, z * half.z);
                var right = rotation * Vector3.right;
                var forward = rotation * Vector3.forward;
                Face(color, forward, P(-1, 0, 1), P(1, 0, 1), P(1, 1, 1), P(-1, 1, 1));
                Face(color, -forward, P(-1, 0, -1), P(1, 0, -1), P(1, 1, -1), P(-1, 1, -1));
                Face(color, right, P(1, 0, -1), P(1, 0, 1), P(1, 1, 1), P(1, 1, -1));
                Face(color, -right, P(-1, 0, -1), P(-1, 0, 1), P(-1, 1, 1), P(-1, 1, -1));
                Face(color, Vector3.up, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1));
            }

            /// <summary>
            /// A solid turned round a vertical axis from (height above the foot, radius) points, bottom to top; a zero
            /// radius closes it to a point. <paramref name="soft"/> bends the normals toward pointing out from its
            /// middle, so foliage shades as a rounded mass instead of a faceted cone.
            /// </summary>
            public void Lathe(Vector3 foot, (float height, float radius)[] profile, int sides, Color32 color, float soft = 0f, float turn = 0f)
            {
                var middle = foot + Vector3.up * ((profile[0].height + profile[^1].height) / 2f);
                var rings = new int[profile.Length];
                for (var i = 0; i < profile.Length; i++)
                {
                    var (height, radius) = profile[i];
                    rings[i] = _vertices.Count;
                    if (radius <= 0f)
                    {
                        Add(foot + Vector3.up * height, i == 0 ? Vector3.down : Vector3.up, color);
                        continue;
                    }

                    var before = profile[Mathf.Max(i - 1, 0)];
                    var after = profile[Mathf.Min(i + 1, profile.Length - 1)];
                    var slope = new Vector2(after.height - before.height, before.radius - after.radius).normalized;
                    for (var s = 0; s < sides; s++)
                    {
                        var angle = (s + turn) * Mathf.PI * 2f / sides;
                        var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        var position = foot + radial * radius + Vector3.up * height;
                        var normal = (radial * slope.x + Vector3.up * slope.y).normalized;
                        if (soft > 0f)
                            normal = Vector3.Slerp(normal, (position - middle).normalized, soft);
                        Add(position, normal, color);
                    }
                }

                for (var i = 0; i < profile.Length - 1; i++)
                {
                    var lowPoint = profile[i].radius <= 0f;
                    var highPoint = profile[i + 1].radius <= 0f;
                    for (var s = 0; s < sides; s++)
                    {
                        var t = (s + 1) % sides;
                        if (lowPoint && highPoint)
                            continue;
                        if (lowPoint)
                            Triangle(rings[i], rings[i + 1] + s, rings[i + 1] + t);
                        else if (highPoint)
                            Triangle(rings[i] + s, rings[i] + t, rings[i + 1]);
                        else
                        {
                            Triangle(rings[i] + s, rings[i] + t, rings[i + 1] + t);
                            Triangle(rings[i] + s, rings[i + 1] + t, rings[i + 1] + s);
                        }
                    }
                }
            }

            /// <summary>The heightfield as smooth ground, coloured by height and slope; cells deep under the sea are left out.</summary>
            public void Terrain(Heightfield field, int seed, float patchSize)
            {
                var first = _vertices.Count;
                for (var z = 0; z < field.Depth; z++)
                {
                    for (var x = 0; x < field.Width; x++)
                    {
                        var p = field.Position(x, z);
                        var height = field.Heights[z * field.Width + x];
                        var normal = field.Normal(p);
                        Add(new Vector3(p.x, SeaLevel + height, p.y), normal, GroundColor(p, height, normal, seed, patchSize));
                    }
                }

                for (var z = 0; z < field.Depth - 1; z++)
                {
                    for (var x = 0; x < field.Width - 1; x++)
                    {
                        var a = first + z * field.Width + x;
                        if (Mathf.Max(Mathf.Max(field.Heights[a - first], field.Heights[a - first + 1]),
                                Mathf.Max(field.Heights[a - first + field.Width], field.Heights[a - first + field.Width + 1])) < -5f)
                            continue;
                        Triangle(a, a + field.Width, a + 1);
                        Triangle(a + 1, a + field.Width, a + field.Width + 1);
                    }
                }
            }

            public Mesh ToMesh(string name, Vector3 origin)
            {
                var mesh = new Mesh { name = name };
                if (_vertices.Count > 65535)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(_vertices.ConvertAll(v => v - origin));
                mesh.SetNormals(_normals);
                mesh.SetColors(_colors);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        static Color32 Srgb(float r, float g, float b, bool lit = false) =>
            new((byte)Mathf.RoundToInt(Mathf.Clamp01(r) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(g) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(b) * 255f), (byte)(lit ? 255 : 0));

        static Color32 Srgb(Color color, bool lit = false) => Srgb(color.r, color.g, color.b, lit);

        /// <summary>
        /// The Mediterranean ground: sand on the flat shore, pale limestone on the cliffs and up high, and on the rest
        /// dry golden grass and olive-green scrub in patches, darkening to pine wood.
        /// </summary>
        static Color32 GroundColor(Vector2 p, float height, Vector3 normal, int seed, float patchSize)
        {
            var steep = 1f - normal.y;
            var patches = Fbm2(p / patchSize, 3, seed);
            var grain = Perlin(p.x / 9f + seed, p.y / 9f);
            var scrub = Color.Lerp(new Color(0.6f, 0.53f, 0.32f), new Color(0.33f, 0.37f, 0.2f), Edge(0.34f, 0.48f, patches));
            var ground = Color.Lerp(scrub, new Color(0.19f, 0.25f, 0.13f), Edge(0.5f, 0.64f, patches) * 0.9f);
            var rock = Color.Lerp(new Color(0.66f, 0.6f, 0.5f), new Color(0.52f, 0.47f, 0.41f), grain);
            var rocky = Mathf.Max(Edge(0.26f, 0.42f, steep), Edge(250f, 320f, height) * 0.8f);
            ground = Color.Lerp(ground, rock, rocky);
            var beach = (1f - Edge(1.2f, 3.2f, height)) * (1f - Edge(0.14f, 0.3f, steep));
            ground = Color.Lerp(ground, new Color(0.84f, 0.76f, 0.6f), beach);
            if (height < 0.6f)
                ground = Color.Lerp(ground, new Color(0.6f, 0.55f, 0.45f), 0.6f);
            return Srgb(ground * (0.92f + 0.16f * grain));
        }

        // ---------------------------------------------------------------- Trees

        /// <summary>
        /// Trees and scrub wherever the ground isn't too steep, bare or built on, in clumps: dark cypress spires, flat
        /// umbrella pines on the heights, and silvery olive bushes lower down.
        /// </summary>
        static void Plant(LandMesh mesh, Heightfield terrain, Vector2 center, float radius, System.Random random, List<(Vector2 position, float radius)> buildings)
        {
            const float spacing = 4.5f;
            var seed = random.Next(1000);
            for (var x = -radius * 1.3f; x <= radius * 1.3f; x += spacing)
            {
                for (var z = -radius * 1.3f; z <= radius * 1.3f; z += spacing)
                {
                    var p = center + new Vector2(x, z) + new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * spacing;
                    var height = terrain.Sample(p);
                    var normal = terrain.Normal(p);
                    if (height < 1.8f || normal.y < 0.72f)
                        continue;
                    var clump = Fbm2(p / 40f, 2, seed);
                    var chance = Mathf.Lerp(0.05f, 0.75f, Edge(0.4f, 0.62f, clump));
                    if (random.NextDouble() > chance || Crowded(p, 3.5f, buildings))
                        continue;

                    var foot = new Vector3(p.x, SeaLevel + height - 0.4f, p.y);
                    var kind = random.NextDouble();
                    var size = 0.8f + 0.45f * (float)random.NextDouble();
                    var shade = 0.85f + 0.3f * (float)random.NextDouble();
                    if (kind < 0.2f && height > 8f)
                        UmbrellaPine(mesh, foot, size, shade, random);
                    else if (kind < 0.42f)
                        Cypress(mesh, foot, size, shade, random);
                    else
                        Bush(mesh, foot, size, shade, random);
                }
            }
        }

        static bool Crowded(Vector2 p, float clearance, List<(Vector2 position, float radius)> buildings)
        {
            foreach (var (position, radius) in buildings)
            {
                if ((p - position).sqrMagnitude < (radius + clearance) * (radius + clearance))
                    return true;
            }

            return false;
        }

        static void Cypress(LandMesh mesh, Vector3 foot, float size, float shade, System.Random random)
        {
            var height = 11f * size;
            var width = 1.3f * size;
            mesh.Lathe(foot, new[] { (0f, 0f), (0.02f, width * 0.6f), (0.12f * height, width), (0.4f * height, width * 0.95f), (0.7f * height, width * 0.65f), (0.9f * height, width * 0.25f), (height, 0f) },
                6, Srgb(new Color(0.11f, 0.17f, 0.09f) * shade), 0.35f, (float)random.NextDouble());
        }

        static void UmbrellaPine(LandMesh mesh, Vector3 foot, float size, float shade, System.Random random)
        {
            var height = 8.5f * size;
            var crown = 5f * size;
            var lean = new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * 1.6f;
            var trunkColor = Srgb(0.3f, 0.22f, 0.16f);
            mesh.Lathe(foot, new[] { (0f, 0f), (0f, 0.35f * size), (height * 0.7f, 0.22f * size), (height * 0.7f, 0f) }, 4, trunkColor);
            // The crown: a broad flat canopy made of a few overlapping clumps, so its edge is ragged.
            var top = foot + lean + Vector3.up * (height * 0.6f);
            var color = Srgb(new Color(0.25f, 0.33f, 0.16f) * shade);
            var clumps = 3 + random.Next(2);
            var start = (float)random.NextDouble() * 360f;
            for (var i = -1; i < clumps; i++)
            {
                var spread = i < 0 ? Vector3.zero : Quaternion.Euler(0f, start + i * 360f / clumps, 0f) * Vector3.forward * (crown * 0.5f);
                var radius = crown * (i < 0 ? 0.65f : 0.55f + 0.15f * (float)random.NextDouble());
                var rise = (i < 0 ? 0.4f : (float)random.NextDouble() * 0.6f - 0.3f) * size;
                var thick = (2.8f + 1f * (float)random.NextDouble()) * size;
                mesh.Lathe(top + spread + Vector3.up * rise, new[] { (0f, 0f), (0.15f * thick, radius * 0.8f), (0.45f * thick, radius), (0.8f * thick, radius * 0.6f), (thick, 0f) },
                    6, color, 0.6f, (float)random.NextDouble());
            }
        }

        static void Bush(LandMesh mesh, Vector3 foot, float size, float shade, System.Random random)
        {
            var radius = 2.8f * size;
            var height = 3.4f * size;
            var color = Color.Lerp(new Color(0.36f, 0.4f, 0.27f), new Color(0.26f, 0.32f, 0.17f), (float)random.NextDouble());
            mesh.Lathe(foot, new[] { (0f, 0f), (0.1f, radius * 0.8f), (height * 0.45f, radius), (height * 0.85f, radius * 0.6f), (height, 0f) },
                6, Srgb(color * shade), 0.6f, (float)random.NextDouble());
        }

        // ---------------------------------------------------------------- Villages

        static readonly Color[] WallColors =
        {
            new(0.94f, 0.92f, 0.87f), new(0.94f, 0.92f, 0.87f), new(0.95f, 0.93f, 0.9f), new(0.93f, 0.87f, 0.74f),
            new(0.9f, 0.74f, 0.52f), new(0.92f, 0.72f, 0.64f), new(0.96f, 0.86f, 0.6f),
        };

        static readonly Color[] ShutterColors = { new(0.16f, 0.36f, 0.62f), new(0.2f, 0.42f, 0.32f), new(0.12f, 0.45f, 0.55f), new(0.45f, 0.26f, 0.17f) };

        /// <summary>
        /// A whitewashed village climbing the slope that faces the casino: houses round the lowest stretch of shore
        /// on that side, a church with a blue dome above them, and a lighthouse out on a point.
        /// </summary>
        static void BuildVillage(LandMesh mesh, Heightfield terrain, Vector2 center, float radius, float homeward, System.Random random, List<(Vector2 position, float radius)> buildings)
        {
            var spots = BuildingSpots(terrain, center, radius, homeward, 70f, 2.5f, 32f, random);
            if (spots.Count == 0)
                return;

            // The village gathers round the shore nearest the casino.
            var heart = spots[0];
            foreach (var spot in spots)
            {
                if (spot.height < 9f && Vector2.Dot(spot.position - center, Facing(homeward)) > Vector2.Dot(heart.position - center, Facing(homeward)))
                    heart = spot;
            }

            spots.Sort((a, b) => (a.position - heart.position).sqrMagnitude.CompareTo((b.position - heart.position).sqrMagnitude));

            // The church: the flattest ground a little way up behind the houses.
            (Vector2 position, float drop)? church = null;
            foreach (var spot in spots)
            {
                var distance = (spot.position - heart.position).magnitude;
                if (spot.height < 8f || distance < 15f || distance > 75f)
                    continue;
                var drop = Ground(terrain, spot.position, homeward + 20f, new Vector2(13f, 14f)).drop;
                if (church == null || drop < church.Value.drop)
                    church = (spot.position, drop);
            }

            if (church is { } site)
            {
                Church(mesh, terrain, site.position, homeward + 20f, random);
                buildings.Add((site.position, 10f));
            }

            var houses = 0;
            foreach (var spot in spots)
            {
                if (houses >= 26 || (spot.position - heart.position).magnitude > 70f)
                    break;
                var size = new Vector3(6f + 4f * (float)random.NextDouble(), 0f, 5.5f + 3f * (float)random.NextDouble());
                var footprint = Mathf.Max(size.x, size.z) * 0.55f;
                if (Crowded(spot.position, footprint + 2.5f, buildings))
                    continue;
                House(mesh, terrain, spot.position, homeward + 25f + (float)(random.NextDouble() - 0.5) * 24f, size, random, true);
                buildings.Add((spot.position, footprint));
                houses++;
            }

            // The lighthouse: the low point that sticks out furthest to the north of the village.
            var cape = Facing(homeward + 70f);
            (Vector2 position, float height)? best = null;
            foreach (var spot in BuildingSpots(terrain, center, radius, homeward + 70f, 80f, 3f, 14f, random))
            {
                if (best == null || Vector2.Dot(spot.position - center, cape) > Vector2.Dot(best.Value.position - center, cape))
                    best = spot;
            }

            if (best != null && !Crowded(best.Value.position, 6f, buildings))
            {
                Lighthouse(mesh, terrain, best.Value.position, homeward, random);
                buildings.Add((best.Value.position, 6f));
            }
        }

        /// <summary>A chapel with a few houses round it on the island's casino side.</summary>
        static void BuildHamlet(LandMesh mesh, Heightfield terrain, Vector2 center, float radius, float homeward, System.Random random, List<(Vector2 position, float radius)> buildings)
        {
            var spots = BuildingSpots(terrain, center, radius, homeward, 60f, 3f, 24f, random);
            if (spots.Count == 0)
                return;
            spots.Sort((a, b) => b.height.CompareTo(a.height));
            var chapel = spots[0];
            Chapel(mesh, terrain, chapel.position, homeward + 15f, random);
            buildings.Add((chapel.position, 7f));
            spots.Sort((a, b) => (a.position - chapel.position).sqrMagnitude.CompareTo((b.position - chapel.position).sqrMagnitude));
            var houses = 0;
            foreach (var spot in spots)
            {
                if (houses >= 4)
                    break;
                var size = new Vector3(6f + 2f * (float)random.NextDouble(), 0f, 5f + 2f * (float)random.NextDouble());
                if (Crowded(spot.position, 5f, buildings))
                    continue;
                House(mesh, terrain, spot.position, homeward + 20f + (float)(random.NextDouble() - 0.5) * 30f, size, random, true);
                buildings.Add((spot.position, 4.5f));
                houses++;
            }
        }

        static Vector2 Facing(float yaw) => new(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));

        /// <summary>
        /// Places fit to build on: ground between the heights given, gentle enough, on the side of the island that
        /// faces <paramref name="yaw"/> (within <paramref name="spread"/> degrees), in a jittered order.
        /// </summary>
        static List<(Vector2 position, float height)> BuildingSpots(Heightfield terrain, Vector2 center, float radius, float yaw, float spread, float low, float high, System.Random random)
        {
            var spots = new List<(Vector2, float)>();
            var facing = Facing(yaw);
            var cos = Mathf.Cos(spread * Mathf.Deg2Rad);
            for (var x = -radius * 1.2f; x <= radius * 1.2f; x += 2.5f)
            {
                for (var z = -radius * 1.2f; z <= radius * 1.2f; z += 2.5f)
                {
                    var offset = new Vector2(x, z) + new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 2f;
                    if (offset.magnitude < 1f || Vector2.Dot(offset.normalized, facing) < cos)
                        continue;
                    var p = center + offset;
                    var height = terrain.Sample(p);
                    if (height >= low && height <= high && terrain.Normal(p).y > 0.88f)
                        spots.Add((p, height));
                }
            }

            return spots;
        }

        /// <summary>The ground under a footprint: where to stand a building, and how far its walls must reach down.</summary>
        static (float floor, float drop) Ground(Heightfield terrain, Vector2 position, float yaw, Vector2 size)
        {
            var low = float.MaxValue;
            var high = float.MinValue;
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            for (var i = -1; i <= 1; i++)
            {
                for (var j = -1; j <= 1; j++)
                {
                    var corner = rotation * new Vector3(i * size.x / 2f, 0f, j * size.y / 2f);
                    var height = terrain.Sample(position + new Vector2(corner.x, corner.z));
                    low = Mathf.Min(low, height);
                    high = Mathf.Max(high, height);
                }
            }

            // Stand it on the high side so it never floats; its walls run down past the low side into the ground.
            return (SeaLevel + high - 0.2f, high - low + 1.5f);
        }

        /// <summary>
        /// A village house: whitewashed (or ochre, or rose) walls, a flat roof with a parapet or a terracotta hipped
        /// one, shuttered windows on its front and sides (some lit for the evening) and a door.
        /// </summary>
        static void House(LandMesh mesh, Heightfield terrain, Vector2 position, float yaw, Vector3 size, System.Random random, bool detailed)
        {
            var storeys = random.NextDouble() < 0.55 ? 2 : 1;
            var height = storeys * 3f + 0.4f;
            var (floor, drop) = Ground(terrain, position, yaw, new Vector2(size.x, size.z));
            var foot = new Vector3(position.x, floor, position.y);
            var wall = WallColors[random.Next(WallColors.Length)] * (0.95f + 0.05f * (float)random.NextDouble());
            mesh.Box(foot + Vector3.down * drop, new Vector3(size.x, height + drop, size.z), yaw, Srgb(wall));

            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var top = foot + Vector3.up * height;
            if (random.NextDouble() < 0.5)
            {
                var whiter = Color.Lerp(wall, Color.white, 0.4f);
                mesh.Box(top, new Vector3(size.x + 0.3f, 0.45f, size.z + 0.3f), yaw, Srgb(whiter));
                if (random.NextDouble() < 0.4)
                    mesh.Box(top + rotation * new Vector3(size.x * 0.25f, 0f, -size.z * 0.2f), new Vector3(2.4f, 2.4f, 2.4f), yaw, Srgb(whiter));
            }
            else
            {
                HipRoof(mesh, top, size.x + 0.6f, size.z + 0.6f, Mathf.Min(size.x, size.z) * 0.32f, yaw,
                    Srgb(Color.Lerp(new Color(0.72f, 0.36f, 0.22f), new Color(0.62f, 0.3f, 0.2f), (float)random.NextDouble())));
            }

            if (!detailed)
            {
                Windows(mesh, foot, rotation, size, storeys, Vector3.forward, random, null, 0.5f);
                return;
            }

            var shutters = random.NextDouble() < 0.7 ? ShutterColors[random.Next(ShutterColors.Length)] : (Color?)null;
            Windows(mesh, foot, rotation, size, storeys, Vector3.forward, random, shutters, 0.4f);
            Windows(mesh, foot, rotation, size, storeys, Vector3.right, random, shutters, 0.3f);
            Windows(mesh, foot, rotation, size, storeys, Vector3.left, random, shutters, 0.3f);

            // In the gap Windows leaves for it in the middle of the front's ground floor.
            var count = Mathf.Max(1, Mathf.FloorToInt(size.x / 2.8f));
            var door = rotation * new Vector3((count / 2 + 0.5f) / count * size.x - size.x / 2f, 0f, size.z / 2f + 0.03f);
            var doorColor = shutters ?? new Color(0.35f, 0.24f, 0.16f);
            Panel(mesh, foot + door + Vector3.up * 1.05f, rotation, 1.1f, 2.1f, Srgb(doorColor * 0.8f));
        }

        /// <summary>A row of windows per storey on one wall (a local direction), some lit, with shutters either side.</summary>
        static void Windows(LandMesh mesh, Vector3 foot, Quaternion rotation, Vector3 size, int storeys, Vector3 side, System.Random random, Color? shutters, float litChance)
        {
            var along = side == Vector3.forward ? size.x : size.z;
            var depth = side == Vector3.forward ? size.z : size.x;
            var count = Mathf.Max(1, Mathf.FloorToInt(along / 2.8f));
            var facing = rotation * Quaternion.LookRotation(side);
            for (var storey = 0; storey < storeys; storey++)
            {
                for (var i = 0; i < count; i++)
                {
                    if (storey == 0 && side == Vector3.forward && i == count / 2)
                        continue;
                    var x = (i + 0.5f) / count * along - along / 2f;
                    var center = foot + facing * new Vector3(x, 1.7f + storey * 3f, depth / 2f + 0.03f);
                    var lit = random.NextDouble() < litChance;
                    Panel(mesh, center, facing, 0.9f, 1.25f, lit ? Srgb(0.3f, 0.2f, 0.1f, true) : Srgb(0.1f, 0.1f, 0.12f));
                    if (shutters is { } color)
                    {
                        var right = facing * Vector3.right;
                        Panel(mesh, center + right * 0.7f, facing, 0.45f, 1.25f, Srgb(color));
                        Panel(mesh, center - right * 0.7f, facing, 0.45f, 1.25f, Srgb(color));
                    }
                }
            }
        }

        /// <summary>
        /// A flat rectangle on a wall, facing out along <paramref name="facing"/>'s forward. Its alpha marks it as on a
        /// wall (192, or 255 lit), for the shader to keep it in front of the wall far away.
        /// </summary>
        static void Panel(LandMesh mesh, Vector3 center, Quaternion facing, float width, float height, Color32 color)
        {
            color.a = (byte)Mathf.Max(color.a, 192);
            var right = facing * Vector3.right * (width / 2f);
            var up = Vector3.up * (height / 2f);
            mesh.Face(color, facing * Vector3.forward, center - right - up, center + right - up, center + right + up, center - right + up);
        }

        /// <summary>A hipped roof over a width x depth rectangle: two sloping trapezoids and two triangular ends.</summary>
        static void HipRoof(LandMesh mesh, Vector3 eaves, float width, float depth, float rise, float yaw, Color32 color)
        {
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var alongX = width >= depth;
            var ridge = Mathf.Abs(width - depth) / 2f;
            Vector3 P(float x, float y, float z) => eaves + rotation * new Vector3(x, y, z);
            var w = width / 2f;
            var d = depth / 2f;
            var r0 = alongX ? P(-ridge, rise, 0f) : P(0f, rise, -ridge);
            var r1 = alongX ? P(ridge, rise, 0f) : P(0f, rise, ridge);
            Vector3 c00 = P(-w, 0f, -d), c10 = P(w, 0f, -d), c11 = P(w, 0f, d), c01 = P(-w, 0f, d);
            var middle = P(0f, 0f, 0f);
            Vector3 Out(params Vector3[] points)
            {
                var sum = Vector3.zero;
                foreach (var point in points)
                    sum += point;
                return sum / points.Length - middle + Vector3.up * 0.01f;
            }

            if (alongX)
            {
                mesh.Face(color, Out(c00, c10, r1, r0), c00, c10, r1, r0);
                mesh.Face(color, Out(c01, c11, r1, r0), c11, c01, r0, r1);
                mesh.Face(color, Out(c00, c01, r0), c01, c00, r0);
                mesh.Face(color, Out(c10, c11, r1), c10, c11, r1);
            }
            else
            {
                mesh.Face(color, Out(c00, c01, r1, r0), c01, c00, r0, r1);
                mesh.Face(color, Out(c10, c11, r1, r0), c10, c11, r1, r0);
                mesh.Face(color, Out(c00, c10, r0), c00, c10, r0);
                mesh.Face(color, Out(c01, c11, r1), c11, c01, r1);
            }
        }

        /// <summary>A white church with a blue dome over its front and a bell tower beside it.</summary>
        static void Church(LandMesh mesh, Heightfield terrain, Vector2 position, float yaw, System.Random random)
        {
            var size = new Vector3(8f, 0f, 14f);
            var (floor, drop) = Ground(terrain, position, yaw, new Vector2(size.x + 5f, size.z));
            var foot = new Vector3(position.x, floor, position.y);
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var white = Srgb(0.96f, 0.95f, 0.92f);
            var blue = Srgb(0.13f, 0.32f, 0.68f);
            mesh.Box(foot + Vector3.down * drop, new Vector3(size.x, 7.5f + drop, size.z), yaw, white);
            HipRoof(mesh, foot + Vector3.up * 7.5f, size.x + 0.4f, size.z + 0.4f, 1.6f, yaw, Srgb(0.7f, 0.36f, 0.23f));

            // The dome on a drum over the front half.
            var domeFoot = foot + rotation * new Vector3(0f, 7.5f, size.z * 0.18f);
            mesh.Box(domeFoot, new Vector3(5.2f, 1.2f, 5.2f), yaw, white);
            var drum = domeFoot + Vector3.up * 1.2f;
            mesh.Lathe(drum, new[] { (0f, 0f), (0f, 2.4f), (1.6f, 2.4f), (1.6f, 0f) }, 10, white);
            var dome = new List<(float, float)>();
            for (var i = 0; i <= 6; i++)
            {
                var angle = i / 6f * Mathf.PI / 2f;
                dome.Add((1.6f + Mathf.Sin(angle) * 2.6f, Mathf.Cos(angle) * 2.55f));
            }

            mesh.Lathe(drum, dome.ToArray(), 12, blue, 0f);
            mesh.Box(drum + Vector3.up * 4.2f, new Vector3(0.15f, 1.4f, 0.15f), yaw, white);
            mesh.Box(drum + Vector3.up * 5f, new Vector3(0.8f, 0.15f, 0.15f), yaw, white);

            // The bell tower at the front corner, its belfry open on every side.
            var tower = foot + rotation * new Vector3(size.x / 2f + 1.6f, 0f, size.z / 2f - 1.6f);
            mesh.Box(tower + Vector3.down * drop, new Vector3(3.2f, 10f + drop, 3.2f), yaw, white);
            foreach (var side in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                var facing = rotation * Quaternion.LookRotation(side);
                Panel(mesh, tower + facing * new Vector3(0f, 8.6f, 1.63f), facing, 1.3f, 2f, Srgb(0.08f, 0.08f, 0.1f));
            }

            mesh.Lathe(tower + Vector3.up * 10f, new[] { (0f, 0f), (0f, 1.5f), (0.5f, 1.5f), (1.6f, 0.9f), (2.2f, 0f) }, 8, blue, 0.2f);
            Windows(mesh, foot, rotation, size, 1, Vector3.right, random, null, 0.6f);
            Windows(mesh, foot, rotation, size, 1, Vector3.left, random, null, 0.6f);
            Panel(mesh, foot + rotation * new Vector3(0f, 1.6f, size.z / 2f + 0.03f), rotation, 1.8f, 3.2f, Srgb(0.4f, 0.26f, 0.15f));
        }

        /// <summary>A little white chapel with a blue barrel roof and a bell gable.</summary>
        static void Chapel(LandMesh mesh, Heightfield terrain, Vector2 position, float yaw, System.Random random)
        {
            var size = new Vector3(5f, 0f, 8f);
            var (floor, drop) = Ground(terrain, position, yaw, new Vector2(size.x, size.z));
            var foot = new Vector3(position.x, floor, position.y);
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var white = Srgb(0.96f, 0.95f, 0.92f);
            mesh.Box(foot + Vector3.down * drop, new Vector3(size.x, 4.5f + drop, size.z), yaw, white);
            HipRoof(mesh, foot + Vector3.up * 4.5f, size.x + 0.3f, size.z + 0.3f, 1.4f, yaw, Srgb(0.15f, 0.34f, 0.66f));
            mesh.Box(foot + rotation * new Vector3(0f, 4.5f, size.z / 2f - 0.3f), new Vector3(2.2f, 3.2f, 0.6f), yaw, white);
            var facing = rotation;
            Panel(mesh, foot + rotation * new Vector3(0f, 6.6f, size.z / 2f + 0.03f), facing, 0.8f, 1.1f, Srgb(0.08f, 0.08f, 0.1f));
            Panel(mesh, foot + rotation * new Vector3(0f, 1.3f, size.z / 2f + 0.03f), facing, 1.2f, 2.4f, Srgb(0.13f, 0.3f, 0.6f));
            Windows(mesh, foot, rotation, size, 1, Vector3.right, random, null, 0.5f);
        }

        /// <summary>A white lighthouse with a red band and its lamp lit, and the keeper's cottage.</summary>
        static void Lighthouse(LandMesh mesh, Heightfield terrain, Vector2 position, float yaw, System.Random random)
        {
            var (floor, drop) = Ground(terrain, position, yaw, new Vector2(4.5f, 4.5f));
            var foot = new Vector3(position.x, floor, position.y);
            var white = Srgb(0.95f, 0.94f, 0.9f);
            var red = Srgb(0.7f, 0.15f, 0.12f);
            mesh.Lathe(foot + Vector3.down * drop, new[] { (0f, 0f), (0f, 2.3f), (drop + 8f, 1.95f), (drop + 8f, 0f) }, 12, white);
            mesh.Lathe(foot + Vector3.up * 8f, new[] { (0f, 0f), (0f, 1.95f), (3.5f, 1.8f), (3.5f, 0f) }, 12, red);
            mesh.Lathe(foot + Vector3.up * 11.5f, new[] { (0f, 0f), (0f, 1.8f), (3f, 1.65f), (3f, 0f) }, 12, white);
            var gallery = foot + Vector3.up * 14.5f;
            mesh.Lathe(gallery, new[] { (0f, 0f), (0f, 2.3f), (0.35f, 2.3f), (0.35f, 0f) }, 12, Srgb(0.15f, 0.15f, 0.16f));
            mesh.Lathe(gallery + Vector3.up * 0.35f, new[] { (0f, 0f), (0f, 1.25f), (1.9f, 1.25f), (1.9f, 0f) }, 10, Srgb(1f, 0.85f, 0.55f, true));
            mesh.Lathe(gallery + Vector3.up * 2.25f, new[] { (0f, 0f), (0f, 1.5f), (0.9f, 0.5f), (1.3f, 0f) }, 10, red, 0.3f);
            var cottage = position + Facing(yaw + 90f) * 6.5f;
            House(mesh, terrain, cottage, yaw, new Vector3(6f, 0f, 5f), random, true);
        }

        // ---------------------------------------------------------------- The far coast

        /// <summary>
        /// The far shore of the bay, across the water to the south-east: from a low cape in the east it runs round to
        /// the south and rises behind its beaches into foothills and then a range of mountains, with a harbour town on
        /// the shore. The ground ends past the far side of the range, out of sight.
        /// </summary>
        static void BuildFarCoast(Transform parent, Material material)
        {
            const float east = 116f;
            const float west = 252f;
            var o = 4.7f;

            float Coast(float yaw) => 780f + 520f * (1f - Edge(118f, 168f, yaw)) + 620f * Edge(222f, 252f, yaw)
                                      + 160f * (Fbm2(new Vector2(yaw / 14f, o), 3, o) - 0.5f);

            float Height(Vector2 p)
            {
                var offset = p - SeaCenter;
                var distance = offset.magnitude;
                var yaw = Mathf.Repeat(Mathf.Atan2(offset.x, offset.y) * Mathf.Rad2Deg, 360f);
                var inland = distance - Coast(yaw);
                var taper = Edge(east, 134f, yaw) * (1f - Edge(230f, west, yaw));
                var warp = new Vector2(Fbm2(p / 600f, 3, o + 2f), Fbm2(p / 600f, 3, o + 9f)) * 400f;
                var ridges = Ridged((p + warp) / 520f, 6, o);
                var range = 40f * Edge(-40f, 260f, inland) + 170f * Edge(180f, 900f, inland) + 150f * Edge(800f, 1350f, inland);
                range *= Mathf.Lerp(0.35f, 1f, taper);
                var land = range * (0.3f + 0.85f * ridges) + 10f * Fbm2(p / 90f, 3, o) - 2f;
                return Mathf.Lerp(-12f, land, Edge(-70f, 30f, inland));
            }

            // The terrain on a grid covering the coast's sector, worn down by rain.
            var corners = new List<Vector2>();
            for (var yaw = east; yaw <= west; yaw += 4f)
            {
                corners.Add(FromTerrace(yaw, FarCoastReach));
                corners.Add(FromTerrace(yaw, Coast(yaw) - 120f));
            }

            Vector2 min = corners[0], max = corners[0];
            foreach (var corner in corners)
            {
                min = Vector2.Min(min, corner);
                max = Vector2.Max(max, corner);
            }

            const float cell = 7f;
            var field = new Heightfield(min, Mathf.CeilToInt((max.x - min.x) / cell) + 1, Mathf.CeilToInt((max.y - min.y) / cell) + 1, cell);
            field.Fill(Height);
            field.Erode(field.Width * field.Depth, 360f, 5);

            // The mesh fans out from the terrace, so it's as fine near the shore as it is far up the mountains.
            var mesh = new LandMesh();
            const float step = 0.3f;
            var columns = Mathf.CeilToInt((west - east) / step) + 1;
            const int rows = 90;
            var heights = new float[columns * rows];
            for (var c = 0; c < columns; c++)
            {
                var yaw = east + c * step;
                var near = Coast(yaw) - 100f;
                for (var r = 0; r < rows; r++)
                {
                    var p = FromTerrace(yaw, Mathf.Lerp(near, FarCoastReach, Mathf.Pow(r / (rows - 1f), 1.6f)));
                    var height = heights[c * rows + r] = field.Sample(p);
                    var normal = field.Normal(p);
                    mesh.Add(new Vector3(p.x, SeaLevel + height, p.y), normal, GroundColor(p, height, normal, 5, 70f));
                }
            }

            for (var c = 0; c < columns - 1; c++)
            {
                for (var r = 0; r < rows - 1; r++)
                {
                    var a = c * rows + r;
                    if (Mathf.Max(Mathf.Max(heights[a], heights[a + 1]), Mathf.Max(heights[a + rows], heights[a + rows + 1])) < -5f)
                        continue;
                    mesh.Triangle(a, a + rows, a + 1);
                    mesh.Triangle(a + 1, a + rows, a + rows + 1);
                }
            }

            mesh.Building = true;
            var random = new System.Random(9);
            var buildings = new List<(Vector2 position, float radius)>();
            foreach (var (yaw, houses, radius, tower) in Towns)
                BuildTown(mesh, field, yaw, Coast(yaw), houses, radius, tower, buildings, random);
            BuildFarmhouses(mesh, field, east + 4f, west - 6f, Coast, buildings, random);
            AddLand(parent, "FarCoast", mesh, new Vector3(SeaCenter.x, SeaLevel, SeaCenter.y), material);
        }

        /// <summary>
        /// The towns and villages along the far shore, east to west: where (yaw from the terrace), how many houses, how
        /// far they spread from the harbour, and whether a church tower stands among them. The shore east of 140° is
        /// behind the village island.
        /// </summary>
        static readonly (float yaw, int houses, float radius, bool tower)[] Towns =
        {
            (146f, 22, 120f, true),
            (166f, 60, 200f, true),
            (186f, 22, 120f, false),
            (205f, 30, 150f, true),
            (226f, 20, 110f, false),
            (243f, 14, 100f, false),
        };

        /// <summary>
        /// A town on the far shore: houses crowding the low ground round its harbour, thinning out up the slopes, and
        /// maybe a church tower.
        /// </summary>
        static void BuildTown(LandMesh mesh, Heightfield terrain, float yaw, float coast, int count, float radius, bool tower,
            List<(Vector2 position, float radius)> buildings, System.Random random)
        {
            var homeward = yaw + 180f;
            var heart = FromTerrace(yaw, coast + 40f);
            var spots = new List<Vector2>();
            for (var x = -radius; x <= radius; x += 9f)
            {
                for (var z = -radius; z <= radius; z += 9f)
                {
                    var p = heart + new Vector2(x, z) + new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 6f;
                    var height = terrain.Sample(p);
                    if (height > 2f && height < 45f && terrain.Normal(p).y > 0.9f)
                        spots.Add(p);
                }
            }

            spots.Sort((a, b) => (a - heart).sqrMagnitude.CompareTo((b - heart).sqrMagnitude));
            var houses = 0;
            var towerAt = tower ? Mathf.Min(count / 4, 10) : -1;
            foreach (var position in spots)
            {
                if (houses >= count)
                    break;
                if (random.NextDouble() < (position - heart).magnitude / radius || Crowded(position, 5.5f, buildings))
                    continue;
                if (houses == towerAt)
                {
                    var (floor, drop) = Ground(terrain, position, homeward, new Vector2(5f, 5f));
                    var height = 20f + 6f * (float)random.NextDouble();
                    mesh.Box(new Vector3(position.x, floor - drop, position.y), new Vector3(5f, height + drop, 5f), homeward, Srgb(0.92f, 0.86f, 0.74f));
                    HipRoof(mesh, new Vector3(position.x, floor + height, position.y), 5.6f, 5.6f, 4f, homeward, Srgb(0.68f, 0.34f, 0.22f));
                    buildings.Add((position, 6f));
                }
                else
                {
                    var size = new Vector3(8f + 6f * (float)random.NextDouble(), 0f, 7f + 4f * (float)random.NextDouble());
                    House(mesh, terrain, position, homeward + (float)(random.NextDouble() - 0.5) * 30f, size, random, false);
                    buildings.Add((position, Mathf.Max(size.x, size.z) * 0.55f));
                }

                houses++;
            }
        }

        /// <summary>Farmhouses and villas dotted over the foothills between the towns, each on a gentle spot of its own.</summary>
        static void BuildFarmhouses(LandMesh mesh, Heightfield terrain, float east, float west, System.Func<float, float> coast,
            List<(Vector2 position, float radius)> buildings, System.Random random)
        {
            var placed = 0;
            for (var attempt = 0; attempt < 4000 && placed < 46; attempt++)
            {
                var yaw = Mathf.Lerp(east, west, (float)random.NextDouble());
                var inland = Mathf.Lerp(40f, 650f, Mathf.Pow((float)random.NextDouble(), 1.6f));
                var position = FromTerrace(yaw, coast(yaw) + inland);
                var height = terrain.Sample(position);
                if (height < 6f || height > 130f || terrain.Normal(position).y < 0.88f || Crowded(position, 45f, buildings))
                    continue;
                var size = new Vector3(7f + 5f * (float)random.NextDouble(), 0f, 6f + 3f * (float)random.NextDouble());
                House(mesh, terrain, position, yaw + 180f + (float)(random.NextDouble() - 0.5) * 40f, size, random, false);
                buildings.Add((position, Mathf.Max(size.x, size.z) * 0.55f));
                placed++;
            }
        }
    }
}
