using System.Collections.Generic;
using CasinoMares.Props;
using CasinoMares.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.Editor
{
    /// <summary>
    /// The cat wall: a climbing course up the seaside hall's west wall (the lounge doesn't have one). From a rug in
    /// the south-west corner it goes up a cat tree, along shelves to a rope bridge over the poker table's corner, onto
    /// a cat cube, down a row of stepping pads, then up again past a long jump to a cushion under the ceiling in the
    /// north-west corner, next to a window seat. Every step is within a cat's jump (a metre up; across, a little more
    /// running). <see cref="CatWall"/> times the climb.
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        /// <summary>The west wall's inside face, where the cat wall hangs.</summary>
        const float CatWallFace = -HallWidth / 2f + WallThickness / 2f;

        /// <summary>The highest step. A cat jumping from it still clears the ceiling (see the ceiling collider below).</summary>
        const float CatWallTop = 2.05f;

        static void BuildCatWall(Transform hall, Materials m, Material teak)
        {
            var root = Child(hall, "CatWall", Vector3.zero);
            var pad = Textured("Sea_CatWallPad", (Texture2D)m.Felt.GetTexture("_BaseMap"), 0.05f, 0.5f);
            pad.SetColor("_BaseColor", new Color(0.2f, 0.3f, 0.52f));
            var sisal = Lit("Sea_Sisal", Color.white, 0.05f);
            sisal.SetTexture("_BaseMap", SisalTexture());
            var rope = Lit("Sea_Rope", new Color(0.74f, 0.63f, 0.46f), 0.05f);
            var bed = Lit("Sea_CatBed", new Color(0.82f, 0.5f, 0.46f), 0.1f);
            var pillow = Lit("Sea_CatBedPillow", new Color(0.96f, 0.91f, 0.83f), 0.1f);
            var hole = Lit("Sea_CatCubeHole", new Color(0.05f, 0.04f, 0.04f), 0f);
            EditorUtility.SetDirty(pad);
            EditorUtility.SetDirty(sisal);

            Vector3 Wall(float out_, float y, float z) => new(CatWallFace + out_, y, z);

            void Bracket(float z, float underside, float depth)
            {
                // Brass: a plate on the wall, an arm out under the board and a strut between them.
                NoShadows(Box("Bracket", root, Wall(0.012f, underside - 0.09f, z), new Vector3(0.024f, 0.18f, 0.035f), m.Brass, collider: false));
                NoShadows(Box("BracketArm", root, Wall(depth * 0.4f, underside - 0.012f, z), new Vector3(depth * 0.8f, 0.024f, 0.035f), m.Brass, collider: false));
                NoShadows(Strut("BracketStrut", root, Wall(0.02f, underside - 0.17f, z), Wall(depth * 0.55f, underside - 0.02f, z), new Vector2(0.02f, 0.03f), m.Brass));
            }

            void Shelf(string name, float z0, float z1, float top, float depth)
            {
                var length = z1 - z0;
                var middle = (z0 + z1) / 2f;
                Box(name, root, Wall(depth / 2f, top - 0.02f, middle), new Vector3(depth, 0.04f, length), teak);
                Box("Pad", root, Wall(depth / 2f + 0.005f, top + 0.006f, middle), new Vector3(depth - 0.05f, 0.012f, length - 0.05f), pad, collider: false);
                foreach (var z in length > 0.8f ? new[] { z0 + 0.12f, middle, z1 - 0.12f } : new[] { z0 + 0.1f, z1 - 0.1f })
                    Bracket(z, top - 0.04f, depth);
            }

            void Disc(string name, Vector3 center, float radius)
            {
                Cylinder(name, root, center + Vector3.down * 0.02f, new Vector3(radius * 2f, 0.02f, radius * 2f), teak);
                Cylinder("Pad", root, center + Vector3.up * 0.006f, new Vector3(radius * 2f - 0.05f, 0.006f, radius * 2f - 0.05f), pad, collider: false);
            }

            // ---- The start: a round rug at the foot of a cat tree in the south-west corner.
            var rugCenter = new Vector3(-9.75f, 0f, -5.45f);
            NoShadows(Cylinder("StartRug_Border", root, rugCenter + Vector3.up * 0.002f, new Vector3(1.34f, 0.002f, 1.34f), m.Brass, collider: false));
            NoShadows(Cylinder("StartRug", root, rugCenter + Vector3.up * 0.004f, new Vector3(1.24f, 0.002f, 1.24f), pad, collider: false));

            Box("TreeBase", root, Wall(0.75f, 0.03f, -6.475f), new Vector3(1.5f, 0.06f, 0.75f), pad);
            SisalPost(root, "Post", new Vector2(-9.62f, -6.42f), 0.06f, 0.43f, 0.065f, sisal);
            Disc("Tree_1", new Vector3(-9.62f, 0.45f, -6.42f), 0.3f);
            SisalPost(root, "Post", new Vector2(-10.42f, -6.42f), 0.06f, 0.83f, 0.07f, sisal);
            Disc("Tree_2", new Vector3(-10.42f, 0.85f, -6.42f), 0.32f);
            // A toy on a string under the middle platform.
            NoShadows(Rope("ToyString", root, new Vector3(-10.2f, 0.81f, -6.3f), new Vector3(-10.2f, 0.5f, -6.3f), 0.004f, rope));
            NoShadows(Sphere("Toy", root, new Vector3(-10.2f, 0.47f, -6.3f), 0.07f, m.Yarn[0]));
            // The third platform stands on its own post, clear of the second, so a cat on that one can jump up.
            Cylinder("Tree_3_Foot", root, new Vector3(-10.47f, 0.015f, -5.47f), new Vector3(0.4f, 0.015f, 0.4f), teak);
            SisalPost(root, "Post", new Vector2(-10.47f, -5.47f), 0.03f, 1.21f, 0.075f, sisal);
            Box("Tree_3", root, new Vector3(-10.47f, 1.23f, -5.47f), new Vector3(0.62f, 0.04f, 0.62f), teak);
            Box("Pad", root, new Vector3(-10.47f, 1.256f, -5.47f), new Vector3(0.57f, 0.012f, 0.57f), pad, collider: false);
            // A sisal scratching board against the wainscot beside the tree.
            NoShadows(Box("ScratchBoard", root, Wall(0.065f, 0.45f, -3.95f), new Vector3(0.03f, 0.8f, 0.5f), sisal, collider: false));

            // ---- Up the wall to the rope bridge.
            Shelf("Step_1", -4.85f, -4.1f, 1.6f, 0.45f);
            Shelf("Step_2", -3.85f, -3.4f, 1.95f, 0.62f);

            // The bridge spans the poker table's corner (above its seat cameras) from Step_2 to the cat cube, sagging
            // a little in the middle. Rope rails either side; the outer one also keeps a walking cat on the planks.
            const float bridgeFrom = -3.4f;
            const float bridgeTo = 0f;
            const float bridgeHeight = 1.95f;
            const float sag = 0.12f;
            // Wide enough for a cat (0.5 m) between the wall and the outer rail.
            const float deckIn = 0.03f;
            const float deckOut = 0.58f;
            float DeckY(float z) => bridgeHeight - sag * Mathf.Sin(Mathf.PI * Mathf.InverseLerp(bridgeFrom, bridgeTo, z));
            var bridge = Child(root, "Bridge", Vector3.zero);
            const int planks = 31;
            for (var i = 0; i < planks; i++)
            {
                var z = Mathf.Lerp(bridgeFrom + 0.05f, bridgeTo - 0.05f, i / (planks - 1f));
                var slope = (DeckY(z + 0.05f) - DeckY(z - 0.05f)) / 0.1f;
                var plank = Box("Plank", bridge, Wall((deckIn + deckOut) / 2f, DeckY(z) - 0.0125f, z), new Vector3(deckOut - deckIn, 0.025f, 0.085f), teak);
                plank.transform.localRotation = Quaternion.Euler(-Mathf.Atan(slope) * Mathf.Rad2Deg, 0f, 0f);
            }

            const int ropeSegments = 10;
            foreach (var x in new[] { deckIn - 0.02f, deckOut + 0.02f })
            {
                for (var i = 0; i < ropeSegments; i++)
                {
                    var z0 = Mathf.Lerp(bridgeFrom, bridgeTo, i / (float)ropeSegments);
                    var z1 = Mathf.Lerp(bridgeFrom, bridgeTo, (i + 1f) / ropeSegments);
                    Rope("Rail", bridge, Wall(x, DeckY(z0) + 0.3f, z0), Wall(x, DeckY(z1) + 0.3f, z1), 0.011f, rope);
                    // Hangers down to every few planks.
                    if (i > 0)
                        Rope("Hanger", bridge, Wall(x, DeckY(z0) + 0.3f, z0), Wall(x, DeckY(z0), z0), 0.006f, rope);
                }

                foreach (var z in new[] { bridgeFrom, bridgeTo })
                    Box("BridgePost", bridge, Wall(x, bridgeHeight + 0.15f, z), new Vector3(0.045f, 0.36f, 0.045f), teak, collider: false);
            }

            // On the cat barrier layer, which the camera ignores (it would pull in to the rope whenever it looked past it).
            var rail = Child(bridge, "RailCollider", Wall(deckOut + 0.04f, bridgeHeight - sag + 0.12f, (bridgeFrom + bridgeTo) / 2f));
            rail.gameObject.layer = CatBarrierLayer;
            rail.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.03f, 0.3f + sag * 2f, bridgeTo - bridgeFrom - 0.3f);

            // ---- A cat cube at the bridge's far end, with a way in on the front (painted on: the cube is solid).
            Box("CatCube", root, Wall(0.31f, bridgeHeight - 0.275f, 0.3f), new Vector3(0.62f, 0.55f, 0.6f), teak);
            Box("Pad", root, Wall(0.315f, bridgeHeight + 0.006f, 0.3f), new Vector3(0.57f, 0.012f, 0.55f), pad, collider: false);
            var opening = Cylinder("CubeOpening", root, Wall(0.62f + 0.005f, bridgeHeight - 0.29f, 0.3f), new Vector3(0.32f, 0.001f, 0.32f), hole, collider: false);
            opening.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var rim = Cylinder("CubeOpeningRim", root, Wall(0.62f + 0.002f, bridgeHeight - 0.29f, 0.3f), new Vector3(0.38f, 0.001f, 0.38f), sisal, collider: false);
            rim.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            NoShadows(opening);
            NoShadows(rim);
            Bracket(0.1f, bridgeHeight - 0.55f, 0.62f);
            Bracket(0.5f, bridgeHeight - 0.55f, 0.62f);

            // ---- Down two round stepping pads on brass arms, about as far apart as a walking cat jumps going down.
            var pads = new[] { (1.6f, 1.65f), (2.75f, 1.35f) };
            for (var i = 0; i < pads.Length; i++)
            {
                var (z, top) = pads[i];
                Disc($"SteppingPad_{i + 1}", Wall(0.3f, top, z), 0.25f);
                NoShadows(Box("Arm", root, Wall(0.15f, top - 0.055f, z), new Vector3(0.3f, 0.03f, 0.045f), m.Brass, collider: false));
                NoShadows(Box("WallPlate", root, Wall(0.012f, top - 0.1f, z), new Vector3(0.024f, 0.16f, 0.07f), m.Brass, collider: false));
            }

            // ---- Back up: a run-up shelf, the long jump, and the top.
            Shelf("RunUp", 3.3f, 4.2f, 1.6f, 0.45f);
            Shelf("LongJump", 5.05f, 5.45f, 1.85f, 0.42f);
            // The top stops short of the north window's frame.
            const float topFrom = 5.85f;
            const float topTo = HallDepth / 2f - WallThickness / 2f;
            const float topDepth = 0.52f;
            Shelf("Top", topFrom, topTo, CatWallTop, topDepth);
            var bedCenter = Wall(0.27f, CatWallTop, 6.15f);
            Cylinder("CatBed", root, bedCenter + Vector3.up * 0.035f, new Vector3(0.5f, 0.035f, 0.5f), bed);
            Cylinder("CatBedPillow", root, bedCenter + Vector3.up * 0.082f, new Vector3(0.38f, 0.012f, 0.38f), pillow, collider: false);
            var glass = SeaGlass();
            var glow = Emissive("Sea_LanternGlow", new Color(1f, 0.85f, 0.6f), new Color(1f, 0.7f, 0.4f) * 4f);
            Lantern(root, Wall(0.12f, CatWallTop, topTo - 0.1f), m, glass, glow, light: true);
            // A pennant on a brass pole: you made it.
            var pole = Wall(topDepth - 0.04f, CatWallTop, topTo - 0.06f);
            NoShadows(Cylinder("FlagPole", root, pole + Vector3.up * 0.26f, new Vector3(0.022f, 0.26f, 0.022f), m.Brass, collider: false));
            NoShadows(Box("Flag", root, pole + new Vector3(0f, 0.44f, -0.11f), new Vector3(0.006f, 0.13f, 0.2f), m.Velvet, collider: false));

            // ---- A cushioned window seat under the north-west window, to look out at the sea from (and to drop onto from the top).
            // In front of the wainscot, whose top is level with the cushion's.
            var opening0 = NorthOpenings[0];
            const float panelling = HallDepth / 2f - 0.195f;
            var seatMiddle = new Vector3((opening0.From + opening0.To) / 2f, 0f, panelling - 0.21f);
            Box("WindowSeat", root, seatMiddle + Vector3.up * 0.84f, new Vector3(opening0.To - opening0.From, 0.04f, 0.42f), teak);
            Box("WindowSeatCushion", root, seatMiddle + Vector3.up * 0.88f, new Vector3(opening0.To - opening0.From - 0.06f, 0.04f, 0.38f), pillow);
            foreach (var x in new[] { opening0.From + 0.25f, opening0.To - 0.25f })
            {
                NoShadows(Box("SeatBracket", root, new Vector3(x, 0.73f, panelling - 0.012f), new Vector3(0.035f, 0.18f, 0.024f), m.Brass, collider: false));
                NoShadows(Strut("SeatStrut", root, new Vector3(x, 0.66f, panelling - 0.02f), new Vector3(x, 0.81f, seatMiddle.z - 0.06f), new Vector2(0.03f, 0.02f), m.Brass));
            }

            // ---- Lamps: one over the bridge (the lantern at the top is the other). They stand in for the two
            // sconces the west wall has in the lounge.
            WallLamp(root, Wall(0f, 2.95f, (bridgeFrom + bridgeTo) / 2f), m);

            // The plaster ceiling has no collider: a cat jumping from the top would put its head through it.
            var ceiling = Child(root, "CatCeiling", new Vector3(0f, CeilingHeight + 0.05f, 0f));
            ceiling.gameObject.layer = CatBarrierLayer;
            ceiling.gameObject.AddComponent<BoxCollider>().size = new Vector3(HallWidth, 0.1f, HallDepth);

            // Patrons keep to the floor (the tree's base and the rug carve it, so they walk round).
            NotWalkable(root.gameObject);

            var wall = root.gameObject.AddComponent<CatWall>();
            var serialized = new SerializedObject(wall);
            serialized.FindProperty("_start").boundsValue = new Bounds(new Vector3(-9.85f, 0.05f, -5.75f), new Vector3(2f, 0.5f, 2.2f));
            serialized.FindProperty("_top").boundsValue = new Bounds(Wall(topDepth / 2f, CatWallTop + 0.25f, (topFrom + topTo) / 2f),
                new Vector3(topDepth + 0.1f, 0.6f, topTo - topFrom + 0.1f));
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var hud = CreateDocument("UI_CatWall", "CatWall.uxml", Load<PanelSettings>(PanelSettingsPath), 4);
            SetFields(hud.gameObject.AddComponent<CatWallHud>(), ("_document", hud));
        }

        /// <summary>A brass wall lamp like the hall's sconces, with its small warm light.</summary>
        static void WallLamp(Transform parent, Vector3 position, Materials m)
        {
            var lamp = Child(parent, "WallLamp", position, Quaternion.LookRotation(Vector3.right));
            NoShadows(Box("Plate", lamp, new Vector3(0f, 0f, 0.02f), new Vector3(0.1f, 0.24f, 0.03f), m.Brass, collider: false));
            NoShadows(Box("Arm", lamp, new Vector3(0f, -0.04f, 0.08f), new Vector3(0.025f, 0.025f, 0.14f), m.Brass, collider: false));
            NoShadows(Cylinder("Shade", lamp, new Vector3(0f, 0.03f, 0.15f), new Vector3(0.13f, 0.07f, 0.13f), m.SconceGlow, collider: false));
            var light = PointLight("Light", new Vector3(0f, 0.03f, 0.2f), 3.2f, 1.1f, new Color(1f, 0.72f, 0.46f), lamp);
            light.renderMode = LightRenderMode.Auto;
        }

        /// <summary>A round post wound with sisal rope from <paramref name="bottom"/> to <paramref name="top"/>, solid to cats.</summary>
        static void SisalPost(Transform parent, string name, Vector2 position, float bottom, float top, float radius, Material sisal)
        {
            const int sides = 14;
            var height = top - bottom;
            var meshName = $"Sea_SisalPost_{Mathf.RoundToInt(radius * 1000f)}_{Mathf.RoundToInt(height * 100f)}";
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (var i = 0; i <= sides; i++)
            {
                var angle = i * Mathf.PI * 2f / sides;
                var normal = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                foreach (var y in new[] { 0f, height })
                {
                    vertices.Add(normal * radius + Vector3.up * y);
                    normals.Add(normal);
                    // The texture repeats every half metre up the post, whatever its height.
                    uvs.Add(new Vector2(i / (float)sides, y * 2f));
                }

                if (i == sides)
                    break;
                var a = i * 2;
                triangles.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });
            }

            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            var post = new GameObject(name);
            post.transform.SetParent(parent, false);
            post.transform.localPosition = new Vector3(position.x, bottom, position.y);
            post.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh, meshName);
            post.AddComponent<MeshRenderer>().sharedMaterial = sisal;
            var capsule = post.AddComponent<CapsuleCollider>();
            capsule.radius = radius;
            capsule.height = height;
            capsule.center = Vector3.up * height / 2f;
            GameObjectUtility.SetStaticEditorFlags(post, StaticEditorFlags.BatchingStatic);
        }

        /// <summary>A thin cylinder from one point to another (rope, string). No collider.</summary>
        static GameObject Rope(string name, Transform parent, Vector3 from, Vector3 to, float radius, Material material)
        {
            var rope = Cylinder(name, parent, (from + to) / 2f, new Vector3(radius * 2f, (to - from).magnitude / 2f, radius * 2f), material, collider: false);
            rope.transform.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
            return rope;
        }

        /// <summary>A box from one point to another, <paramref name="section"/> wide and thick. No collider.</summary>
        static GameObject Strut(string name, Transform parent, Vector3 from, Vector3 to, Vector2 section, Material material)
        {
            var strut = Box(name, parent, (from + to) / 2f, new Vector3(section.x, section.y, (to - from).magnitude), material, collider: false);
            strut.transform.localRotation = Quaternion.LookRotation(to - from, Vector3.up);
            return strut;
        }

        static GameObject Sphere(string name, Transform parent, Vector3 position, float diameter, Material material)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.transform.SetParent(parent, false);
            sphere.transform.localPosition = position;
            sphere.transform.localScale = Vector3.one * diameter;
            sphere.GetComponent<MeshRenderer>().sharedMaterial = material;
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            return sphere;
        }

        /// <summary>Sisal rope wound round a post: ridges across V (twelve turns a repeat), each a little twisted and frayed.</summary>
        static Texture2D SisalTexture() => GeneratedTexture("SeaSisal", 128, 256, (u, v) =>
        {
            var turns = v * 12f + u;
            var across = Mathf.Repeat(turns, 1f) * 2f - 1f;
            var round = Mathf.Sqrt(Mathf.Max(0f, 1f - across * across));
            var twist = 0.9f + 0.1f * Mathf.Sin((u * 18f - v * 12f * 4f) * Mathf.PI * 2f);
            var fibre = 0.85f + 0.3f * Fbm(u, v, 32, 2, 13);
            var shade = Mathf.Lerp(0.5f, 1f, round) * twist * fibre;
            return new Color(0.8f * shade, 0.68f * shade, 0.5f * shade);
        });
    }
}
