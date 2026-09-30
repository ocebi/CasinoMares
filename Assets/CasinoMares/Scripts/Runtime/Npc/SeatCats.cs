using System;
using System.Collections.Generic;
using CasinoMares.Player;
using UnityEngine;
using UnityEngine.AI;

namespace CasinoMares.Npc
{
    /// <summary>
    /// Local bodies for the NPC players at one table (the table views own one each). An arriving cat walks in from
    /// the entrance, hops onto its stool and sits; its place on the path comes from the table's networked arrival
    /// timer, so every client shows it in the same spot. A cat whose seat empties stands up, hops down and walks
    /// out. Without an entrance in the scene, cats simply appear and vanish in their seats.
    /// </summary>
    public class SeatCats
    {
        const float StandUpSeconds = 0.6f;
        const float HopHeight = 0.22f;
        const float TurnSpeed = 420f;
        /// <summary>Metres per second a walking cat moves up or down, e.g. hopping onto a table on its way.</summary>
        const float ClimbSpeed = 2.5f;

        class Walker
        {
            public NpcCatVisual Cat;
            public Transform Anchor;
            public int Fur;
            public readonly List<Vector3> Path = new();
            public float PathLength;
            public bool LeavingFromSeat;
            public float LeavingTime;
        }

        readonly NpcCatVisual _prefab;
        readonly Transform _parent;
        readonly Func<int, Transform> _anchorOf;
        readonly Walker[] _seats;
        readonly List<Walker> _leaving = new();
        readonly Stack<NpcCatVisual> _pool = new();

        public SeatCats(NpcCatVisual prefab, Transform parent, int seatCount, Func<int, Transform> anchorOf)
        {
            _prefab = prefab;
            _parent = parent;
            _anchorOf = anchorOf;
            _seats = new Walker[seatCount];
        }

        /// <summary>The seated NPC's reactions, or null (empty seat, or the cat is still on its way).</summary>
        public CatReactions ReactionsAt(int seat)
        {
            var walker = _seats[seat];
            return walker != null && walker.Cat.Sitting ? walker.Cat.Reactions : null;
        }

        /// <summary>
        /// Call every frame for every seat. <paramref name="arrival"/> is the seconds until the NPC sits down
        /// (0 once it's seated, or when it was already there).
        /// </summary>
        public void UpdateSeat(int seat, bool npc, int fur, float arrival)
        {
            var walker = _seats[seat];
            if (walker != null && (!npc || walker.Fur != fur))
            {
                StartLeaving(walker);
                _seats[seat] = walker = null;
            }

            if (!npc)
                return;

            if (walker == null)
            {
                walker = new Walker { Cat = Take(), Anchor = _anchorOf(seat), Fur = fur };
                walker.Cat.SetFur(fur);
                if (arrival > 0f && CasinoDoor.Exists)
                {
                    NpcSeating.TryPath(CasinoDoor.Inside, NpcSeating.StandPoint(walker.Anchor), walker.Path);
                    walker.PathLength = NpcSeating.Length(walker.Path);
                    var start = NpcSeating.Sample(walker.Path, 0f, out var direction);
                    walker.Cat.transform.SetPositionAndRotation(start, Flat(direction, walker.Anchor.forward));
                }

                walker.Cat.gameObject.SetActive(true);
                _seats[seat] = walker;
            }

            PlaceArriving(walker, arrival);
        }

        /// <summary>Call once a frame after the seats: moves the cats that are leaving.</summary>
        public void Tick()
        {
            for (var i = _leaving.Count - 1; i >= 0; i--)
            {
                if (!UpdateLeaving(_leaving[i], Time.deltaTime))
                    continue;
                Release(_leaving[i].Cat);
                _leaving.RemoveAt(i);
            }
        }

        void PlaceArriving(Walker walker, float remaining)
        {
            var cat = walker.Cat;
            var anchor = walker.Anchor;
            if (remaining <= 0f || walker.Path.Count == 0)
            {
                cat.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
                cat.Sitting = true;
                return;
            }

            cat.Sitting = false;
            if (remaining > NpcSeating.HopSeconds)
            {
                var distance = walker.PathLength - (remaining - NpcSeating.HopSeconds) * NpcSeating.WalkSpeed;
                Walk(cat, walker.Path, distance);
                return;
            }

            Hop(cat, walker.Path[^1], anchor, 1f - remaining / NpcSeating.HopSeconds, anchor.rotation);
        }

