using CasinoMares.Npc;
using CasinoMares.Social;
using Fusion;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace CasinoMares.Editor
{
    /// <summary>
    /// The wine bars (GDD §4.3): the networked <see cref="WineBar"/> on a bar's seat object, the generated wine glass
    /// prefab, and the cat animator's Drink state. <see cref="BuildWineBar"/> runs once per bar: from BuildBarSeats
    /// for the hall's (sea scene only: the classic hall has a bar but no stools you can sit on), from BuildPoolBar for
    /// the one by the pool. The Drink state is hooked from BuildCatController.
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        const string WineGlassPrefabPath = Root + "/Prefabs/WineGlass.prefab";
        /// <summary>
        /// The bar counters' top surface (BuildBar, BuildPoolBar), where glasses are poured, slide and stand, and the
        /// props on the hall bar rest. Low enough for a cat seated on a stool (BarStoolHeight, 0.4 m; the cat is about
        /// 0.58 m tall) that its chin is about level with the top and its head and shoulders are above it.
        /// </summary>
        const float BarCounterTop = 0.72f;
        /// <summary>The solid top slab on each counter, and the counter body under it (overlapping it a little).</summary>
        const float BarCounterTopThickness = 0.05f;
        const float BarCounterBodyHeight = BarCounterTop - 0.045f;
        static readonly Vector3 WinePourPoint = new(-5.2f, BarCounterTop, 5.45f);
        /// <summary>
        /// Glasses slide down the middle of the counter top rather than its front edge (z 4.95), where the knockable
        /// glasses stand: a sliding glass would pass straight through them.
        /// </summary>
        static readonly Vector3 WineSlideLane = new(-5.2f, BarCounterTop, 5.2f);

        /// <summary>
        /// The seats of one bar: one <see cref="TerraceTable"/> for the row (a master-client object under
        /// <paramref name="parent"/>), and for each stool a seat anchor on its top, a little behind its centre and
        /// facing the counter (<paramref name="facing"/>), its own orbiting camera kept on the guests' side, the
        /// <see cref="TerraceSeat"/> prompt on the stool, and the patron spot that gives way to players.
        /// </summary>
        static TerraceTable BuildBarSeatRow(Transform parent, string name, string displayName, Transform[] stools, PatronSpot[] spots, Quaternion facing)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            MakeMasterClientObject(root.AddComponent<NetworkObject>());
            var table = root.AddComponent<TerraceTable>();

            var anchors = new Transform[stools.Length];
            var cameras = new CinemachineCamera[stools.Length];
            for (var i = 0; i < stools.Length; i++)
            {
                var stool = stools[i];
                var top = new Vector3(stool.position.x, BarStoolHeight, stool.position.z) - facing * Vector3.forward * SeatBack;
                anchors[i] = Child(root.transform, $"Seat_{i + 1}", root.transform.InverseTransformPoint(top),
                    Quaternion.Inverse(root.transform.rotation) * facing);
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

                SetFields(spots[i], ("_playerTable", table));
                SetInt(spots[i], "_playerSeat", i);
            }

            SetString(table, "_displayName", displayName);
            SetArray(table, "_seatAnchors", anchors);
            SetArray(table, "_seatCameras", cameras);
            return table;
        }

        /// <summary>
        /// Adds the wine service to a bar's seat row: <paramref name="bartender"/> pours at <paramref name="pourPoint"/>
        /// and slides glasses along the counter through <paramref name="slideLane"/>, which runs along the counter
        /// (across <paramref name="facing"/>, the way the seated cats face). Each bar runs its own orders; every glass
        /// counts toward the drinker's one DrinkCount. The bars share one <paramref name="glassPrefab"/>
        /// (<see cref="BuildWineGlassPrefab"/>, made once per build).
        /// </summary>
        static void BuildWineBar(TerraceTable table, StaffCat bartender, Vector3 pourPoint, Vector3 slideLane, Quaternion facing, GameObject glassPrefab)
        {
            var seats = table.transform;
            SetBool(table, "_isBar", true);

            var wineBar = seats.gameObject.AddComponent<WineBar>();
            var pour = Child(seats, "PourPoint", seats.InverseTransformPoint(pourPoint));
            var lane = Child(seats, "SlideLane", seats.InverseTransformPoint(slideLane), Quaternion.Inverse(seats.rotation) * facing);
            SetFields(wineBar,
                ("_table", table),
                ("_bartender", bartender),
                ("_bartenderVisual", bartender.GetComponent<NpcCatVisual>()),
                ("_glassPrefab", glassPrefab),
                ("_pourPoint", pour),
                ("_slideLane", lane));
        }



        /// <summary>
        /// A stemmed glass from primitives: base disc, stem and bowl in the props' glass material, and a "Wine"
        /// cylinder inside the bowl whose height WineBar scales with the level. No collider, no physics.
        /// </summary>
        static GameObject BuildWineGlassPrefab()
        {
            var glass = Load<Material>($"{Root}/Art/Materials/PropGlass.mat");
            var wine = TransparentLit("WineRed", new Color(0.45f, 0.03f, 0.1f, 0.85f), 0.9f);
            // Drawn before the bowl around it, so the glass tints the wine rather than hiding it.
            wine.renderQueue = (int)RenderQueue.Transparent - 1;
            EditorUtility.SetDirty(wine);

            var root = new GameObject("WineGlass");
            var t = root.transform;
            NoShadows(Cylinder("Base", t, new Vector3(0f, 0.004f, 0f), new Vector3(0.07f, 0.004f, 0.07f), glass, collider: false));
            NoShadows(Cylinder("Stem", t, new Vector3(0f, 0.036f, 0f), new Vector3(0.012f, 0.03f, 0.012f), glass, collider: false));
            NoShadows(Cylinder("Bowl", t, new Vector3(0f, 0.11f, 0f), new Vector3(0.07f, 0.045f, 0.07f), glass, collider: false));
            var liquid = Cylinder("Wine", t, new Vector3(0f, WineBar.LiquidBottom + WineBar.LiquidHalfHeight, 0f),
                new Vector3(0.06f, WineBar.LiquidHalfHeight, 0.06f), wine, collider: false);
            NoShadows(liquid);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, WineGlassPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// The Drink state: the licking clip, looping, entered from Sit while "Drinking" is set (CatAnimatorDriver
        /// writes it from WineBar) and left again when it's cleared, or when the cat stands up.
        /// </summary>
        static void BuildDrinkState(AnimatorController controller, AnimatorStateMachine machine, AnimatorState sit, AnimatorState standUp)
        {
            controller.AddParameter("Drinking", AnimatorControllerParameterType.Bool);
            var drink = State(machine, "Drink", "licking");
            Transition(sit, drink, 0.2f, ("Drinking", AnimatorConditionMode.If));
            Transition(drink, sit, 0.2f, ("Drinking", AnimatorConditionMode.IfNot));
            Transition(drink, standUp, 0.1f, ("Sitting", AnimatorConditionMode.IfNot));
        }

        static void SetBool(Object target, string field, bool value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
