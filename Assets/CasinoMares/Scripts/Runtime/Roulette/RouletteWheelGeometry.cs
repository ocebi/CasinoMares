using System.Collections.Generic;
using CasinoMares.Rules;
using UnityEngine;

namespace CasinoMares.Roulette
{
    /// <summary>
    /// The shape of the roulette wheel (GDD §13.1) in the wheel's own frame: origin at the centre of the bowl on the
    /// felt, +Y up. The bowl and rotor meshes are lathed from the profiles below, and <see cref="RouletteBallPath"/>
    /// rides the same profiles, so the ball sits on the surfaces it is drawn on.
    /// </summary>
    public static class RouletteWheelGeometry
    {
        public const float BallRadius = 0.014f;

        /// <summary>The rotor's top reaches this far out; the wheel texture is laid over it flat, from above.</summary>
        public const float RotorRadius = 0.3f;

        /// <summary>Ball centre while it runs around the track, pressed against the bowl wall.</summary>
        public const float TrackRadius = 0.361f;

        /// <summary>Where the apron (the sloped part of the bowl) ends and the ball drops onto the rotor.</summary>
        public const float ApronInner = 0.305f;

        /// <summary>Ball centre once it rests in a pocket, between two frets.</summary>
        public const float PocketRadius = 0.208f;

        /// <summary>Radius of the eight brass deflectors ("diamonds") on the apron.</summary>
        public const float DiamondRadius = 0.335f;
        public const int DiamondCount = 8;

        const float FretInner = 0.19f;
        const float FretOuter = 0.236f;
        const float FretTop = 0.058f;
        const int Segments = 96;

        // (radius, height), walked from the outside in along the visible surface.
        static readonly Vector2[] BowlProfile =
        {
            new(0.42f, 0f), new(0.42f, 0.118f), new(0.405f, 0.135f), new(0.385f, 0.135f), new(0.376f, 0.124f), // wooden rim
            new(0.375f, 0.095f), new(0.37f, 0.092f), new(0.305f, 0.068f), new(0.304f, 0.03f), // ball track, apron
        };

        const int BowlRimSegments = 4;

        static readonly Vector2[] RotorProfile =
        {
            new(0.3f, 0.035f), new(0.3f, 0.062f), new(0.279f, 0.06f), new(0.258f, 0.056f), new(0.235f, 0.05f),
            new(0.19f, 0.042f), new(0.186f, 0.042f), new(0.186f, 0.058f), new(0.15f, 0.062f), new(0.04f, 0.095f), new(0f, 0.1f),
        };

        static Mesh _bowl;
        static Mesh _rotor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _bowl = _rotor = null;

        /// <summary>Bowl: submesh 0 the wooden rim, 1 the ball track and apron, 2 the brass diamonds.</summary>
        public static Mesh Bowl => _bowl ??= BuildBowl();

        /// <summary>Rotor: submesh 0 the painted wheel (planar UVs over <see cref="RouletteWheelTexture"/>), 1 the brass frets and turret.</summary>
        public static Mesh Rotor => _rotor ??= BuildRotor();

        /// <summary>Height of the apron or rotor surface under a point this far from the centre.</summary>
        public static float SurfaceHeight(float radius)
        {
            if (radius >= ApronInner)
                return ProfileHeight(BowlProfile, radius);

            // The few millimetres between the apron and the rotor edge: ease down so the ball doesn't snap.
            var rotor = ProfileHeight(RotorProfile, radius);
            var blend = Mathf.InverseLerp(ApronInner - 0.015f, ApronInner, radius);
            return Mathf.Lerp(rotor, ProfileHeight(BowlProfile, ApronInner), blend * blend);
        }

        /// <summary>Height of the ball's centre when it rests on the surface at this radius.</summary>
        public static float RestingHeight(float radius) => SurfaceHeight(radius) + BallRadius;

        static float ProfileHeight(Vector2[] profile, float radius)
        {
            for (var i = 0; i < profile.Length - 1; i++)
            {
                var outer = profile[i];
                var inner = profile[i + 1];
                if (outer.x - inner.x < 0.0001f || radius > outer.x || radius < inner.x)
                    continue;
                return Mathf.Lerp(outer.y, inner.y, (outer.x - radius) / (outer.x - inner.x));
            }

            return profile[^1].y;
        }

        // ---------------------------------------------------------------- Meshes

        sealed class MeshParts
        {
            public readonly List<Vector3> Vertices = new();
            public readonly List<Vector3> Normals = new();
            public readonly List<Vector2> Uvs = new();
            public readonly List<int>[] Triangles;

