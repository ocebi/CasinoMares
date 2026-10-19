using System;
using System.Collections.Generic;
using CasinoMares.Player;
using Fusion;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace CasinoMares.Npc
{
    /// <summary>Networked bookkeeping for a table's NPC players in the "random" setting.</summary>
    public struct NpcCrew : INetworkStruct
    {
        /// <summary>How many NPCs the table is drifting toward right now.</summary>
        public byte Target;

        /// <summary>When to roll a new target.</summary>
        public TickTimer Churn;

        /// <summary>Spaces arrivals and departures out, so cats come and go one at a time.</summary>
        public TickTimer Step;

        /// <summary>False until the first fill: the cats already at the table when the session starts don't walk in.</summary>
        public NetworkBool Settled;
    }

    /// <summary>
    /// NPC players at the game tables (GDD §5.6). With the table's NPC setting on "random" (the default), cats wander
    /// in from the entrance, play a while and leave again on their own; a number instead keeps exactly that many.
    /// Also the shared walking math, so every client animates an arriving or leaving cat along the same path.
    /// </summary>
    public static class NpcSeating
    {
        /// <summary>The NpcFill value for "random". Any other value is an exact number of NPC players.</summary>
        public const byte Auto = 255;
        public const int MaxFill = 4;
        public const float WalkSpeed = 1.1f;
        /// <summary>Seconds for the hop between the floor behind the stool and the seat.</summary>
        public const float HopSeconds = 0.45f;

        const float StandBack = 0.6f;
        /// <summary>Seconds a seat stays empty of NPCs after its cat was pawed and ran off.</summary>
        public const float ScareRestSeconds = 60f;
        /// <summary>How close a pawing player must really be to the seat (the paw reaches about a metre; this allows for lag).</summary>
        const float PawRange = 1.8f;
        /// <summary>In <see cref="Plan"/>'s occupants: an empty seat resting after a scare, which no NPC takes.</summary>
        const byte Resting = 3;

        static NavMeshPath _path;

        /// <summary>What <see cref="Plan"/> sees in a seat: an empty one still resting after a scare isn't free.</summary>
        public static byte PlanOccupant(byte occupant, TickTimer rest, NetworkRunner runner) =>
            occupant == 0 && !rest.ExpiredOrNotRunning(runner) ? Resting : occupant;

        /// <summary>Whether <paramref name="player"/>'s cat is close enough to have pawed the cat on this seat.</summary>
        public static bool InPawReach(PlayerRef player, Transform seatAnchor)
        {
            var cat = PlayerCat.Find(player);
            return cat != null && (cat.transform.position - seatAnchor.position).sqrMagnitude <= PawRange * PawRange;
        }

        public static string Describe(byte fill) => fill == Auto ? "random" : fill.ToString();

        /// <summary>The settings button cycles random → 0 → 1 → ... → 4 → random.</summary>
        public static byte NextOption(byte fill) => fill == Auto ? (byte)0 : fill >= MaxFill ? Auto : (byte)(fill + 1);

        public static byte Clamp(byte fill) => fill == Auto ? Auto : (byte)Mathf.Min(fill, MaxFill);

        /// <summary>
        /// Decides the next NPC to sit down or get up, if any. Call from the table authority, only between rounds.
        /// <paramref name="occupants"/> uses the tables' shared layout: 0 empty, 1 player, 2 NPC.
        /// </summary>
        public static bool Plan(NetworkRunner runner, ref NpcCrew crew, byte fill, ReadOnlySpan<byte> occupants,
            out int seat, out bool join, out bool instant)
        {
            seat = -1;
            join = false;
            instant = !crew.Settled;

            var npcs = 0;
            var empty = 0;
            foreach (var occupant in occupants)
            {
                if (occupant == 2)
                    npcs++;
                else if (occupant == 0)
                    empty++;
            }

            var auto = fill == Auto;
            int goal;
            if (auto)
            {
                if (crew.Churn.ExpiredOrNotRunning(runner))
                {
                    crew.Target = Scale(RollTarget(), NpcBudget.SeatScale);
                    crew.Churn = TickTimer.CreateFromSeconds(runner, Random.Range(30f, 80f));
                }

                goal = crew.Target;
                if (crew.Settled && !crew.Step.ExpiredOrNotRunning(runner))
                    return false;
            }
            else
            {
                goal = fill;
            }

            if (npcs > goal)
            {
                seat = auto ? PickSeat(occupants, 2) : LastSeat(occupants, 2);
            }
            else if (npcs < goal && empty > 1)
            {
                // Keep at least one seat free so a friend can always sit down.
                seat = auto ? PickSeat(occupants, 0) : FirstSeat(occupants, 0);
                join = true;
            }
            else
            {
                crew.Settled = true;
                return false;
            }

            if (auto && crew.Settled)
                crew.Step = TickTimer.CreateFromSeconds(runner, Random.Range(3f, 10f));
            return seat >= 0;
        }

        /// <summary>Mostly one or two NPCs, sometimes an empty table or a busy one.</summary>
        static byte RollTarget()
        {
            var roll = Random.value;
            return roll switch
            {
                < 0.15f => 0,
                < 0.45f => 1,
                < 0.75f => 2,
                < 0.93f => 3,
                _ => 4,
            };
        }

        /// <summary>
        /// The rolled target times the player-count scale (<see cref="NpcBudget"/>), rounded up or down at random so the
        /// tables average the scaled count. Cats already seated only leave when the table next rolls lower.
        /// </summary>
        static byte Scale(byte target, float scale) =>
            scale >= 1f ? target : (byte)Mathf.Min(MaxFill, Mathf.FloorToInt(target * scale + Random.value));

        static int PickSeat(ReadOnlySpan<byte> occupants, byte kind)
        {
            var count = 0;
            foreach (var occupant in occupants)
            {
                if (occupant == kind)
                    count++;
            }

            if (count == 0)
                return -1;
            var pick = Random.Range(0, count);
            for (var i = 0; i < occupants.Length; i++)
            {
                if (occupants[i] == kind && pick-- == 0)
                    return i;
            }

            return -1;
        }

        static int FirstSeat(ReadOnlySpan<byte> occupants, byte kind)
        {
            for (var i = 0; i < occupants.Length; i++)
            {
                if (occupants[i] == kind)
                    return i;
            }

            return -1;
        }

        static int LastSeat(ReadOnlySpan<byte> occupants, byte kind)
        {
            for (var i = occupants.Length - 1; i >= 0; i--)
            {
                if (occupants[i] == kind)
                    return i;
            }

            return -1;
        }

        // ---------------------------------------------------------------- Walking

        /// <summary>The floor spot behind a seat where a cat hops up from and down to.</summary>
        public static Vector3 StandPoint(Transform seatAnchor)
        {
            var point = seatAnchor.position - seatAnchor.forward * StandBack;
            point.y = 0f;
            return NavMesh.SamplePosition(point, out var hit, 1.5f, NavMesh.AllAreas) ? hit.position : point;
        }

        /// <summary>Seconds from the entrance to sitting on this seat, or 0 when there's no entrance to walk from.</summary>
        public static float ArrivalSeconds(Transform seatAnchor)
        {
            if (!CasinoDoor.Exists)
                return 0f;
            var path = new List<Vector3>();
            return TryPath(CasinoDoor.Inside, StandPoint(seatAnchor), path) ? Length(path) / WalkSpeed + HopSeconds : 0f;
        }

        /// <summary>A NavMesh path from one point to another (a straight line if there's none).</summary>
        public static bool TryPath(Vector3 from, Vector3 to, List<Vector3> corners)
        {
            corners.Clear();
            _path ??= new NavMeshPath();
            if (NavMesh.SamplePosition(from, out var fromHit, 1.5f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(to, out var toHit, 1.5f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, _path) &&
                _path.status == NavMeshPathStatus.PathComplete)
            {
                corners.AddRange(_path.corners);
                return true;
            }

            corners.Add(from);
            corners.Add(to);
            return false;
        }

        public static float Length(List<Vector3> corners)
        {
            var length = 0f;
            for (var i = 1; i < corners.Count; i++)
                length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }

        /// <summary>The point <paramref name="distance"/> metres along a path, and the direction of travel there.</summary>
        public static Vector3 Sample(List<Vector3> corners, float distance, out Vector3 direction)
        {
            direction = Vector3.forward;
            if (corners.Count == 0)
                return Vector3.zero;

            for (var i = 1; i < corners.Count; i++)
            {
                var segment = corners[i] - corners[i - 1];
                var length = segment.magnitude;
                if (length < 0.0001f)
                    continue;
                direction = segment / length;
                if (distance <= length)
                    return corners[i - 1] + direction * Mathf.Max(0f, distance);
                distance -= length;
            }

            return corners[^1];
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _path = null;
    }
}
