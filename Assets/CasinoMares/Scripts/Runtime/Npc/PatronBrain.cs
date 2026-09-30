using System.Collections.Generic;
using CasinoMares.Blackjack;
using CasinoMares.Player;
using Fusion;
using UnityEngine;
using UnityEngine.AI;

namespace CasinoMares.Npc
{
    /// <summary>
    /// An ambient patron cat (GDD §4.4). Networked scene object simulated by the master client: it walks NavMesh
    /// paths between <see cref="PatronSpot"/>s inside FixedUpdateNetwork, idles or sits there, and every client
    /// shows the same cat in the same place. Nearby big wins make it hop, and <see cref="WinnerCrowd"/> can send it
    /// over to watch a player on a winning streak.
    /// </summary>
    public class PatronBrain : NetworkBehaviour, IStateAuthorityChanged
    {
        const float ArriveDistance = 0.15f;
        const float BigWinReactRange = 6f;

        public static readonly List<PatronBrain> All = new();

        [SerializeField] NpcCatVisual _visual;
        [SerializeField] float _walkSpeed = 1.1f;
        [SerializeField] float _turnSpeed = 360f;

        [Networked] public NetworkBool Sitting { get; set; }
        [Networked] public byte Fur { get; set; }
        [Networked] NetworkBool Initialized { get; set; }
        [Networked] TickTimer WaitTimer { get; set; }
        [Networked] byte EmoteId { get; set; }
        [Networked] byte EmoteCount { get; set; }
        [Networked] TickTimer EmoteTimer { get; set; }
        [Networked] TickTimer CheerTimer { get; set; }

        static readonly CatEmote[] IdleEmotes = { CatEmote.Meow, CatEmote.Groom, CatEmote.Wiggle, CatEmote.Purr, CatEmote.Pounce };

        readonly List<Vector3> _path = new();
        NavMeshPath _navPath;
        PatronSpot _target;
        Vector3 _simPosition;
        int _pathIndex;
        int _seenEmoteCount = -1;
        bool _watching;
        Vector3 _watchSpot;
        Vector3 _watchFocus;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        public override void Spawned()
        {
            All.Add(this);
            _navPath = new NavMeshPath();
            _simPosition = transform.position;
            if (HasStateAuthority && !Initialized)
            {
                Initialized = true;
                // Start on the NavMesh, or no path from here would ever work.
                if (NavMesh.SamplePosition(_simPosition, out var hit, 2f, NavMesh.AllAreas))
                    transform.position = _simPosition = hit.position;
                Fur = (byte)Random.Range(0, 6);
                WaitTimer = TickTimer.CreateFromSeconds(Runner, Random.Range(0.5f, 4f));
            }

            BlackjackTable.BigWin += OnBigWin;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            BlackjackTable.BigWin -= OnBigWin;
            ReleaseTarget();
        }

        void IStateAuthorityChanged.StateAuthorityChanged()
        {
            // The master client left and we inherited this cat: carry on from where it is.
            _simPosition = transform.position;
            _path.Clear();
            _watching = false;
            ReleaseTarget();
            if (HasStateAuthority)
                WaitTimer = TickTimer.CreateFromSeconds(Runner, 1f);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            WinnerCrowd.Tick(Runner);
            if (CheerTimer.IsRunning && CheerTimer.Expired(Runner))
            {
                CheerTimer = TickTimer.None;
                EmoteId = (byte)(Random.value < 0.7f ? CatEmote.Happy : CatEmote.Meow);
                EmoteCount++;
            }

            if (_path.Count > 0)
            {
                Walk(Runner.DeltaTime);
                return;
            }

            if (_watching)
            {
                FaceTowards(_watchFocus, Runner.DeltaTime);
                MaybeEmote();
                return;
            }

            if (!WaitTimer.ExpiredOrNotRunning(Runner))
            {
                MaybeEmote();
                return;
            }

            Sitting = false;
            PickNextSpot();
        }

        public override void Render()
        {
            if (_visual == null)
                return;
            _visual.SetFur(Fur);
            _visual.Sitting = Sitting;

            // Play emotes that happened while we watch (a late joiner doesn't replay the last one).
            if (_seenEmoteCount < 0)
                _seenEmoteCount = EmoteCount;
            if (EmoteCount != _seenEmoteCount)
            {
                _seenEmoteCount = EmoteCount;
                _visual.Reactions?.PlayEmote((CatEmote)EmoteId);
            }
        }

        // ---------------------------------------------------------------- Watching a winner (master client)

