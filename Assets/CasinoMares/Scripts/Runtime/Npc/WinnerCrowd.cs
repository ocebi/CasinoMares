using System.Collections.Generic;
using CasinoMares.Core;
using CasinoMares.Player;
using Fusion;
using UnityEngine;
using UnityEngine.AI;

namespace CasinoMares.Npc
{
    /// <summary>
    /// Patrons gather behind a player on a winning streak (GDD §4.4): 50 chips up at the current table draws the
    /// first watcher and bigger winnings draw more. They stand in an arc behind the seat, cheer each win and drift
    /// off again once the streak is over. Runs on the master client, which simulates the patrons.
    /// </summary>
    public static class WinnerCrowd
    {
        /// <summary>Winnings needed for the 1st, 2nd, 3rd... watcher.</summary>
        static readonly int[] Thresholds = { 50, 150, 300, 500, 800, 1200 };

        /// <summary>Where watchers stand around a seat: angle from straight behind it, and distance.</summary>
        static readonly (float angle, float radius)[] Arc =
        {
            (0f, 1.3f), (-35f, 1.35f), (35f, 1.35f), (-65f, 1.4f), (65f, 1.4f),
            (-18f, 2f), (18f, 2f), (-48f, 2.05f), (48f, 2.05f), (0f, 2.6f),
        };

        const float MinSpacing = 0.6f;
        /// <summary>Patrons that never get pulled into a crowd, so the room keeps some life elsewhere.</summary>
        const int KeepWandering = 2;

        struct Assignment
        {
            public PlayerRef Player;
            public Vector3 Spot;
        }

        static readonly Dictionary<PatronBrain, Assignment> Assignments = new();
        static readonly Dictionary<PlayerRef, int> SeenResults = new();
        static readonly List<PatronBrain> Scratch = new();
        static readonly List<PlayerCat> Winners = new();
        static int _lastTick = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Assignments.Clear();
            SeenResults.Clear();
            _lastTick = -1;
        }

        public static int ViewersFor(int winnings)
        {
            var count = 0;
            foreach (var threshold in Thresholds)
            {
                if (winnings >= threshold)
                    count++;
            }

            return count;
        }

        /// <summary>Called by every patron the master simulates; does its work once per tick.</summary>
        public static void Tick(NetworkRunner runner)
        {
            if (runner.Tick.Raw == _lastTick)
                return;
            _lastTick = runner.Tick.Raw;

            DropStaleAssignments();
            Winners.Clear();
            foreach (var cat in PlayerCat.All)
            {
                if (cat == null || cat.Object == null || !cat.Object.IsValid || cat.Winnings == null || !cat.IsSeated)
                    continue;
                if (ViewersFor(cat.Winnings.Amount) > 0)
                    Winners.Add(cat);
                Cheer(cat);
            }

            ReleaseFinished();
            foreach (var winner in Winners)
                Recruit(winner);
        }

        public static bool IsAssigned(PatronBrain patron) => Assignments.ContainsKey(patron);

        /// <summary>Patrons that despawned or moved to another client are no longer ours to direct.</summary>
        static void DropStaleAssignments()
        {
            Scratch.Clear();
            foreach (var patron in Assignments.Keys)
            {
                if (patron == null || patron.Object == null || !patron.Object.IsValid || !patron.HasStateAuthority)
                    Scratch.Add(patron);
            }

            foreach (var patron in Scratch)
                Assignments.Remove(patron);
        }

        /// <summary>Sends watchers home when their player got up, left, or dropped below the streak they came for.</summary>
        static void ReleaseFinished()
        {
            Scratch.Clear();
            foreach (var (patron, assignment) in Assignments)
            {
                var cat = PlayerCat.Find(assignment.Player);
                var wanted = cat != null && cat.IsSeated && cat.Winnings != null ? ViewersFor(cat.Winnings.Amount) : 0;
                if (CountFor(assignment.Player) > wanted)
                    Scratch.Add(patron);
            }

            // Only one leaves per tick; the next tick recounts.
            if (Scratch.Count == 0)
                return;
            var leaving = Scratch[^1];
            Assignments.Remove(leaving);
            leaving.StopWatching();
        }

        static void Recruit(PlayerCat winner)
        {
            if (!GameTables.TryFindSeat(winner.Owner, out var table, out var seat))
                return;
            var anchor = table.GetSeatAnchor(seat);
            if (anchor == null)
                return;

            var wanted = ViewersFor(winner.Winnings.Amount);
            var have = CountFor(winner.Owner);
            if (have >= wanted || Assignments.Count >= PatronBrain.All.Count - KeepWandering)
                return;
            if (!TryFindSpot(anchor, out var spot))
                return;

            // The closest free patron comes over.
            PatronBrain best = null;
            var bestDistance = float.MaxValue;
            foreach (var patron in PatronBrain.All)
            {
                if (patron == null || !patron.HasStateAuthority || Assignments.ContainsKey(patron))
                    continue;
                var distance = (patron.transform.position - spot).sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = patron;
                    bestDistance = distance;
                }
            }

            if (best == null)
                return;
            Assignments[best] = new Assignment { Player = winner.Owner, Spot = spot };
            best.Watch(spot, anchor.position);
        }

        /// <summary>A free spot in the arc behind the seat that's on the NavMesh and not too close to another watcher.</summary>
        static bool TryFindSpot(Transform anchor, out Vector3 spot)
        {
            var back = -anchor.forward;
            back.y = 0f;
            back.Normalize();
            var seat = anchor.position;
            seat.y = 0f;

            foreach (var (angle, radius) in Arc)
            {
                var point = seat + Quaternion.Euler(0f, angle, 0f) * back * radius;
                if (!NavMesh.SamplePosition(point, out var hit, 0.45f, NavMesh.AllAreas))
                    continue;
                if (IsTaken(hit.position))
                    continue;
                spot = hit.position;
                return true;
            }

            spot = default;
            return false;
        }

        static bool IsTaken(Vector3 point)
        {
            foreach (var assignment in Assignments.Values)
            {
                if ((assignment.Spot - point).sqrMagnitude < MinSpacing * MinSpacing)
                    return true;
            }

            // Don't block a seated player's stool either.
            foreach (var cat in PlayerCat.All)
            {
                if (cat != null && FlatDistance(cat.transform.position, point) < MinSpacing)
                    return true;
            }

            return false;
        }

        static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        static int CountFor(PlayerRef player)
        {
            var count = 0;
            foreach (var assignment in Assignments.Values)
            {
                if (assignment.Player == player)
                    count++;
            }

            return count;
        }

        /// <summary>Every settled round: the crowd hops and meows at a win.</summary>
        static void Cheer(PlayerCat cat)
        {
            var results = cat.Winnings.ResultCount;
            if (!SeenResults.TryGetValue(cat.Owner, out var seen))
            {
                SeenResults[cat.Owner] = results;
                return;
            }

            if (seen == results)
                return;
            SeenResults[cat.Owner] = results;
            if (cat.Winnings.LastResult <= 0)
                return;

            foreach (var (patron, assignment) in Assignments)
            {
                if (assignment.Player == cat.Owner)
                    patron.Cheer();
            }
        }
    }
}
