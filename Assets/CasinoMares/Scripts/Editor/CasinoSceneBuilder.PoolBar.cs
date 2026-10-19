using CasinoMares.Npc;
using UnityEditor;
using UnityEngine;

namespace CasinoMares.Editor
{
    /// <summary>
    /// A second wine bar by the pool (plan §11.3, GDD §4.3): a teak counter with a stone top along the east railing,
    /// three stools on its pool side so the seated cats look across it at the sea and the sunset, and its own bartender
    /// on a step behind it. The counter, step and stools copy the hall bar's (BuildBar) heights and depths from the
    /// stools to the bartender, so <see cref="Social.WineBar"/>'s glass spots fit both. Its seats and wine service are
    /// built by the same <see cref="BuildBarSeatRow"/> and <see cref="BuildWineBar"/> as the hall's. Sea scene only.
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        /// <summary>The middle of the stool row, and the way the seated cats face (toward the counter and the sea).</summary>
        static readonly Vector3 PoolBarStools = new(23.9f, 0f, 0.5f);
        const float PoolBarYaw = 90f;
        const int PoolBarStoolCount = 3;
        const float PoolBarStoolSpacing = 1.1f;

        // Measured from the stools toward the counter, as in the hall bar (stools at z 4.45 there).
        const float PoolBarCounterLength = 2.9f;
        const float PoolBarCounterMiddle = 0.85f;
        const float PoolBarCounterDepth = 0.8f;
        const float PoolBarTopMiddle = 0.8f;
        const float PoolBarTopDepth = 0.95f;
        const float PoolBarPour = 1f;
        const float PoolBarLane = 0.75f;
        const float PoolBarBartender = 1.55f;
        const float PoolBarStepHeight = 0.5f;

        static void BuildPoolBar(Transform terrace, Material stone, Material teak)
        {
            var facing = Quaternion.Euler(0f, PoolBarYaw, 0f);
            var bar = Child(terrace, "Pool Bar", PoolBarStools, facing);

            Box("Counter", bar, new Vector3(0f, 0.4f, PoolBarCounterMiddle), new Vector3(PoolBarCounterLength, 0.8f, PoolBarCounterDepth), teak);
            Box("CounterTop", bar, new Vector3(0f, BarCounterTop - 0.025f, PoolBarTopMiddle), new Vector3(PoolBarCounterLength + 0.2f, 0.05f, PoolBarTopDepth), stone);
            Box("BartenderStep", bar, new Vector3(0f, PoolBarStepHeight / 2f, PoolBarBartender), new Vector3(1.2f, PoolBarStepHeight, 0.6f), teak);
            // Side panels from the counter's back to the railing, so nobody walks round behind the bartender.
            var back = PoolBarCounterMiddle + PoolBarCounterDepth / 2f;
            const float behind = 0.65f;
            foreach (var side in new[] { -1f, 1f })
                Box("Side", bar, new Vector3(side * (PoolBarCounterLength / 2f - 0.05f), 0.4f, back + behind / 2f), new Vector3(0.1f, 0.8f, behind), teak);

            var spots = GameObject.Find(PatronSpotsName).transform;
            var stools = new Transform[PoolBarStoolCount];
            var stoolSpots = new PatronSpot[PoolBarStoolCount];
            for (var i = 0; i < PoolBarStoolCount; i++)
            {
                var x = (i - (PoolBarStoolCount - 1) / 2f) * PoolBarStoolSpacing;
                stools[i] = Cylinder($"PoolBarStool_{i + 1}", bar, new Vector3(x, BarStoolHeight / 2f, 0f), new Vector3(0.4f, BarStoolHeight / 2f, 0.4f), teak).transform;
                var seatPoint = new Vector3(stools[i].position.x, BarStoolHeight, stools[i].position.z) - facing * Vector3.forward * SeatBack;
                stoolSpots[i] = AddPatronSpot(spots, $"PoolBarStool_{i + 1}", seatPoint, PoolBarYaw, true, new Vector2(10f, 25f));
            }

            var npcPrefab = Load<GameObject>(NpcPrefabPath).GetComponent<NpcCatVisual>();
            var bartender = (NpcCatVisual)PrefabUtility.InstantiatePrefab(npcPrefab, bar);
            bartender.name = "PoolBartender";
            bartender.transform.SetLocalPositionAndRotation(new Vector3(0f, PoolBarStepHeight, PoolBarBartender), Quaternion.Euler(0f, 180f, 0f));
            var staff = bartender.gameObject.AddComponent<StaffCat>();
            SetFields(staff, ("_visual", bartender));
            SetInt(staff, "_fur", 2);

            // Lower than a cat's climb, so the NavMesh would spread over the stools; patrons reach their spots anyway.
            NotWalkable(bar.gameObject);

            var table = BuildBarSeatRow(bar, "Pool Bar Seats", "Pool bar", stools, stoolSpots, facing);
            BuildWineBar(table, staff, bar.TransformPoint(new Vector3(0f, BarCounterTop, PoolBarPour)),
                bar.TransformPoint(new Vector3(0f, BarCounterTop, PoolBarLane)), facing, Load<GameObject>(WineGlassPrefabPath));
        }
    }
}
