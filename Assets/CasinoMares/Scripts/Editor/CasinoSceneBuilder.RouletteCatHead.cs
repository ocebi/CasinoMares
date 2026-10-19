using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CasinoMares.Editor
{
    /// <summary>
    /// The roulette wheel as a cat head: two triangular wooden ears rising from the round rim of the wheel's bowl on
    /// the croupier's side, so from the seats the wheel reads as a cat's head. One generated mesh in the wheel's wood,
    /// with no collider: the table top, the wheel's collider, the seats and every camera stay where
    /// <see cref="BuildRouletteTable"/> put them, so the NavMesh carve is the same.
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        /// <summary>The ears' height from the rim to the tip, half the width of their base and their thickness.</summary>
        const float CatEarHeight = 0.2f;
        const float CatEarHalfWidth = 0.11f;
        const float CatEarThickness = 0.025f;
        /// <summary>Degrees each ear sits round the rim from the point straight across from the players.</summary>
        const float CatEarSpread = 40f;
        /// <summary>Degrees the ears lean back, away from the players.</summary>
        const float CatEarTilt = 15f;
        /// <summary>Where the ears stand on the bowl (RouletteWheelGeometry's wooden rim: radius 0.385–0.42, top 0.135).</summary>
        const float CatEarRadius = 0.405f;
        const float CatEarBase = 0.125f;

        /// <summary>Adds the ears round the wheel at <paramref name="wheelCenter"/> (table-local; the players sit at −z).</summary>
        static void BuildRouletteCatEars(Transform table, Vector3 wheelCenter, Materials m)
        {
            var ears = new GameObject("CatEars");
            ears.transform.SetParent(table, false);
            ears.transform.localPosition = wheelCenter;
            ears.AddComponent<MeshFilter>().sharedMesh = SaveMesh(CatEarsMesh(m), "Roulette_CatEars");
            ears.AddComponent<MeshRenderer>().sharedMaterial = m.Wood;
            GameObjectUtility.SetStaticEditorFlags(ears, StaticEditorFlags.BatchingStatic);
        }

        static Mesh CatEarsMesh(Materials m)
        {
            var tile = m.Wood != null && WorldUvTiles.TryGetValue(m.Wood, out var woodTile) ? woodTile : 1f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            // A flat face, corners wound clockwise seen from where it faces (Unity's front).
            void Face(params Vector3[] corners)
            {
                var normal = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]).normalized;
                var start = vertices.Count;
                foreach (var corner in corners)
                {
                    vertices.Add(corner);
                    normals.Add(normal);
                    uvs.Add(new Vector2(corner.x + corner.z, corner.y) / tile);
                }

                for (var i = 1; i < corners.Length - 1; i++)
                {
                    triangles.Add(start);
                    triangles.Add(start + i);
                    triangles.Add(start + i + 1);
                }
            }

            foreach (var side in new[] { -1f, 1f })
            {
                var angle = side * CatEarSpread * Mathf.Deg2Rad;
                var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var along = new Vector3(outward.z, 0f, -outward.x);
                var tilt = CatEarTilt * Mathf.Deg2Rad;
                var rise = Vector3.up * (CatEarHeight * Mathf.Cos(tilt)) + outward * (CatEarHeight * Mathf.Sin(tilt));
                var half = outward * (CatEarThickness * 0.5f);

                var centre = outward * CatEarRadius + Vector3.up * CatEarBase;
                var left = centre - along * CatEarHalfWidth;
                var right = centre + along * CatEarHalfWidth;
                var tip = centre + rise;

                // Front (toward the players), back, then the two sloping edges and the base.
                Vector3 front(Vector3 p) => p - half;
                Vector3 back(Vector3 p) => p + half;
                Face(front(left), front(tip), front(right));
                Face(back(right), back(tip), back(left));
                Face(back(left), back(tip), front(tip), front(left));
                Face(front(right), front(tip), back(tip), back(right));
                Face(front(left), front(right), back(right), back(left));
            }

            var mesh = new Mesh { name = "Roulette_CatEars" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