        void StartLeaving(Walker walker)
        {
            if (!CasinoDoor.Exists)
            {
                Release(walker.Cat);
                return;
            }

            walker.LeavingFromSeat = walker.Cat.Sitting;
            walker.LeavingTime = 0f;
            walker.Cat.Sitting = false;
            var from = walker.LeavingFromSeat ? NpcSeating.StandPoint(walker.Anchor) : walker.Cat.transform.position;
            NpcSeating.TryPath(from, CasinoDoor.Inside, walker.Path);
            walker.PathLength = NpcSeating.Length(walker.Path);
            _leaving.Add(walker);
        }

        /// <summary>Stand up, hop down, walk to the entrance. True once the cat is out of the door.</summary>
        bool UpdateLeaving(Walker walker, float deltaTime)
        {
            walker.LeavingTime += deltaTime;
            var time = walker.LeavingTime;
            var cat = walker.Cat;
            if (walker.LeavingFromSeat)
            {
                if (time < StandUpSeconds)
                    return false;
                time -= StandUpSeconds;
                if (time < NpcSeating.HopSeconds)
                {
                    var away = Flat(walker.Path.Count > 1 ? walker.Path[1] - walker.Path[0] : -walker.Anchor.forward, -walker.Anchor.forward);
                    Hop(cat, walker.Anchor.position, walker.Path[0], time / NpcSeating.HopSeconds, away);
                    return false;
                }

                time -= NpcSeating.HopSeconds;
            }

            var distance = time * NpcSeating.WalkSpeed;
            Walk(cat, walker.Path, distance);
            return distance >= walker.PathLength;
        }

        /// <summary>A yaw-only rotation facing along <paramref name="direction"/>.</summary>
        static Quaternion Flat(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                direction = new Vector3(fallback.x, 0f, fallback.z);
            return direction.sqrMagnitude < 0.0001f ? Quaternion.identity : Quaternion.LookRotation(direction, Vector3.up);
        }

        static void Walk(NpcCatVisual cat, List<Vector3> path, float distance)
        {
            var position = NpcSeating.Sample(path, distance, out var direction);
            // Path corners only mark turns, so follow the NavMesh up and down in between: cats hop up and stroll
            // across a table top on the way, like the patrons do.
            if (NavMesh.SamplePosition(position, out var hit, 0.8f, NavMesh.AllAreas))
                position.y = hit.position.y;
            var current = cat.transform.position.y;
            if (cat.gameObject.activeSelf && Mathf.Abs(position.y - current) < 1f)
                position.y = Mathf.MoveTowards(current, position.y, ClimbSpeed * Time.deltaTime);
            direction.y = 0f;
            var rotation = cat.transform.rotation;
            if (direction.sqrMagnitude > 0.0001f)
                rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(direction, Vector3.up), TurnSpeed * Time.deltaTime);
            cat.transform.SetPositionAndRotation(position, rotation);
        }

        static void Hop(NpcCatVisual cat, Vector3 from, Transform to, float t, Quaternion facing) =>
            Hop(cat, from, to.position, t, facing);

        static void Hop(NpcCatVisual cat, Vector3 from, Vector3 to, float t, Quaternion facing)
        {
            t = Mathf.Clamp01(t);
            var position = Vector3.Lerp(from, to, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * HopHeight);
            var rotation = Quaternion.Slerp(cat.transform.rotation, facing, 1f - Mathf.Exp(-12f * Time.deltaTime));
            cat.transform.SetPositionAndRotation(position, rotation);
        }

        NpcCatVisual Take()
        {
            while (_pool.Count > 0)
            {
                var pooled = _pool.Pop();
                if (pooled != null)
                    return pooled;
            }

            var cat = UnityEngine.Object.Instantiate(_prefab, _parent);
            cat.name = "SeatNpc";
            cat.gameObject.SetActive(false);
            return cat;
        }

        void Release(NpcCatVisual cat)
        {
            if (cat == null)
                return;
            cat.Sitting = false;
            cat.gameObject.SetActive(false);
            _pool.Push(cat);
        }
    }
}