        /// <summary>Walk over to <paramref name="spot"/> and watch the player seated at <paramref name="focus"/>.</summary>
        public void Watch(Vector3 spot, Vector3 focus)
        {
            _watchFocus = focus;
            if (_watching && (spot - _watchSpot).sqrMagnitude < 0.01f)
                return;

            _watching = true;
            _watchSpot = spot;
            ReleaseTarget();
            Sitting = false;
            WaitTimer = TickTimer.None;
            EmoteTimer = TickTimer.None;
            _path.Clear();
            if (NavMesh.CalculatePath(_simPosition, spot, NavMesh.AllAreas, _navPath) && _navPath.status == NavMeshPathStatus.PathComplete)
            {
                _path.AddRange(_navPath.corners);
            }
            else
            {
                _path.Add(_simPosition);
                _path.Add(spot);
            }

            _pathIndex = 1;
        }

        /// <summary>The streak is over (or this cat isn't needed any more): wander off after a moment.</summary>
        public void StopWatching()
        {
            if (!_watching)
                return;
            _watching = false;
            _path.Clear();
            WaitTimer = TickTimer.CreateFromSeconds(Runner, Random.Range(1f, 3.5f));
        }

        /// <summary>The watched player just won a round: hop and meow, a beat apart from the rest of the crowd.</summary>
        public void Cheer()
        {
            if (!CheerTimer.IsRunning)
                CheerTimer = TickTimer.CreateFromSeconds(Runner, Random.Range(0.15f, 0.8f));
        }

        void FaceTowards(Vector3 point, float deltaTime)
        {
            var direction = point - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                return;
            var target = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, _turnSpeed * deltaTime);
        }

        /// <summary>Standing patrons sometimes meow, groom or wiggle while they hang around.</summary>
        void MaybeEmote()
        {
            if (Sitting)
                return;
            if (!EmoteTimer.IsRunning)
            {
                EmoteTimer = TickTimer.CreateFromSeconds(Runner, Random.Range(4f, 14f));
                return;
            }

            if (!EmoteTimer.Expired(Runner))
                return;
            EmoteTimer = TickTimer.CreateFromSeconds(Runner, Random.Range(8f, 20f));
            EmoteId = (byte)IdleEmotes[Random.Range(0, IdleEmotes.Length)];
            EmoteCount++;
        }

        void PickNextSpot()
        {
            ReleaseTarget();

            var candidates = PatronSpot.All;
            if (candidates.Count == 0)
            {
                WaitTimer = TickTimer.CreateFromSeconds(Runner, 5f);
                return;
            }

            for (var attempt = 0; attempt < 6; attempt++)
            {
                var spot = candidates[Random.Range(0, candidates.Count)];
                if (spot.ClaimedBy != null)
                    continue;

                if (!NavMesh.CalculatePath(_simPosition, spot.transform.position, NavMesh.AllAreas, _navPath) ||
                    _navPath.status != NavMeshPathStatus.PathComplete)
                    continue;

                _target = spot;
                spot.ClaimedBy = this;
                _path.Clear();
                _path.AddRange(_navPath.corners);
                _pathIndex = 1;
                return;
            }

            WaitTimer = TickTimer.CreateFromSeconds(Runner, 2f);
        }

        void Walk(float deltaTime)
        {
            if (_pathIndex >= _path.Count)
            {
                Arrive();
                return;
            }

            var waypoint = _path[_pathIndex];
            var toWaypoint = waypoint - _simPosition;
            toWaypoint.y = 0f;
            var step = _walkSpeed * deltaTime;

            if (toWaypoint.magnitude <= Mathf.Max(step, ArriveDistance))
            {
                _simPosition = new Vector3(waypoint.x, _simPosition.y, waypoint.z);
                _pathIndex++;
            }
            else
            {
                _simPosition += toWaypoint.normalized * step;
            }

            // Stay glued to the NavMesh height.
            if (NavMesh.SamplePosition(_simPosition, out var hit, 0.5f, NavMesh.AllAreas))
                _simPosition.y = hit.position.y;

            transform.position = _simPosition;
            if (toWaypoint.sqrMagnitude > 0.0001f)
            {
                var targetRotation = Quaternion.LookRotation(toWaypoint.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, _turnSpeed * deltaTime);
            }
        }

        void Arrive()
        {
            _path.Clear();
            EmoteTimer = default;
            if (_watching)
                return;
            if (_target != null)
            {
                transform.rotation = _target.transform.rotation;
                Sitting = _target.Sit;
                WaitTimer = TickTimer.CreateFromSeconds(Runner, Random.Range(_target.StayRange.x, _target.StayRange.y));
            }
            else
            {
                WaitTimer = TickTimer.CreateFromSeconds(Runner, 3f);
            }
        }

        void ReleaseTarget()
        {
            if (_target != null && _target.ClaimedBy == this)
                _target.ClaimedBy = null;
            _target = null;
        }

        void OnBigWin(Vector3 tablePosition)
        {
            if (_visual == null || (tablePosition - transform.position).sqrMagnitude > BigWinReactRange * BigWinReactRange)
                return;
            _visual.Reactions?.PlayHappy(2, 0.2f + CatReactions.StaggerDelay());
        }
    }
}