            public MeshParts(int submeshes)
            {
                Triangles = new List<int>[submeshes];
                for (var i = 0; i < submeshes; i++)
                    Triangles[i] = new List<int>();
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(Vertices);
                mesh.SetNormals(Normals);
                mesh.SetUVs(0, Uvs);
                mesh.subMeshCount = Triangles.Length;
                for (var i = 0; i < Triangles.Length; i++)
                    mesh.SetTriangles(Triangles[i], i);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        static Mesh BuildBowl()
        {
            var parts = new MeshParts(3);
            Lathe(parts, BowlProfile, 0, BowlRimSegments, 0, false);
            Lathe(parts, BowlProfile, BowlRimSegments, BowlProfile.Length - 1, 1, false);

            // Diamonds alternate lying along the slope and across it, tilted to sit flat on the apron.
            var slope = Mathf.Atan2(BowlProfile[6].y - BowlProfile[7].y, BowlProfile[6].x - BowlProfile[7].x) * Mathf.Rad2Deg;
            for (var i = 0; i < DiamondCount; i++)
            {
                var angle = i * 360f / DiamondCount;
                var size = i % 2 == 0 ? new Vector3(0.012f, 0.007f, 0.034f) : new Vector3(0.03f, 0.007f, 0.012f);
                var rotation = Quaternion.Euler(0f, angle, 0f) * Quaternion.Euler(-slope, 0f, 0f);
                AddBox(parts, 2, OnCircle(angle, DiamondRadius, SurfaceHeight(DiamondRadius) + 0.002f), rotation, size);
            }

            return parts.ToMesh("RouletteBowl");
        }

        static Mesh BuildRotor()
        {
            var parts = new MeshParts(2);
            Lathe(parts, RotorProfile, 0, RotorProfile.Length - 1, 0, true);

            // A fret on every pocket boundary, standing on the sloped pocket floor.
            const float step = 360f / RouletteRules.Numbers;
            var middle = (FretInner + FretOuter) / 2f;
            var floor = Mathf.Min(SurfaceHeight(FretInner), SurfaceHeight(FretOuter)) - 0.002f;
            for (var i = 0; i < RouletteRules.Numbers; i++)
            {
                var angle = (i + 0.5f) * step;
                AddBox(parts, 1, OnCircle(angle, middle, (floor + FretTop) / 2f), Quaternion.Euler(0f, angle, 0f),
                    new Vector3(0.0025f, FretTop - floor, FretOuter - FretInner));
            }

            // The turret: a brass post with four little arms the croupier spins the rotor by.
            Lathe(parts, new[] { new Vector2(0.024f, 0.09f), new Vector2(0.024f, 0.13f), new Vector2(0.016f, 0.145f), new Vector2(0f, 0.15f) }, 0, 3, 1, false);
            for (var arm = 0; arm < 4; arm++)
            {
                var angle = arm * 90f + 45f;
                AddBox(parts, 1, OnCircle(angle, 0.045f, 0.122f), Quaternion.Euler(0f, angle, 0f), new Vector3(0.008f, 0.008f, 0.06f));
            }

            return parts.ToMesh("RouletteRotor");
        }

        /// <summary>Point at an angle (degrees, from +Z towards +X, clockwise seen from above) and radius.</summary>
        public static Vector3 OnCircle(float angle, float radius, float height)
        {
            var radians = angle * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians) * radius, height, Mathf.Cos(radians) * radius);
        }

        /// <summary>
        /// Spins profile segments [from, to) around Y. Each segment gets its own rings, so the creases between them
        /// stay sharp. Planar UVs map the texture from above, matching <see cref="RouletteWheelTexture"/>.
        /// </summary>
        static void Lathe(MeshParts parts, Vector2[] profile, int from, int to, int submesh, bool planarUvs)
        {
            for (var j = from; j < to; j++)
            {
                var a = profile[j];
                var b = profile[j + 1];
                var direction = b - a;
                var normal = new Vector2(direction.y, -direction.x).normalized;
                var start = parts.Vertices.Count;

                for (var i = 0; i <= Segments; i++)
                {
                    var angle = i * 360f / Segments;
                    var radians = angle * Mathf.Deg2Rad;
                    var outward = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                    foreach (var point in new[] { a, b })
                    {
                        parts.Vertices.Add(outward * point.x + Vector3.up * point.y);
                        parts.Normals.Add(outward * normal.x + Vector3.up * normal.y);
                        if (planarUvs)
                        {
                            // Stay inside the painted disc: its outermost texels are transparent.
                            var r = Mathf.Min(point.x, RotorRadius * 0.98f) / (2f * RotorRadius);
                            parts.Uvs.Add(new Vector2(0.5f + outward.x * r, 0.5f + outward.z * r));
                        }
                        else
                        {
                            parts.Uvs.Add(new Vector2(i / (float)Segments, point.y * 4f));
                        }
                    }
                }

                var triangles = parts.Triangles[submesh];
                for (var i = 0; i < Segments; i++)
                {
                    var aHere = start + i * 2;
                    var bHere = aHere + 1;
                    var aNext = aHere + 2;
                    var bNext = aHere + 3;
                    triangles.Add(aHere);
                    triangles.Add(aNext);
                    triangles.Add(bHere);
                    triangles.Add(aNext);
                    triangles.Add(bNext);
                    triangles.Add(bHere);
                }
            }
        }

        static readonly Vector3[] BoxNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        static void AddBox(MeshParts parts, int submesh, Vector3 center, Quaternion rotation, Vector3 size)
        {
            foreach (var normal in BoxNormals)
            {
                // Two axes spanning this face, ordered so the winding faces outwards.
                var u = normal.y != 0f ? Vector3.right : Vector3.up;
                var v = Vector3.Cross(normal, u);
                var start = parts.Vertices.Count;
                for (var corner = 0; corner < 4; corner++)
                {
                    var su = corner is 0 or 3 ? -0.5f : 0.5f;
                    var sv = corner < 2 ? -0.5f : 0.5f;
                    var local = Vector3.Scale(normal * 0.5f + u * su + v * sv, size);
                    parts.Vertices.Add(center + rotation * local);
                    parts.Normals.Add(rotation * normal);
                    parts.Uvs.Add(new Vector2(su + 0.5f, sv + 0.5f));
                }

                var triangles = parts.Triangles[submesh];
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
            }
        }
    }
}
