using CasinoMares.Npc;
using CasinoMares.Social;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace CasinoMares.Editor
{
    /// <summary>
    /// The wine bar (GDD §4.3): the networked <see cref="WineBar"/> on the bar's seat object, the generated wine glass
    /// prefab, and the cat animator's Drink state. Hooked at the end of BuildBarSeats (sea scene only: the classic
    /// hall has a bar but no stools you can sit on) and from BuildCatController.
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        const string WineGlassPrefabPath = Root + "/Prefabs/WineGlass.prefab";
        /// <summary>The bar counter's top surface (see BuildBar) and where glasses are poured and slide.</summary>
        const float BarCounterTop = 0.845f;
        static readonly Vector3 WinePourPoint = new(-5.2f, BarCounterTop, 5.45f);
        /// <summary>
        /// Glasses slide down the middle of the counter top rather than its front edge (z 4.95), where the knockable
        /// glasses stand: a sliding glass would pass straight through them.
        /// </summary>
        static readonly Vector3 WineSlideLane = new(-5.2f, BarCounterTop, 5.2f);

        /// <summary>Adds the wine service to the bar seats (the "Bar Seats" TerraceTable under the hall's Bar).</summary>
        static void BuildWineBar(Transform hall)
        {
            var bar = hall.Find("Bar");
            var seats = bar.Find("Bar Seats");
            var table = seats.GetComponent<TerraceTable>();
            SetBool(table, "_isBar", true);

            var bartender = bar.Find("Bartender");
            var wineBar = seats.gameObject.AddComponent<WineBar>();
            var pour = Child(seats, "PourPoint", seats.InverseTransformPoint(WinePourPoint));
            var lane = Child(seats, "SlideLane", seats.InverseTransformPoint(WineSlideLane));
            SetFields(wineBar,
                ("_table", table),
                ("_bartender", bartender.GetComponent<StaffCat>()),
                ("_bartenderVisual", bartender.GetComponent<NpcCatVisual>()),
                ("_glassPrefab", BuildWineGlassPrefab()),
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
