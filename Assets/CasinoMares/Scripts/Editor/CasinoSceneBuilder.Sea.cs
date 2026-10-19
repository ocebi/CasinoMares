using System.Collections.Generic;
using System.IO;
using CasinoMares.Npc;
using CasinoMares.Props;
using CasinoMares.Social;
using CasinoMares.UI;
using Fusion;
using Unity.AI.Navigation;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace CasinoMares.Editor
{
    /// <summary>
    /// The seaside casino (CasinoSea.unity): the same hall at golden hour, brighter, with windows onto the sea and a
    /// door out to a terrace on the east side. The rest of the builder checks <see cref="_sea"/> where the two differ;
    /// the themed surfaces get their own assets (<see cref="Themed"/>), so the original scene never changes.
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        const string ClassicScenePath = Root + "/Scenes/Casino.unity";
        const string SeaScenePath = Root + "/Scenes/CasinoSea.unity";
        const float WallpaperTile = 1.2f;
        const float WindowSill = 0.95f;
        const float WindowTop = 2.75f;
        /// <summary>The terrace door in the east wall, measured along it (z).</summary>
        const float SeaDoorFrom = 2.2f;
        const float SeaDoorTo = 4.2f;

        /// <summary>Building the seaside scene instead of the original lounge.</summary>
        static bool _sea;

        static Material _stucco;

        /// <summary>Materials whose texture tiles in metres (see <see cref="WorldUvCube"/>), with the tile size.</summary>
        static readonly Dictionary<Material, float> WorldUvTiles = new();

        const float SeaLevel = -3f;
        /// <summary>The top of the stone base, just under the floors (their bottoms are at -0.1), so no faces meet.</summary>
        const float PlinthTop = -0.12f;
        /// <summary>
        /// The terrace deck, east of the hall: from the east wall out to here, and between these two along it. The pool's
        /// extension (CasinoSceneBuilder.Pool.cs) runs it on to <see cref="DeckEast"/>; the sea and islands stay centred on
        /// this part.
        /// </summary>
        const float TerraceEast = 18f;
        const float TerraceSouth = -1.5f;
        const float TerraceNorth = HallDepth / 2f + 0.2f;
        /// <summary>The stucco skin outside the walls (the walls themselves are papered on both faces).</summary>
        const float CladdingThickness = 0.05f;
        const float WallThickness = 0.3f;

        /// <summary>
        /// The sky: a photographed sunset (Poly Haven, CC0), prepared by Tools/prepare_sky.py into a PNG and a JSON
        /// with where its sun is. To try another, prepare it next to this one and change the name; a cloudless one,
        /// syferfontein_1d_clear_puresky, is already there.
        /// </summary>
        const string SkyPhotoPath = "Assets/ThirdParty/PolyHaven/industrial_sunset_02_puresky";
        /// <summary>Scales the photo's light to the scene's: darker than the photo's own exposure, so the colours show.</summary>
        const float SkyBrightness = 0.45f;
        /// <summary>The photo is turned so its sun sets over the sea in the east-north-east.</summary>
        const float SunAzimuth = 65f;
        /// <summary>
        /// The sun light shines from a little higher than the sun in the photo (which is about to touch the sea), so
        /// it still reaches across the terrace and in through the windows.
        /// </summary>
        static readonly Vector3 LightDirection = Quaternion.Euler(-7f, SunAzimuth, 0f) * Vector3.forward;
        static readonly Color SunColor = new(1f, 0.7f, 0.45f);

        /// <summary>What Tools/prepare_sky.py found in the photo; colours are linear at the PNG's exposure.</summary>
        [System.Serializable]
        class SkyPhotoInfo
        {
            public float sunU;
            public float sunElevation;
            public float[] horizonAway;
            public float[] horizonUnderSun;
            public float[] zenith;
            public float[] midSky;

            public Vector3 SunDirection => Quaternion.Euler(-sunElevation, SunAzimuth, 0f) * Vector3.forward;

            /// <summary>A colour from the photo at the scene's brightness, as an (sRGB) Color for materials and settings.</summary>
            public static Color Colour(float[] linear, float scale = 1f) =>
                new Color(linear[0] * scale * SkyBrightness, linear[1] * scale * SkyBrightness, linear[2] * scale * SkyBrightness).gamma;
        }

        static SkyPhotoInfo _skyPhoto;
        /// <summary>A window or door in a seaside wall: from/to along the wall, bottom/top above the floor.</summary>
        readonly struct Opening
        {
            public readonly float From, To, Bottom, Top;

            public Opening(float from, float to, float bottom, float top)
            {
                From = from;
                To = to;
                Bottom = bottom;
                Top = top;
            }

            public bool IsDoor => Bottom <= 0f;
        }

        // Clear of the bar, neon sign and leaderboard (north) and of the slots (east, z below -1.6).
        static readonly Opening[] NorthOpenings = { new(-10.3f, -8.2f, WindowSill, WindowTop), new(7.3f, 10.3f, WindowSill, WindowTop) };
        static readonly Opening[] EastOpenings =
        {
            new(-0.9f, 0.9f, WindowSill, WindowTop), new(SeaDoorFrom, SeaDoorTo, 0f, DoorHeight), new(5.0f, 6.5f, WindowSill, WindowTop),
        };

        [MenuItem("Casino Mares/Setup/Rebuild Sea Casino (Assets and Scene)")]
        public static void RebuildSea()
        {
            _sea = true;
            try
            {
                RebuildAll();
            }
            finally
            {
                _sea = false;
                _stucco = null;
                _skyPhoto = null;
                WorldUvTiles.Clear();
            }
        }

        [MenuItem("Casino Mares/Setup/Default Scene/Classic")]
        static void DefaultClassic() => SetDefaultScene(ClassicScenePath);

        [MenuItem("Casino Mares/Setup/Default Scene/Sea")]
        static void DefaultSea() => SetDefaultScene(SeaScenePath);

        [MenuItem("Casino Mares/Setup/Default Scene/Classic", true)]
        static bool DefaultClassicValidate() => ValidateDefaultScene("Classic", ClassicScenePath);

        [MenuItem("Casino Mares/Setup/Default Scene/Sea", true)]
        static bool DefaultSeaValidate() => ValidateDefaultScene("Sea", SeaScenePath);

        static bool ValidateDefaultScene(string item, string path)
        {
            var scenes = EditorBuildSettings.scenes;
            Menu.SetChecked($"Casino Mares/Setup/Default Scene/{item}", scenes.Length > 0 && scenes[0].path == path);
            return File.Exists(path);
        }

        /// <summary>The game opens the first scene in the build; the other is a <c>?scene=</c> away.</summary>
        static void SetDefaultScene(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(scene => scene.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Both casinos go in the build (whichever exist), keeping whichever was first as the default.</summary>
        static void UpdateBuildScenes()
        {
            var current = EditorBuildSettings.scenes;
            var paths = new List<string>();
            if (current.Length > 0 && File.Exists(current[0].path))
                paths.Add(current[0].path);
            foreach (var path in new[] { ClassicScenePath, SeaScenePath })
            {
                if (!paths.Contains(path) && File.Exists(path))
                    paths.Add(path);
            }

            EditorBuildSettings.scenes = paths.ConvertAll(path => new EditorBuildSettingsScene(path, true)).ToArray();
        }

        static string Themed(string name) => _sea ? "Sea_" + name : name;

        // ---------------------------------------------------------------- Look

        /// <summary>
        /// Golden hour: the sunset photo for a sky, a low warm sun light, ambient light and a thin haze taken from the
        /// photo's colours.
        /// </summary>
        static void BuildSeaLighting()
        {
            _skyPhoto = JsonUtility.FromJson<SkyPhotoInfo>(File.ReadAllText(SkyPhotoPath + ".json"));

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.6f, 0.36f);
            sun.intensity = 1.7f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.transform.rotation = Quaternion.LookRotation(-LightDirection);
            RenderSettings.sun = sun;

            var sky = MaterialAsset("Sea_SunsetSky", "CasinoMares/SunsetSkybox");
            SetSkyParameters(sky);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            // The sky overhead from above, the horizon from the sides (warmed: the sunset is behind the walls), and
            // light off the bright floor and the sea from below, which keeps the plaster ceiling from going dark.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(SkyPhotoInfo.Colour(_skyPhoto.zenith, 1.6f), new Color(0.62f, 0.56f, 0.66f), 0.5f);
            RenderSettings.ambientEquatorColor = Color.Lerp(SkyPhotoInfo.Colour(_skyPhoto.horizonAway, 1.6f), new Color(0.6f, 0.48f, 0.44f), 0.6f);
            RenderSettings.ambientGroundColor = new Color(0.38f, 0.31f, 0.27f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0012f;
            RenderSettings.fogColor = SkyPhotoInfo.Colour(_skyPhoto.horizonAway);
        }

        /// <summary>
        /// The sky and the sea both draw the sky (SunsetSky.hlsl), so the sea's haze meets it at the horizon: the photo,
        /// turned to put its sun in the east-north-east, and a halo and disk where that sun is.
        /// </summary>
        static void SetSkyParameters(Material material)
        {
            var path = SkyPhotoPath + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.wrapModeV != TextureWrapMode.Clamp || importer.maxTextureSize != 4096)
            {
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 4096;
                importer.SaveAndReimport();
            }

            material.SetTexture("_SkyTex", Load<Texture2D>(path));
            material.SetVector("_SkyParams", new Vector4(_skyPhoto.sunU - SunAzimuth / 360f, SkyBrightness, 1.5f, 0f));
            material.SetColor("_SunColor", SunColor);
            material.SetVector("_SunDirection", _skyPhoto.SunDirection);
            material.SetVector("_SunGlow", new Vector4(0.6f, 400f, 12f, 0.012f));
        }

        /// <summary>
        /// The seaside room is lit for real, so it needs less exposure than the dim lounge; the sunset wants more
        /// colour and contrast, violet shade against orange light, and a bloom that makes the sun blaze.
        /// </summary>
        static void TuneSeaVolume(ColorAdjustments color, SplitToning splitToning, Vignette vignette, Bloom bloom)
        {
            color.postExposure.Override(0.35f);
            color.contrast.Override(16f);
            color.saturation.Override(18f);
            splitToning.shadows.Override(new Color(0.4f, 0.44f, 0.6f));
            splitToning.highlights.Override(new Color(0.68f, 0.52f, 0.36f));
            vignette.intensity.Override(0.2f);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.72f);
        }

        /// <summary>Seaside carpet: deep sea blue with a cream-gold lattice (the lounge carpet's pattern).</summary>
        static Texture2D SeaCarpetTexture() => GeneratedTexture("SeaCarpet", 512, 512, (u, v) =>
        {
            var a = Mathf.Repeat((u + v) * 4f, 1f);
            var b = Mathf.Repeat((u - v) * 4f, 1f);
            var line = Mathf.Min(Mathf.Min(a, 1f - a), Mathf.Min(b, 1f - b));
            var lattice = 1f - Edge(0.012f, 0.026f, line);
            var cell = Mathf.Abs(a - 0.5f) + Mathf.Abs(b - 0.5f);
            var medallion = 1f - Edge(0.08f, 0.11f, cell);
            var ring = 1f - Edge(0.01f, 0.02f, Mathf.Abs(cell - 0.22f));

            var color = Color.Lerp(new Color(0.15f, 0.29f, 0.41f), new Color(0.2f, 0.36f, 0.48f), Fbm(u, v, 8, 2, 3));
            var gold = new Color(0.78f, 0.62f, 0.36f);
            color = Color.Lerp(color, gold, lattice * 0.8f);
            color = Color.Lerp(color, gold * 0.9f, medallion * 0.75f);
            color = Color.Lerp(color, new Color(0.09f, 0.18f, 0.27f), ring * 0.6f);
            return color * (0.85f + 0.25f * Fbm(u, v, 64, 3, 11));
        });

        /// <summary>Seaside wallpaper: ivory and sand stripes with gold pinstripes.</summary>
        static Texture2D SeaWallpaperTexture() => GeneratedTexture("SeaWallpaper", 512, 512, (u, v) =>
        {
            var stripe = Mathf.Repeat(u * 2f, 1f);
            var sand = new Color(0.78f, 0.68f, 0.55f);
            var ivory = new Color(0.86f, 0.79f, 0.67f);
            var color = stripe < 0.5f ? sand : ivory;
            var edge = Mathf.Min(Mathf.Abs(stripe - 0.5f), Mathf.Min(stripe, 1f - stripe));
            color = Color.Lerp(color, new Color(0.7f, 0.55f, 0.3f), 1f - Edge(0.005f, 0.011f, edge));
            return color * (0.93f + 0.1f * Fbm(u, v, 16, 2, 5));
        });

        // ---------------------------------------------------------------- Walls with windows

        static void BuildSeaWalls(Transform hall, Materials m)
        {
            var stucco = Stucco();
            WallWithOpenings("Wall_North", hall, true, HallDepth / 2f, -HallWidth / 2f, HallWidth / 2f, NorthOpenings, m.Wall, stucco);
            WallWithOpenings("Wall_East", hall, false, HallWidth / 2f, -HallDepth / 2f, HallDepth / 2f, EastOpenings, m.WallShort, stucco);
        }

        /// <summary>
        /// A wall along x (<paramref name="alongX"/>) or z at <paramref name="line"/>, from/to along it, with window and
        /// door openings cut out. Outside is the + side of the line (these are the north and east walls), where the
        /// wall wears stucco instead of wallpaper. Windows get a frame, glazing bars, a sill and glass cats can't get
        /// through.
        /// </summary>
        static void WallWithOpenings(string name, Transform parent, bool alongX, float line, float from, float to, Opening[] openings, Material material, Material outside)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);

            Vector3 At(float along, float y, float across) => alongX ? new Vector3(along, y, line + across) : new Vector3(line + across, y, along);
            Vector3 Size(float along, float y, float across) => alongX ? new Vector3(along, y, across) : new Vector3(across, y, along);

            void Piece(string piece, float a, float b, float y0, float y1)
            {
                if (b - a <= 0.01f || y1 - y0 <= 0.01f)
                    return;
                Box(piece, root, At((a + b) / 2f, (y0 + y1) / 2f, 0f), Size(b - a, y1 - y0, WallThickness), material);
                // The stucco runs down past the floor's edge to the stone base.
                var bottom = y0 <= 0f ? PlinthTop : y0;
                var across = (WallThickness + CladdingThickness) / 2f;
                NoShadows(Box("Outside", root, At((a + b) / 2f, (bottom + y1) / 2f, across), Size(b - a, y1 - bottom, CladdingThickness), outside, collider: false));
            }

            var frame = SeaTrim();
            var glass = SeaGlass();
            // Frames stand a little proud of the wallpaper inside and of the stucco outside.
            var frameDepth = WallThickness + CladdingThickness + 0.03f;
            var frameAcross = CladdingThickness / 2f;
            var cursor = from;
            foreach (var opening in openings)
            {
                Piece("Wall", cursor, opening.From, 0f, CeilingHeight);
                Piece("Below", opening.From, opening.To, 0f, opening.Bottom);
                Piece("Above", opening.From, opening.To, opening.Top, CeilingHeight);
                cursor = opening.To;

                var width = opening.To - opening.From;
                var middle = (opening.From + opening.To) / 2f;
                var height = opening.Top - opening.Bottom;
                var centerY = (opening.Bottom + opening.Top) / 2f;
                Box("Jamb", root, At(opening.From + 0.04f, centerY, frameAcross), Size(0.08f, height, frameDepth), frame, collider: false);
                Box("Jamb", root, At(opening.To - 0.04f, centerY, frameAcross), Size(0.08f, height, frameDepth), frame, collider: false);
                Box("Head", root, At(middle, opening.Top - 0.04f, frameAcross), Size(width, 0.08f, frameDepth), frame, collider: false);
                if (opening.IsDoor)
                    continue;

                Box("Sill", root, At(middle, opening.Bottom + 0.03f, frameAcross), Size(width + 0.16f, 0.06f, frameDepth + 0.2f), frame);
                Box("Mullion", root, At(middle, centerY, 0f), Size(0.05f, height, 0.08f), frame, collider: false);
                Box("Transom", root, At(middle, opening.Bottom + height * 0.68f, 0f), Size(width, 0.05f, 0.08f), frame, collider: false);
                // The glass lets the low sun through (no shadow) but stops cats and the camera.
                NoShadows(Box("Glass", root, At(middle, centerY, 0f), Size(width, height, 0.02f), glass));
            }

            Piece("Wall", cursor, to, 0f, CeilingHeight);
        }

        static Material SeaGlass() => TransparentLit("Sea_WindowGlass", new Color(0.8f, 0.9f, 0.95f, 0.12f), 0.95f);

        /// <summary>White-painted wood: window frames, the cornice and the railing posts.</summary>
        static Material SeaTrim() => Lit("Sea_WindowFrame", new Color(0.93f, 0.9f, 0.84f), 0.4f);

        /// <summary>Warm white stucco for the outside of the building, tiled in metres (made once per rebuild).</summary>
        static Material Stucco()
        {
            if (_stucco != null)
                return _stucco;
            var texture = GeneratedTexture("SeaStucco", 256, 256, (u, v) =>
            {
                var value = 0.9f + 0.1f * Fbm(u, v, 16, 4, 17);
                return new Color(0.93f * value, 0.88f * value, 0.79f * value);
            });
            return _stucco = Textured("Sea_Stucco", texture, 0.15f, 2f);
        }

        /// <summary>A Lit material whose texture tiles every <paramref name="tile"/> metres on builder boxes (WorldUvCube).</summary>
        static Material Textured(string name, Texture2D texture, float smoothness, float tile)
        {
            var material = Lit(name, Color.white, smoothness);
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", Vector2.one);
            EditorUtility.SetDirty(material);
            WorldUvTiles[material] = tile;
            return material;
        }

        /// <summary>
        /// A cube mesh whose UVs run in tiles of <paramref name="tile"/> metres at this <paramref name="size"/>, so a
        /// short wall piece shows the same stripes as a long one (the material tiling stays 1).
        /// </summary>
        static Mesh WorldUvCube(Vector3 size, float tile)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            void Face(Vector3 normal, Vector3 right, Vector3 up, float width, float height)
            {
                var start = vertices.Count;
                var center = normal * 0.5f;
                vertices.Add(center - right * 0.5f - up * 0.5f);
                vertices.Add(center + right * 0.5f - up * 0.5f);
                vertices.Add(center + right * 0.5f + up * 0.5f);
                vertices.Add(center - right * 0.5f + up * 0.5f);
                for (var i = 0; i < 4; i++)
                    normals.Add(normal);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(width / tile, 0f));
                uvs.Add(new Vector2(width / tile, height / tile));
                uvs.Add(new Vector2(0f, height / tile));
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }

            Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y);
            Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
            Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
            Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y);
            Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
            Face(Vector3.down, Vector3.right, Vector3.back, size.x, size.z);

            var mesh = new Mesh { name = $"WallBox {size.x:0.##}x{size.y:0.##}x{size.z:0.##}" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------------------------------------------------------- Outside

        /// <summary>
        /// Everything outside the seaside hall: the stone base it stands on, the sea out to the horizon with rocks and
        /// islands, the building's outside, and the terrace. Every camera sees out to the horizon.
        /// </summary>
        static void BuildSeaside(Transform hall, Materials m)
        {
            var outside = new GameObject("Seaside").transform;
            var stone = Textured("Sea_Stone", StoneTexture(), 0.2f, 2f);
            BuildPlinth(outside, stone);
            BuildSea(outside);
            BuildRocks(outside);
            BuildIslands(outside);
            BuildSailBoats(outside);
            BuildExterior(outside);
            var terrace = BuildTerraceDeck(outside, stone, out var teak);
            BuildTerraceFurniture(terrace, m, teak);
            BuildPool(terrace, stone, teak);
            BuildBarSeats(hall);
            BuildCatWall(hall, m, teak);

            var hud = CreateDocument("UI_Terrace", "Terrace.uxml", Load<PanelSettings>(PanelSettingsPath), 6);
            SetFields(hud.gameObject.AddComponent<TerraceHud>(), ("_document", hud));

            foreach (var camera in Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
            {
                var lens = camera.Lens;
                lens.FarClipPlane = FarClip;
                camera.Lens = lens;
            }

            if (Camera.main != null)
                Camera.main.farClipPlane = FarClip;

            // Outside the hall's probe: the terrace furniture reflects the sea, the sky and the facade.
            BakeReflectionProbe("TerraceReflectionProbe", new Vector3((HallWidth / 2f + DeckEast) / 2f, 1.2f, (TerraceSouth + TerraceNorth) / 2f),
                new Vector3(DeckEast - HallWidth / 2f + 0.6f, 4f, TerraceNorth - TerraceSouth + 0.6f), new Vector3(0f, 0.8f, 0f), false, 128,
                Root + "/Scenes/CasinoSea_TerraceReflection.exr");
        }

        /// <summary>The stone base under the hall and the terrace, from below the sea up to just under the floors.</summary>
        static void BuildPlinth(Transform parent, Material stone)
        {
            const float bottom = SeaLevel - 1.5f;
            void Block(string name, float x0, float x1, float z0, float z1) =>
                Box(name, parent, new Vector3((x0 + x1) / 2f, (PlinthTop + bottom) / 2f, (z0 + z1) / 2f), new Vector3(x1 - x0, PlinthTop - bottom, z1 - z0), stone, collider: false);

            foreach (var (name, x0, x1, z0, z1) in PlinthBlocks)
            {
                if (name != "Plinth_Terrace")
                {
                    Block(name, x0, x1, z0, z1);
                    continue;
                }

                // The pool's basin goes down through the terrace's base (CasinoSceneBuilder.Pool.cs).
                foreach (var (part, px0, px1, pz0, pz1, top) in PlinthAroundPool(x0, x1, z0, z1))
                    Box(name + part, parent, new Vector3((px0 + px1) / 2f, (top + bottom) / 2f, (pz0 + pz1) / 2f), new Vector3(px1 - px0, top - bottom, pz1 - pz0), stone, collider: false);
            }
        }

        /// <summary>The hall's base (with room for the entrance vestibule on the south side) and the terrace's.</summary>
        static readonly (string name, float x0, float x1, float z0, float z1)[] PlinthBlocks =
        {
            ("Plinth_Hall", -HallWidth / 2f - 0.7f, HallWidth / 2f + 0.7f, -HallDepth / 2f - 3.5f, TerraceNorth + 0.4f),
            ("Plinth_Terrace", HallWidth / 2f + 0.7f, DeckEast + 0.4f, TerraceSouth - 0.4f, TerraceNorth + 0.4f),
        };

        /// <summary>Sandstone blocks: four staggered courses per tile with sunken mortar.</summary>
        static Texture2D StoneTexture() => GeneratedTexture("SeaStone", 512, 512, (u, v) =>
        {
            var row = Mathf.FloorToInt(v * 4f);
            var x = u * 2f + (row % 2) * 0.5f;
            var column = Mathf.FloorToInt(x);
            var across = x - column;
            var up = v * 4f - row;
            var joint = Mathf.Min(Mathf.Min(across, 1f - across) * 0.5f, Mathf.Min(up, 1f - up) * 0.25f);
            var mortar = 1f - Edge(0.004f, 0.009f, joint);
            var tone = 0.85f + 0.25f * Hash(column % 2, row, 5);
            var color = new Color(0.74f, 0.63f, 0.5f) * (tone * (0.86f + 0.22f * Fbm(u, v, 32, 3, 13)));
            return Color.Lerp(color, new Color(0.45f, 0.4f, 0.35f), mortar);
        });

        // ---------------------------------------------------------------- Sea

        const float SeaRadius = FarClip - 100f;
        /// <summary>The sea mesh is centred on the terrace, where it's finest and the swell is strongest.</summary>
        static readonly Vector2 SeaCenter = new((HallWidth / 2f + TerraceEast) / 2f, (TerraceSouth + TerraceNorth) / 2f);

        static void BuildSea(Transform parent)
        {
            var water = MaterialAsset("Sea_Water", "CasinoMares/Water");
            water.SetTexture("_NormalMap", WaveTexture());
            water.SetFloat("_HazeDensity", RenderSettings.fogDensity);
            // The swell dies away between these distances from the terrace, before the rings get too coarse for it.
            water.SetVector("_SwellCenter", new Vector4(SeaCenter.x, SeaCenter.y, 90f, 180f));
            SetSkyParameters(water);
            EditorUtility.SetDirty(water);

            var sea = new GameObject("Sea");
            sea.transform.SetParent(parent, false);
            sea.transform.localPosition = new Vector3(SeaCenter.x, SeaLevel, SeaCenter.y);
            sea.AddComponent<MeshFilter>().sharedMesh = SaveMesh(SeaMesh(), "Sea_Water");
            var renderer = sea.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = water;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        /// <summary>
        /// A flat disc out to just inside the far clip plane, its rings 0.6 m apart near the middle (fine enough for
        /// the swell, which moves the vertices) and growing 5% each further out.
        /// </summary>
        static Mesh SeaMesh()
        {
            const int segments = 160;
            var radii = new List<float>();
            for (var r = 0.6f; r < SeaRadius; r += Mathf.Max(0.6f, r * 0.05f))
                radii.Add(r);
            radii.Add(SeaRadius);

            var vertices = new List<Vector3> { Vector3.zero };
            var triangles = new List<int>();
            foreach (var radius in radii)
            {
                for (var i = 0; i < segments; i++)
                {
                    var angle = i * Mathf.PI * 2f / segments;
                    vertices.Add(new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
                }
            }

            int At(int ring, int i) => 1 + ring * segments + i % segments;
            for (var i = 0; i < segments; i++)
            {
                // Clockwise seen from above, so the triangles face up.
                triangles.AddRange(new[] { 0, At(0, i + 1), At(0, i) });
                for (var ring = 0; ring < radii.Count - 1; ring++)
                {
                    triangles.AddRange(new[] { At(ring, i), At(ring, i + 1), At(ring + 1, i + 1) });
                    triangles.AddRange(new[] { At(ring, i), At(ring + 1, i + 1), At(ring + 1, i) });
                }
            }

            var mesh = new Mesh { name = "Sea" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(vertices.ConvertAll(_ => Vector3.up));
            mesh.SetTriangles(triangles, 0);
            // The swell moves vertices up and down a little; keep them from being culled.
            mesh.RecalculateBounds();
            var bounds = mesh.bounds;
            bounds.Expand(new Vector3(0f, 2f, 0f));
            mesh.bounds = bounds;
            return mesh;
        }

        /// <summary>
        /// Tileable ripple normals (imported as a normal map): a dozen wave trains running mostly one way, with sharp
        /// crests and flat troughs, over a little noise. Every wave fits a whole number of times across the tile.
        /// </summary>
        static Texture2D WaveTexture()
        {
            const int size = 256;
            const float step = 1f / size;
            var waves = new (int x, int y, float amplitude, float phase)[]
            {
                (3, 1, 1f, 0.1f), (4, -1, 0.8f, 1.7f), (5, 2, 0.6f, 2.9f), (6, -2, 0.5f, 4.1f), (2, 3, 0.45f, 0.6f),
                (8, 1, 0.35f, 5.3f), (9, -3, 0.3f, 3.3f), (11, 2, 0.22f, 1.1f), (7, 5, 0.2f, 2.2f), (13, -1, 0.15f, 4.8f),
                (15, 4, 0.1f, 0.9f), (17, -5, 0.08f, 3.7f),
            };

            float Height(float u, float v)
            {
                var height = 0f;
                foreach (var (x, y, amplitude, phase) in waves)
                {
                    var wave = Mathf.Sin((x * u + y * v) * Mathf.PI * 2f + phase) * 0.5f + 0.5f;
                    height += amplitude * wave * wave;
                }

                return height * 0.25f + Fbm(u, v, 16, 3, 43) * 0.12f;
            }

            return GeneratedTexture("SeaWaves", size, size, (u, v) =>
            {
                var dx = (Height(u + step, v) - Height(u - step, v)) / (2f * step);
                var dy = (Height(u, v + step) - Height(u, v - step)) / (2f * step);
                var normal = new Vector3(-dx * 0.05f, -dy * 0.05f, 1f).normalized;
                return new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f);
            }, configure: importer => importer.textureType = TextureImporterType.NormalMap);
        }

        static Mesh SaveMesh(Mesh mesh, string name)
        {
            var path = $"{Root}/Art/Meshes/{name}.asset";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ---------------------------------------------------------------- Rocks and islands

        /// <summary>Rocks piled along the foot of the base (the islands are in CasinoSceneBuilder.Islands.cs).</summary>
        static void BuildRocks(Transform parent)
        {
            var rockMaterial = Textured("Sea_Rock", GeneratedTexture("SeaRock", 256, 256, (u, v) =>
            {
                var value = 0.62f + 0.45f * Fbm(u, v, 8, 4, 51);
                return new Color(0.5f * value, 0.46f * value, 0.42f * value);
            }), 0.25f, 1f);
            var meshes = new Mesh[4];
            for (var i = 0; i < meshes.Length; i++)
                meshes[i] = SaveMesh(RockMesh($"Rock {i}", 100 + i * 17, 2, 0.45f), $"Sea_Rock_{i}");

            var rocks = new GameObject("Rocks").transform;
            rocks.SetParent(parent, false);
            var random = new System.Random(5);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

            // The base's outline, corner to corner; rocks lie just outside each side.
            var west = PlinthBlocks[0].x0;
            var south = PlinthBlocks[0].z0;
            var north = PlinthBlocks[0].z1;
            var step = PlinthBlocks[0].x1;
            var east = PlinthBlocks[1].x1;
            var terraceSouth = PlinthBlocks[1].z0;
            var outline = new[]
            {
                new Vector2(west, south), new Vector2(west, north), new Vector2(east, north), new Vector2(east, terraceSouth),
                new Vector2(step, terraceSouth), new Vector2(step, south),
            };
            var center = new Vector2((west + east) / 2f, (south + north) / 2f);
            for (var side = 0; side < outline.Length; side++)
            {
                var a = outline[side];
                var b = outline[(side + 1) % outline.Length];
                var along = (b - a).normalized;
                var normal = new Vector2(along.y, -along.x);
                if (Vector2.Dot(normal, (a + b) / 2f - center) < 0f)
                    normal = -normal;
                for (var d = Range(0f, 1.2f); d < (b - a).magnitude; d += Range(1.4f, 2.4f))
                {
                    var spot = a + along * d + normal * Range(0.2f, 1.1f);
                    var rock = new GameObject("Rock");
                    rock.transform.SetParent(rocks, false);
                    rock.transform.SetLocalPositionAndRotation(new Vector3(spot.x, SeaLevel + Range(-0.6f, 0.2f), spot.y), Quaternion.Euler(0f, Range(0f, 360f), 0f));
                    rock.transform.localScale = new Vector3(Range(1.2f, 2.6f), Range(0.9f, 2f), Range(1.2f, 2.6f));
                    rock.AddComponent<MeshFilter>().sharedMesh = meshes[random.Next(meshes.Length)];
                    rock.AddComponent<MeshRenderer>().sharedMaterial = rockMaterial;
                    GameObjectUtility.SetStaticEditorFlags(rock, StaticEditorFlags.BatchingStatic);
                }
            }
        }

        /// <summary>
        /// A lumpy rock: an icosphere (subdivided <paramref name="subdivisions"/> times) pushed in and out by 3D
        /// noise and squashed, so it sits low and wide.
        /// </summary>
        static Mesh RockMesh(string name, int seed, int subdivisions, float roughness)
        {
            var t = (1f + Mathf.Sqrt(5f)) / 2f;
            var vertices = new List<Vector3>
            {
                new(-1f, t, 0f), new(1f, t, 0f), new(-1f, -t, 0f), new(1f, -t, 0f), new(0f, -1f, t), new(0f, 1f, t),
                new(0f, -1f, -t), new(0f, 1f, -t), new(t, 0f, -1f), new(t, 0f, 1f), new(-t, 0f, -1f), new(-t, 0f, 1f),
            };
            var triangles = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (var i = 0; i < vertices.Count; i++)
                vertices[i] = vertices[i].normalized;

            for (var level = 0; level < subdivisions; level++)
            {
                var midpoints = new Dictionary<long, int>();
                int Midpoint(int a, int b)
                {
                    var key = ((long)Mathf.Min(a, b) << 32) | (long)Mathf.Max(a, b);
                    if (!midpoints.TryGetValue(key, out var index))
                    {
                        index = vertices.Count;
                        vertices.Add(((vertices[a] + vertices[b]) / 2f).normalized);
                        midpoints[key] = index;
                    }

                    return index;
                }

                var finer = new List<int>();
                for (var i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    int ab = Midpoint(a, b), bc = Midpoint(b, c), ca = Midpoint(c, a);
                    finer.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }

                triangles = finer;
            }

            var uvs = new List<Vector2>();
            for (var i = 0; i < vertices.Count; i++)
            {
                var direction = vertices[i];
                var radius = 1f + roughness * (Fbm3(direction * 1.6f, seed) - 0.5f) * 2f;
                var point = direction * radius;
                point.y *= point.y > 0f ? 0.75f : 0.6f;
                vertices[i] = point;
                uvs.Add(new Vector2(point.x + point.z * 0.5f, point.y + point.z * 0.5f) * 0.5f);
            }

            // Face every triangle outward (Unity's front faces are clockwise).
            for (var i = 0; i < triangles.Count; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f)
                    (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        static float Noise3(Vector3 p, int seed)
        {
            var x0 = Mathf.FloorToInt(p.x);
            var y0 = Mathf.FloorToInt(p.y);
            var z0 = Mathf.FloorToInt(p.z);
            static float Smooth(float f) => f * f * (3f - 2f * f);
            var fx = Smooth(p.x - x0);
            var fy = Smooth(p.y - y0);
            var fz = Smooth(p.z - z0);
            float Corner(int dx, int dy, int dz) => Hash(x0 + dx, y0 + dy, seed + (z0 + dz) * 7919);
            var near = Mathf.Lerp(Mathf.Lerp(Corner(0, 0, 0), Corner(1, 0, 0), fx), Mathf.Lerp(Corner(0, 1, 0), Corner(1, 1, 0), fx), fy);
            var far = Mathf.Lerp(Mathf.Lerp(Corner(0, 0, 1), Corner(1, 0, 1), fx), Mathf.Lerp(Corner(0, 1, 1), Corner(1, 1, 1), fx), fy);
            return Mathf.Lerp(near, far, fz);
        }

        static float Fbm3(Vector3 p, int seed)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (var octave = 0; octave < 4; octave++)
            {
                sum += Noise3(p, seed + octave) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                p *= 2.03f;
            }

            return sum / total;
        }

        // ---------------------------------------------------------------- Sailboats

        /// <summary>
        /// Loops the boats sail round, all east of the terrace, at least ~110 m out (past the swell, which they'd
        /// otherwise sit in) and clear of the islands: centre (x, z), radii, seconds per lap, start phase, direction,
        /// size, hull and sail.
        /// </summary>
        static readonly (Vector2 center, Vector2 radii, float lap, float phase, bool clockwise, float scale, bool navy, bool redSail)[] Boats =
        {
            (new Vector2(160f, 40f), new Vector2(35f, 105f), 300f, 0f, false, 1f, false, false),
            (new Vector2(270f, 160f), new Vector2(70f, 120f), 420f, 0.3f, true, 1.2f, true, false),
            (new Vector2(160f, 330f), new Vector2(50f, 90f), 330f, 0.55f, false, 0.9f, false, true),
            (new Vector2(450f, -20f), new Vector2(90f, 200f), 640f, 0.15f, true, 1.5f, false, false),
            (new Vector2(70f, -140f), new Vector2(22f, 40f), 200f, 0.8f, false, 0.85f, true, false),
        };

        /// <summary>Sailboats going round long loops out at sea (SailBoat moves them), each with a foam wake.</summary>
        static void BuildSailBoats(Transform parent)
        {
            var root = new GameObject("SailBoats").transform;
            root.SetParent(parent, false);
            var white = Lit("Sea_BoatHull", new Color(0.92f, 0.92f, 0.9f), 0.55f);
            var navy = Lit("Sea_BoatHullNavy", new Color(0.1f, 0.16f, 0.3f), 0.55f);
            var deck = Lit("Sea_BoatDeck", new Color(0.56f, 0.41f, 0.27f), 0.3f);
            var sail = Lit("Sea_Sail", new Color(0.96f, 0.93f, 0.86f), 0.15f);
            var redSail = Lit("Sea_SailRed", new Color(0.8f, 0.36f, 0.22f), 0.15f);
            var wake = SoftAdditive("Sea_Wake", WakeTexture(), new Color(0.32f, 0.3f, 0.28f));
            var mesh = SaveMesh(SailBoatMesh(), "Sea_SailBoat");
            var wakeMesh = SaveMesh(WakeMesh(), "Sea_Wake");

            for (var i = 0; i < Boats.Length; i++)
            {
                var (center, radii, lap, phase, clockwise, scale, navyHull, red) = Boats[i];
                var boat = new GameObject($"SailBoat_{i + 1}");
                boat.transform.SetParent(root, false);
                boat.transform.localScale = Vector3.one * scale;
                boat.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = boat.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { navyHull ? navy : white, deck, red ? redSail : sail };
                renderer.shadowCastingMode = ShadowCastingMode.Off;

                var trail = new GameObject("Wake");
                trail.transform.SetParent(root, false);
                trail.transform.localScale = Vector3.one * scale;
                trail.AddComponent<MeshFilter>().sharedMesh = wakeMesh;
                var trailRenderer = trail.AddComponent<MeshRenderer>();
                trailRenderer.sharedMaterial = wake;
                trailRenderer.shadowCastingMode = ShadowCastingMode.Off;

                var sailBoat = boat.AddComponent<SailBoat>();
                var serialized = new SerializedObject(sailBoat);
                serialized.FindProperty("_center").vector3Value = new Vector3(center.x, SeaLevel, center.y);
                serialized.FindProperty("_radii").vector2Value = radii;
                serialized.FindProperty("_lapSeconds").floatValue = lap;
                serialized.FindProperty("_phase").floatValue = phase;
                serialized.FindProperty("_clockwise").boolValue = clockwise;
                serialized.FindProperty("_wake").objectReferenceValue = trail.transform;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                // Out on its loop in the editor too (it only moves itself while the game runs).
                sailBoat.Place();
            }
        }

        /// <summary>
        /// A 9 m sloop along +z, waterline at y 0: a hull lofted from cross-sections (sheer rising to the bow), a deck,
        /// a cabin, the mast and boom, and a billowing mainsail and jib let out to port. Submeshes: hull, deck, sails.
        /// </summary>
        static Mesh SailBoatMesh()
        {
            const float length = 9f;
            const float beam = 1.5f;
            const float draft = 0.9f;
            const int stations = 14;
            var parts = new MeshParts(3);

            // Cross-sections from the stern (t 0) to the bow (t 1): deck edge, bilge, keel, bilge, deck edge.
            var sections = new Vector3[stations + 1][];
            for (var i = 0; i <= stations; i++)
            {
                var t = i / (float)stations;
                // cos(pi/2) comes out a hair below zero at the bow, and a fractional power of that is NaN.
                var width = t < 0.55f ? beam * (0.78f + 0.22f * t / 0.55f) : beam * Mathf.Pow(Mathf.Max(0f, Mathf.Cos((t - 0.55f) / 0.45f * Mathf.PI / 2f)), 0.7f);
                var depth = draft * (0.55f + 0.45f * Mathf.Sin(Mathf.PI * Mathf.Min(t * 1.1f, 1f)));
                var deckHeight = 0.55f + 0.3f * t * t;
                var z = (t - 0.5f) * length;
                sections[i] = new[]
                {
                    new Vector3(-width, deckHeight, z), new Vector3(-width * 0.82f, -depth * 0.55f, z), new Vector3(0f, -depth, z),
                    new Vector3(width * 0.82f, -depth * 0.55f, z), new Vector3(width, deckHeight, z),
                };
            }

            for (var i = 0; i < stations; i++)
            {
                for (var j = 0; j < 4; j++)
                {
                    var a = sections[i][j];
                    var b = sections[i][j + 1];
                    var c = sections[i + 1][j + 1];
                    var d = sections[i + 1][j];
                    var middle = (a + b + c + d) / 4f;
                    parts.Quad(0, a, b, c, d, middle - new Vector3(0f, 0.2f, middle.z));
                }

                parts.Quad(1, sections[i][0], sections[i][4], sections[i + 1][4], sections[i + 1][0], Vector3.up);
            }

            // The transom closes the stern.
            var stern = sections[0];
            parts.Quad(0, stern[0], stern[1], stern[3], stern[4], Vector3.back);
            parts.Triangle(0, stern[1], stern[2], stern[3], Vector3.back);

            const float deckY = 0.6f;
            parts.Box(0, new Vector3(0f, deckY + 0.3f, -0.6f), new Vector3(1.5f, 0.6f, 2.6f), Quaternion.identity);
            var mastFoot = new Vector3(0f, deckY, 0.9f);
            parts.Cylinder(0, mastFoot, mastFoot + Vector3.up * 11f, 0.07f, 8);

            // Sails let out about 20° to port; the boom swings with the mainsail.
            var letOut = Quaternion.AngleAxis(20f, Vector3.up);
            Vector3 Out(Vector3 point) => mastFoot + letOut * (point - mastFoot);
            parts.Cylinder(0, mastFoot + Vector3.up * 1.4f, Out(new Vector3(0f, deckY + 1.4f, -2.9f)), 0.05f, 6);
            parts.Sail(2, mastFoot + Vector3.up * 1.5f, mastFoot + Vector3.up * 10.6f, Out(new Vector3(0f, deckY + 1.5f, -2.8f)), 0.35f);
            parts.Sail(2, new Vector3(0f, deckY + 0.5f, 4.3f), mastFoot + Vector3.up * 9.2f, new Vector3(-0.9f, deckY + 0.8f, 1.1f), 0.3f);
            return parts.ToMesh("SailBoat");
        }

        /// <summary>A flat strip trailing back from the stern (y just above the water), for the wake texture.</summary>
        static Mesh WakeMesh()
        {
            const float width = 6f;
            const float length = 16f;
            var mesh = new Mesh { name = "Wake" };
            mesh.SetVertices(new List<Vector3>
            {
                new(-width / 2f, 0.04f, -4.4f), new(width / 2f, 0.04f, -4.4f), new(width / 2f, 0.04f, -4.4f - length), new(-width / 2f, 0.04f, -4.4f - length),
            });
            mesh.SetUVs(0, new List<Vector2> { new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f) });
            mesh.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            mesh.SetTriangles(new[] { 0, 3, 2, 0, 2, 1 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Foam in a V spreading back from the stern, with churned water down the middle, fading with distance.</summary>
        static Texture2D WakeTexture() => GeneratedTexture("SeaWake", 128, 256, (u, v) =>
        {
            var across = Mathf.Abs(u - 0.5f) * 2f;
            var arm = 0.12f + 0.75f * v;
            var arms = Mathf.Exp(-Mathf.Pow((across - arm) / (0.05f + 0.1f * v), 2f));
            var churn = Mathf.Exp(-Mathf.Pow(across / (0.1f + 0.15f * v), 2f)) * Mathf.Pow(1f - v, 2f);
            var foam = Mathf.Clamp01((arms * 0.8f + churn) * Mathf.Pow(1f - v, 1.3f) * (0.55f + 0.7f * Fbm(u, v, 8, 3, 81)));
            return new Color(1f, 1f, 1f, foam);
        }, TextureWrapMode.Clamp);

        /// <summary>
        /// Builds a mesh's faces one material (submesh) at a time. Every face gets its own vertices, so it's flat
        /// shaded; the facing comes from a hint direction, so callers needn't care about winding.
        /// </summary>
        sealed class MeshParts
        {
            readonly List<Vector3> _vertices = new();
            readonly List<Vector3> _normals = new();
            readonly List<int>[] _triangles;

            public MeshParts(int submeshes)
            {
                _triangles = new List<int>[submeshes];
                for (var i = 0; i < submeshes; i++)
                    _triangles[i] = new List<int>();
            }

            public void Triangle(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-10f)
                    return;
                if (Vector3.Dot(normal, outward) < 0f)
                {
                    (b, c) = (c, b);
                    normal = -normal;
                }

                normal.Normalize();
                var start = _vertices.Count;
                _vertices.AddRange(new[] { a, b, c });
                _normals.AddRange(new[] { normal, normal, normal });
                _triangles[submesh].AddRange(new[] { start, start + 1, start + 2 });
            }

            public void Quad(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
            {
                Triangle(submesh, a, b, c, outward);
                Triangle(submesh, a, c, d, outward);
            }

            public void Box(int submesh, Vector3 center, Vector3 size, Quaternion rotation)
            {
                var half = size / 2f;
                Vector3 P(float x, float y, float z) => center + rotation * new Vector3(x * half.x, y * half.y, z * half.z);
                var c = new[] { P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1) };
                Quad(submesh, c[0], c[1], c[2], c[3], rotation * Vector3.back);
                Quad(submesh, c[5], c[4], c[7], c[6], rotation * Vector3.forward);
                Quad(submesh, c[4], c[0], c[3], c[7], rotation * Vector3.left);
                Quad(submesh, c[1], c[5], c[6], c[2], rotation * Vector3.right);
                Quad(submesh, c[3], c[2], c[6], c[7], rotation * Vector3.up);
                Quad(submesh, c[4], c[5], c[1], c[0], rotation * Vector3.down);
            }

            public void Cylinder(int submesh, Vector3 from, Vector3 to, float radius, int sides)
            {
                var axis = (to - from).normalized;
                var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                var other = Vector3.Cross(axis, side);
                for (var i = 0; i < sides; i++)
                {
                    var a0 = i * Mathf.PI * 2f / sides;
                    var a1 = (i + 1) * Mathf.PI * 2f / sides;
                    var r0 = (side * Mathf.Cos(a0) + other * Mathf.Sin(a0)) * radius;
                    var r1 = (side * Mathf.Cos(a1) + other * Mathf.Sin(a1)) * radius;
                    Quad(submesh, from + r0, from + r1, to + r1, to + r0, r0 + r1);
                }
            }

            /// <summary>A triangular sail (tack, head, clew), bellied out to port, both sides drawn.</summary>
            public void Sail(int submesh, Vector3 tack, Vector3 head, Vector3 clew, float belly)
            {
                const int steps = 6;
                var normal = Vector3.Cross(head - tack, clew - tack).normalized;
                if (normal.x > 0f)
                    normal = -normal;
                Vector3 Point(int i, int j)
                {
                    // i up the luff (tack to head), j across toward the clew; the cloth bellies most in the middle.
                    var up = i / (float)steps;
                    var across = j / (float)steps * (1f - up);
                    var point = tack + (head - tack) * up + (clew - tack) * across;
                    var fullness = Mathf.Sin(Mathf.PI * Mathf.Clamp01(across / Mathf.Max(1f - up, 0.001f))) * (1f - up * 0.6f);
                    return point + normal * (belly * fullness);
                }

                for (var i = 0; i < steps; i++)
                {
                    for (var j = 0; j < steps - i; j++)
                    {
                        Both(Point(i, j), Point(i + 1, j), Point(i, j + 1));
                        if (j < steps - i - 1)
                            Both(Point(i + 1, j), Point(i + 1, j + 1), Point(i, j + 1));
                    }
                }

                void Both(Vector3 a, Vector3 b, Vector3 c)
                {
                    Triangle(submesh, a, b, c, normal);
                    Triangle(submesh, a, b, c, -normal);
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, subMeshCount = _triangles.Length };
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                for (var i = 0; i < _triangles.Length; i++)
                    mesh.SetTriangles(_triangles[i], i);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // ---------------------------------------------------------------- The building outside

        /// <summary>
        /// Stucco on the south and west walls (the north and east ones get theirs in <see cref="WallWithOpenings"/>),
        /// the corners, a white cornice, a parapet hiding the roof and the roof itself.
        /// </summary>
        static void BuildExterior(Transform parent)
        {
            var root = new GameObject("Exterior").transform;
            root.SetParent(parent, false);
            var stucco = Stucco();
            var trim = SeaTrim();
            const float wx = HallWidth / 2f;
            const float wz = HallDepth / 2f;
            const float skin = WallThickness / 2f + CladdingThickness / 2f;
            const float height = CeilingHeight - PlinthTop;
            const float y = (CeilingHeight + PlinthTop) / 2f;

            void Skin(string name, Vector3 position, Vector3 size) => NoShadows(Box(name, root, position, size, stucco, collider: false));

            Skin("Outside_West", new Vector3(-wx - skin, y, 0f), new Vector3(CladdingThickness, height, HallDepth));
            // The south wall around the entrance vestibule (whose own walls are never seen from outside).
            const float vestibuleWest = DoorX - 1.35f;
            const float vestibuleEast = DoorX + 1.35f;
            Skin("Outside_South_West", new Vector3((-wx + vestibuleWest) / 2f, y, -wz - skin), new Vector3(vestibuleWest + wx, height, CladdingThickness));
            Skin("Outside_South_East", new Vector3((vestibuleEast + wx) / 2f, y, -wz - skin), new Vector3(wx - vestibuleEast, height, CladdingThickness));
            Skin("Outside_South_Top", new Vector3(DoorX, (CeilingHeight + DoorHeight + 0.5f) / 2f, -wz - skin), new Vector3(vestibuleEast - vestibuleWest, CeilingHeight - DoorHeight - 0.5f, CladdingThickness));
            // Square columns fill the corners, where the walls' ends leave a notch.
            const float corner = WallThickness / 2f + CladdingThickness;
            foreach (var (x, z) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
                Skin("Outside_Corner", new Vector3(x * (wx + corner / 2f), y, z * (wz + corner / 2f)), new Vector3(corner, height, corner));

            Band("Cornice", root, -0.1f, corner + 0.1f, CeilingHeight - 0.14f, CeilingHeight, trim);
            Band("Parapet", root, -WallThickness / 2f, corner, CeilingHeight, 4f, stucco);
            Band("Coping", root, -WallThickness / 2f - 0.05f, corner + 0.05f, 4f, 4.06f, trim);
            NoShadows(Box("Roof", root, new Vector3(0f, CeilingHeight + 0.12f, 0f), new Vector3(HallWidth - WallThickness, 0.04f, HallDepth - WallThickness),
                Lit("Sea_Roof", new Color(0.55f, 0.52f, 0.5f), 0.1f), collider: false));
        }

        /// <summary>
        /// A band around the top of the building, from <paramref name="inner"/> to <paramref name="outer"/> metres out
        /// from the wall lines. The north and south runs cover the corners; the east and west ones stop against them.
        /// </summary>
        static void Band(string name, Transform parent, float inner, float outer, float y0, float y1, Material material)
        {
            const float wx = HallWidth / 2f;
            const float wz = HallDepth / 2f;
            var y = (y0 + y1) / 2f;
            var middle = (inner + outer) / 2f;
            var depth = outer - inner;
            foreach (var side in new[] { 1f, -1f })
            {
                Box($"{name}_{(side > 0f ? "North" : "South")}", parent, new Vector3(0f, y, side * (wz + middle)), new Vector3(2f * (wx + outer), y1 - y0, depth), material, collider: false);
                Box($"{name}_{(side > 0f ? "East" : "West")}", parent, new Vector3(side * (wx + middle), y, 0f), new Vector3(depth, y1 - y0, 2f * (wz + inner)), material, collider: false);
            }
        }

        // ---------------------------------------------------------------- Terrace

        /// <summary>
        /// The deck out of the east door, with a glass railing (seated cats still see the sea over it) and, just
        /// inside it, an invisible wall on the CatBarrier layer: cats and props stop there, but the follow camera
        /// doesn't, so it can swing out over the water.
        /// </summary>
        static Transform BuildTerraceDeck(Transform parent, Material stone, out Material teak)
        {
            var terrace = new GameObject("Terrace").transform;
            terrace.SetParent(parent, false);
            const float west = HallWidth / 2f;
            teak = Textured("Sea_Teak", TeakTexture(), 0.3f, 2f);
            Box("Deck", terrace, new Vector3((west + TerraceEast) / 2f, -0.125f, (TerraceSouth + TerraceNorth) / 2f), new Vector3(TerraceEast - west, 0.25f, TerraceNorth - TerraceSouth), teak);
            NoShadows(Box("Threshold", terrace, new Vector3(west, 0.004f, (SeaDoorFrom + SeaDoorTo) / 2f), new Vector3(WallThickness + CladdingThickness * 2f, 0.008f, SeaDoorTo - SeaDoorFrom), stone, collider: false));

            var trim = SeaTrim();
            var glass = TransparentLit("Sea_RailGlass", new Color(0.82f, 0.92f, 0.95f, 0.16f), 0.95f);
            const float inset = 0.08f;
            const float railTop = 0.95f;
            // Each open side, from its start to its end along the deck's edge.
            var start = west + WallThickness / 2f + CladdingThickness;
            var deckCenter = new Vector2((west + DeckEast) / 2f, (TerraceSouth + TerraceNorth) / 2f);
            foreach (var (name, a, b) in new[]
                     {
                         ("Rail_North", new Vector2(start, TerraceNorth - inset), new Vector2(DeckEast - inset, TerraceNorth - inset)),
                         ("Rail_East", new Vector2(DeckEast - inset, TerraceNorth - inset), new Vector2(DeckEast - inset, TerraceSouth + inset)),
                         ("Rail_South", new Vector2(DeckEast - inset, TerraceSouth + inset), new Vector2(start, TerraceSouth + inset)),
                     })
            {
                var rail = new GameObject(name).transform;
                rail.SetParent(terrace, false);
                var length = (b - a).magnitude;
                var along = (b - a) / length;
                var middle = (a + b) / 2f;
                var alongX = Mathf.Abs(along.x) > 0.5f;
                Vector3 Size(float l, float h, float w) => alongX ? new Vector3(l, h, w) : new Vector3(w, h, l);

                // The north rail starts at the wall and the others at a corner the previous one already covers; all
                // but the south one run on over the corner they end at, so no two rails overlap.
                var fromWall = name == "Rail_North";
                var toWall = name == "Rail_South";
                var railFrom = a + along * (fromWall ? 0f : 0.05f);
                var railTo = b + along * (toWall ? 0f : 0.05f);
                var railMiddle = (railFrom + railTo) / 2f;
                Box("TopRail", rail, new Vector3(railMiddle.x, railTop + 0.025f, railMiddle.y), Size((railTo - railFrom).magnitude, 0.05f, 0.1f), teak, collider: false);
                var panels = Mathf.CeilToInt(length / 2.2f);
                for (var i = 0; i <= panels; i++)
                {
                    var post = a + along * (length * i / panels);
                    if (i > 0 || fromWall)
                        Box("Post", rail, new Vector3(post.x, railTop / 2f, post.y), new Vector3(0.07f, railTop, 0.07f), trim, collider: false);
                    if (i == panels)
                        continue;
                    var panel = a + along * (length * (i + 0.5f) / panels);
                    NoShadows(Box("Glass", rail, new Vector3(panel.x, 0.5f, panel.y), Size(length / panels - 0.09f, 0.86f, 0.02f), glass, collider: false));
                }

                // Just inside the railing, so cats stop short of the posts.
                var inward = alongX ? new Vector2(0f, Mathf.Sign(deckCenter.y - middle.y)) : new Vector2(Mathf.Sign(deckCenter.x - middle.x), 0f);
                var line = middle + inward * 0.06f;
                var barrier = Child(rail, "CatBarrier", new Vector3(line.x, 1.5f, line.y));
                barrier.gameObject.layer = CatBarrierLayer;
                barrier.gameObject.AddComponent<BoxCollider>().size = Size(length, 3f, 0.1f);
            }

            return terrace;
        }

        // ---------------------------------------------------------------- Terrace furniture

        /// <summary>
        /// Café tables on the terrace: two flank the path out of the door, one stands where the old railing was, one by
        /// the pool (CasinoSceneBuilder.Pool.cs).
        /// </summary>
        static readonly Vector3[] TerraceTables = { new(14.6f, 0f, 0.6f), new(14.6f, 0f, 5.4f), new(16.6f, 0f, 3f), new(20.3f, 0f, 0.5f) };
        /// <summary>Chairs round each table (degrees from east, counterclockwise), all on the land side facing the sea.</summary>
        static readonly float[] TerraceSeatAngles = { 180f, 125f, 235f };
        const float TerraceSeatHeight = 0.34f;

        /// <summary>
        /// The tables with their chairs, seat cameras and patron spots, lanterns (two of them real lights: WebGL only
        /// draws 32), potted palms, and spots where patrons stand at the railing.
        /// </summary>
        static void BuildTerraceFurniture(Transform terrace, Materials m, Material teak)
        {
            var top = Lit("Sea_TableTop", new Color(0.95f, 0.93f, 0.88f), 0.6f);
            var cushion = Lit("Sea_Cushion", new Color(0.86f, 0.82f, 0.72f), 0.1f);
            var glass = SeaGlass();
            var glow = Emissive("Sea_LanternGlow", new Color(1f, 0.85f, 0.6f), new Color(1f, 0.7f, 0.4f) * 4f);
            var spots = GameObject.Find(PatronSpotsName).transform;

            for (var t = 0; t < TerraceTables.Length; t++)
            {
                var center = TerraceTables[t];
                var root = new GameObject($"Terrace Table {t + 1}");
                root.transform.position = center;
                MakeMasterClientObject(root.AddComponent<NetworkObject>());
                var table = root.AddComponent<TerraceTable>();
                var tr = root.transform;
                Cylinder("Base", tr, new Vector3(0f, 0.015f, 0f), new Vector3(0.42f, 0.015f, 0.42f), m.Brass, collider: false);
                Cylinder("Pedestal", tr, new Vector3(0f, 0.3f, 0f), new Vector3(0.07f, 0.29f, 0.07f), m.Brass);
                Cylinder("Top", tr, new Vector3(0f, 0.58f, 0f), new Vector3(0.82f, 0.02f, 0.82f), top);
                Lantern(tr, new Vector3(0.1f, 0.6f, 0.05f), m, glass, glow, light: t < 2);
                // The table and chairs are lower than a cat can step, so the NavMesh would spread over their tops and
                // lift the deck's surface around them (patrons walking past would float). Keep it off them.
                NotWalkable(root);
                var camera = TableOrbitCamera(tr);

                var anchors = new Transform[TerraceSeatAngles.Length];
                var cameras = new CinemachineCamera[TerraceSeatAngles.Length];
                for (var seat = 0; seat < TerraceSeatAngles.Length; seat++)
                {
                    var angle = TerraceSeatAngles[seat] * Mathf.Deg2Rad;
                    var position = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.78f;
                    var facing = Quaternion.LookRotation(center - position, Vector3.up);
                    var chair = Chair(tr, $"Chair_{seat + 1}", position, facing, teak, cushion);
                    var terraceSeat = chair.gameObject.AddComponent<TerraceSeat>();
                    SetFields(terraceSeat, ("_table", table));
                    SetInt(terraceSeat, "_seat", seat);
                    SetFloat(terraceSeat, "_radius", 0.75f);
                    anchors[seat] = Child(chair, "Seat", new Vector3(0f, TerraceSeatHeight, 0f));

                    var seatPoint = anchors[seat].position;
                    cameras[seat] = camera;

                    // Patrons use two chairs at each table, so the hall doesn't empty out onto the terrace.
                    if (seat < 2)
                    {
                        var spot = AddPatronSpot(spots, $"Terrace_{t + 1}_{seat + 1}", seatPoint, anchors[seat].eulerAngles.y, true, new Vector2(12f, 30f));
                        SetFields(spot, ("_playerTable", table));
                        SetInt(spot, "_playerSeat", seat);
                    }
                }

                SetString(table, "_displayName", $"Terrace table {t + 1}");
                SetArray(table, "_seatAnchors", anchors);
                SetArray(table, "_seatCameras", cameras);
            }

            // Patrons also come out to sit at the railing and look at the sea. Sitting, not standing: a standing cat plays
            // idle emotes, and the meow and groom ones sit it down and stand it up again.
            AddPatronSpot(spots, "Terrace_Rail_East_A", new Vector3(DeckEast - 0.8f, 0f, 0.9f), 90f, true, new Vector2(10f, 25f));
            AddPatronSpot(spots, "Terrace_Rail_East_B", new Vector3(DeckEast - 0.8f, 0f, -0.5f), 90f, true, new Vector2(10f, 25f));

            var pot = Lit("Sea_Terracotta", new Color(0.72f, 0.38f, 0.24f), 0.15f);
            var soil = Lit("Sea_Soil", new Color(0.18f, 0.12f, 0.08f), 0.05f);
            var bark = Lit("Sea_PalmTrunk", new Color(0.46f, 0.37f, 0.27f), 0.1f);
            var frond = FrondMaterial();
            var frondMesh = SaveMesh(FrondMesh(), "Sea_PalmFrond");
            var palms = new GameObject("Palms").transform;
            palms.SetParent(terrace, false);
            var seed = 3;
            foreach (var position in new[] { new Vector3(17.35f, 0f, -0.85f), new Vector3(17.35f, 0f, 6.55f), new Vector3(11.75f, 0f, 1.5f), new Vector3(11.75f, 0f, 4.85f) })
                Palm(palms, position, seed++, pot, soil, bark, frond, frondMesh);
        }

        /// <summary>
        /// One camera per table that every seat shares: it orbits the middle of the table (A/D, W/S or a drag, see
        /// PlayerCameraRig), starting behind whoever sits down.
        /// </summary>
        // ---------------------------------------------------------------- Bar stools

        const int BarStools = 4;
        /// <summary>The stools' tops, where a seated cat sits (and where patrons sit, see BuildPatronSpots).</summary>
        const float BarStoolHeight = 0.4f;

        /// <summary>
        /// Players can sit on the bar stools too, as on the terrace chairs: one <see cref="TerraceTable"/> for the row,
        /// and each stool its own orbiting camera, starting behind the cat and kept on the room side of the counter.
        /// The patrons' stool spots give way to players.
        /// </summary>
        static void BuildBarSeats(Transform hall)
        {
            var bar = hall.Find("Bar");
            var spots = GameObject.Find(PatronSpotsName).transform;
            var root = new GameObject("Bar Seats");
            root.transform.SetParent(bar, false);
            MakeMasterClientObject(root.AddComponent<NetworkObject>());
            var table = root.AddComponent<TerraceTable>();

            var anchors = new Transform[BarStools];
            var cameras = new CinemachineCamera[BarStools];
            for (var i = 0; i < BarStools; i++)
            {
                var stool = bar.Find($"BarStool_{i + 1}");
                // Facing the counter (+Z), like the patrons who sit here, and a little behind the stool's centre.
                var top = new Vector3(stool.position.x, BarStoolHeight, stool.position.z - SeatBack);
                anchors[i] = Child(root.transform, $"Seat_{i + 1}", root.transform.InverseTransformPoint(top));
                var camera = TableOrbitCamera(anchors[i]);
                var orbit = camera.GetComponent<CinemachineOrbitalFollow>();
                orbit.Radius = 1.9f;
                orbit.HorizontalAxis.Range = new Vector2(-100f, 100f);
                orbit.HorizontalAxis.Wrap = false;
                cameras[i] = camera;

                var seat = stool.gameObject.AddComponent<TerraceSeat>();
                SetFields(seat, ("_table", table));
                SetInt(seat, "_seat", i);
                SetFloat(seat, "_radius", 0.6f);
                SetString(seat, "_prompt", "Sit at the bar");

                var spot = spots.Find($"BarStool_{i + 1}").GetComponent<PatronSpot>();
                SetFields(spot, ("_playerTable", table));
                SetInt(spot, "_playerSeat", i);
            }

            SetString(table, "_displayName", "Bar");
            SetArray(table, "_seatAnchors", anchors);
            SetArray(table, "_seatCameras", cameras);
            BuildWineBar(hall);
        }

        static CinemachineCamera TableOrbitCamera(Transform table)
        {
            var focus = Child(table, "OrbitTarget", new Vector3(0f, 0.5f, 0f));
            var camera = new GameObject("SeatCamera").AddComponent<CinemachineCamera>();
            camera.transform.SetParent(table, false);
            camera.Follow = focus;
            camera.LookAt = focus;
            camera.Lens = LensSettings.Default;
            camera.Lens.FieldOfView = 55f;
            camera.Lens.NearClipPlane = 0.05f;
            camera.Priority = 0;
            var orbit = camera.gameObject.AddComponent<CinemachineOrbitalFollow>();
            orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            orbit.Radius = 2.3f;
            orbit.HorizontalAxis.Range = new Vector2(-180f, 180f);
            orbit.HorizontalAxis.Wrap = true;
            orbit.VerticalAxis.Range = new Vector2(8f, 60f);
            orbit.VerticalAxis.Center = 22f;
            orbit.VerticalAxis.Value = 22f;
            camera.gameObject.AddComponent<CinemachineRotationComposer>().Damping = new Vector2(0.1f, 0.1f);
            return camera;
        }

        /// <summary>Keeps the NavMesh off this object's colliders (and its children's).</summary>
        static void NotWalkable(GameObject go)
        {
            var modifier = go.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = UnityEngine.AI.NavMesh.GetAreaFromName("Not Walkable");
        }

        /// <summary>A wooden café chair; its local +z faces the table. A plain box collider makes it solid (and a NavMesh step).</summary>
        static Transform Chair(Transform parent, string name, Vector3 position, Quaternion facing, Material teak, Material cushion)
        {
            var chair = Child(parent, name, Vector3.zero);
            chair.SetPositionAndRotation(position, facing);
            foreach (var (x, z) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
                Box("Leg", chair, new Vector3(x * 0.19f, 0.135f, z * 0.19f), new Vector3(0.035f, 0.27f, 0.035f), teak, collider: false);
            Box("Frame", chair, new Vector3(0f, 0.29f, 0f), new Vector3(0.46f, 0.04f, 0.46f), teak, collider: false);
            Box("Cushion", chair, new Vector3(0f, TerraceSeatHeight - 0.0175f, 0f), new Vector3(0.42f, 0.035f, 0.42f), cushion, collider: false);
            foreach (var x in new[] { -0.2f, 0.2f })
                Box("BackPost", chair, new Vector3(x, 0.48f, -0.21f), new Vector3(0.035f, 0.42f, 0.035f), teak, collider: false);
            foreach (var y in new[] { 0.46f, 0.62f })
                Box("Slat", chair, new Vector3(0f, y, -0.215f), new Vector3(0.44f, 0.1f, 0.025f), teak, collider: false);

            var seat = chair.gameObject.AddComponent<BoxCollider>();
            seat.center = new Vector3(0f, TerraceSeatHeight / 2f, 0f);
            seat.size = new Vector3(0.46f, TerraceSeatHeight, 0.46f);
            var back = Child(chair, "BackCollider", new Vector3(0f, 0.52f, -0.21f));
            back.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.46f, 0.36f, 0.05f);
            return chair;
        }

        /// <summary>
        /// A brass storm lantern with a glowing candle; only some get a real point light. The light hangs above the
        /// lantern and is soft: right on the candle it burnt the table top white and shone through it onto the base.
        /// </summary>
        static void Lantern(Transform parent, Vector3 position, Materials m, Material glass, Material glow, bool light)
        {
            var lantern = Child(parent, "Lantern", position);
            NoShadows(Box("Base", lantern, new Vector3(0f, 0.01f, 0f), new Vector3(0.13f, 0.02f, 0.13f), m.Brass, collider: false));
            NoShadows(Box("Cap", lantern, new Vector3(0f, 0.2f, 0f), new Vector3(0.13f, 0.025f, 0.13f), m.Brass, collider: false));
            NoShadows(Cylinder("Handle", lantern, new Vector3(0f, 0.23f, 0f), new Vector3(0.05f, 0.01f, 0.05f), m.Brass, collider: false));
            foreach (var (x, z) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
                NoShadows(Box("Post", lantern, new Vector3(x * 0.055f, 0.105f, z * 0.055f), new Vector3(0.012f, 0.17f, 0.012f), m.Brass, collider: false));
            NoShadows(Cylinder("Candle", lantern, new Vector3(0f, 0.06f, 0f), new Vector3(0.035f, 0.04f, 0.035f), glow, collider: false));
            NoShadows(Box("Glass", lantern, new Vector3(0f, 0.105f, 0f), new Vector3(0.11f, 0.17f, 0.11f), glass, collider: false));
            if (light)
                PointLight("Light", new Vector3(0f, 0.4f, 0f), 2.2f, 0.5f, new Color(1f, 0.72f, 0.45f), lantern);
        }

        /// <summary>A potted palm: a gently curving trunk of tapering segments and a crown of drooping fronds.</summary>
        static void Palm(Transform parent, Vector3 position, int seed, Material pot, Material soil, Material bark, Material frond, Mesh frondMesh)
        {
            var random = new System.Random(seed);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

            var palm = Child(parent, "Palm", position, Quaternion.Euler(0f, Range(0f, 360f), 0f));
            NotWalkable(palm.gameObject);
            Cylinder("Pot", palm, new Vector3(0f, 0.25f, 0f), new Vector3(0.6f, 0.25f, 0.6f), pot);
            Cylinder("Rim", palm, new Vector3(0f, 0.48f, 0f), new Vector3(0.66f, 0.04f, 0.66f), pot, collider: false);
            NoShadows(Cylinder("Soil", palm, new Vector3(0f, 0.505f, 0f), new Vector3(0.56f, 0.01f, 0.56f), soil, collider: false));

            const int segments = 7;
            const float segmentLength = 0.3f;
            var point = new Vector3(0f, 0.45f, 0f);
            var lean = new Vector3(Range(-1f, 1f), 0f, Range(-1f, 1f)).normalized * 0.12f;
            for (var i = 0; i < segments; i++)
            {
                var direction = (Vector3.up + lean * (1f + i * 0.35f)).normalized;
                var radius = Mathf.Lerp(0.15f, 0.09f, i / (segments - 1f));
                var segment = Cylinder("Trunk", palm, point + direction * (segmentLength / 2f), new Vector3(radius, segmentLength / 2f + 0.015f, radius), bark, collider: false);
                segment.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
                point += direction * segmentLength;
            }

            NoShadows(Cylinder("Crown", palm, point, new Vector3(0.16f, 0.08f, 0.16f), bark, collider: false));
            const int fronds = 9;
            for (var i = 0; i < fronds; i++)
            {
                var leaf = new GameObject("Frond");
                leaf.transform.SetParent(palm, false);
                leaf.transform.localPosition = point;
                leaf.transform.localRotation = Quaternion.Euler(Range(-25f, 5f), i * 360f / fronds + Range(-12f, 12f), Range(-10f, 10f));
                leaf.transform.localScale = Vector3.one * Range(0.85f, 1.15f);
                leaf.AddComponent<MeshFilter>().sharedMesh = frondMesh;
                leaf.AddComponent<MeshRenderer>().sharedMaterial = frond;
            }
        }

        /// <summary>
        /// One palm frond along +z: arching up and drooping toward the tip, folded down from the midrib, with a back
        /// side of its own (so both sides are lit right). The texture cuts it into leaflets.
        /// </summary>
        static Mesh FrondMesh()
        {
            const int steps = 10;
            const float length = 1.3f;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var y = 0.4f * t - 0.85f * t * t;
                // sin(pi) comes out a hair below zero, and a fractional power of that is NaN.
                var halfWidth = 0.26f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Min(t * 1.1f + 0.05f, 1f))), 0.6f) * (1f - 0.3f * t);
                var fold = halfWidth * 0.35f;
                vertices.Add(new Vector3(-halfWidth, y - fold, t * length));
                vertices.Add(new Vector3(0f, y, t * length));
                vertices.Add(new Vector3(halfWidth, y - fold, t * length));
                uvs.Add(new Vector2(0f, t));
                uvs.Add(new Vector2(0.5f, t));
                uvs.Add(new Vector2(1f, t));
            }

            var front = new List<int>();
            for (var i = 0; i < steps; i++)
            {
                for (var side = 0; side < 2; side++)
                {
                    var a = i * 3 + side;
                    front.AddRange(new[] { a, a + 3, a + 4, a, a + 4, a + 1 });
                }
            }

            // Make the front face up, then add a reversed copy for the underside.
            for (var i = 0; i < front.Count; i += 3)
            {
                if (Vector3.Cross(vertices[front[i + 1]] - vertices[front[i]], vertices[front[i + 2]] - vertices[front[i]]).y < 0f)
                    (front[i + 1], front[i + 2]) = (front[i + 2], front[i + 1]);
            }

            var count = vertices.Count;
            var triangles = new List<int>(front);
            for (var i = 0; i < front.Count; i += 3)
                triangles.AddRange(new[] { front[i] + count, front[i + 2] + count, front[i + 1] + count });
            vertices.AddRange(vertices.GetRange(0, count));
            uvs.AddRange(uvs.GetRange(0, count));

            var mesh = new Mesh { name = "PalmFrond" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Alpha-clipped leaflets sweeping toward the tip on either side of a midrib.</summary>
        static Material FrondMaterial()
        {
            var texture = GeneratedTexture("SeaFrond", 256, 256, (u, v) =>
            {
                var across = Mathf.Abs(u - 0.5f) * 2f;
                var leaflet = Mathf.Repeat(v * 22f - across * 1.4f, 1f);
                var width = 0.55f * (1f - across * 0.5f);
                var leaf = Edge(0f, 0.08f, leaflet) * (1f - Edge(width - 0.08f, width, leaflet)) * (1f - Edge(0.9f, 0.98f, across));
                var alpha = Mathf.Max(leaf, 1f - Edge(0.04f, 0.07f, across));
                var color = Color.Lerp(new Color(0.2f, 0.33f, 0.11f), new Color(0.4f, 0.52f, 0.2f), (1f - across) * 0.6f + 0.25f * Fbm(u, v, 8, 2, 71));
                return new Color(color.r, color.g, color.b, alpha);
            }, TextureWrapMode.Clamp, importer =>
            {
                importer.mipMapsPreserveCoverage = true;
                importer.alphaTestReferenceValue = 0.5f;
            });

            var material = Lit("Sea_PalmFrond", Color.white, 0.25f);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Teak decking: planks running along z with staggered butt joints, tiled every 2 m.</summary>
        static Texture2D TeakTexture() => GeneratedTexture("SeaTeak", 512, 512, (u, v) =>
        {
            const int planks = 14;
            var plank = Mathf.FloorToInt(u * planks);
            var across = u * planks - plank;
            var offset = Hash(plank % planks, 0, 61);
            var run = v * 2f + offset;
            var board = Mathf.FloorToInt(run);
            var along = run - board;
            var joint = Mathf.Min(Mathf.Min(across, 1f - across) / planks, Mathf.Min(along, 1f - along) / 2f);
            var gap = 1f - Edge(0.001f, 0.0025f, joint);
            var grain = 0.5f + 0.5f * Mathf.Sin((u * planks * 7f + Fbm(u, v, 8, 2, 63) * 2.5f) * Mathf.PI * 2f);
            var tone = Hash(plank % planks, board % 2, 67);
            var color = Color.Lerp(new Color(0.4f, 0.31f, 0.23f), new Color(0.64f, 0.52f, 0.4f), 0.3f + 0.35f * tone + 0.2f * grain);
            return Color.Lerp(color, color * 0.3f, gap);
        });
    }
}
