using System.Collections.Generic;
using CasinoMares.Audio;
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
    /// over to watch a player on a winning streak. A player who paws it gets hissed at, and the cat trots off to
    /// somewhere on the far side of them.
    /// </summary>
    public class PatronBrain : NetworkBehaviour, IStateAuthorityChanged
    {
        const float ArriveDistance = 0.15f;
        const float BigWinReactRange = 6f;
        /// <summary>How close a pawing player must really be (the paw reaches about a metre; this allows for lag).</summary>
        const float PawRange = 1.8f;
        /// <summary>Seconds before the hiss: the walk has to settle into a stand, and a sitting cat has to get up.</summary>
        const float HissDelayStanding = 0.35f;
        const float HissDelaySitting = 0.8f;
        /// <summary>Seconds from the hiss to trotting off, about the length of the hiss animation.</summary>
        const float HissDuration = 1.2f;
        /// <summary>Seconds a pawed cat ignores more paws and winning streaks.</summary>
        const float SpookedSeconds = 20f;
        const float FleeMinDistance = 4f;
        /// <summary>Metres per second a walking cat's height follows the NavMesh (up a step, down off a seat).</summary>
        const float StepSpeed = 2f;
        /// <summary>Another cat this close to a spot (across the floor) is on it.</summary>
        const float SpotTakenDistance = 0.45f;

        public static readonly List<PatronBrain> All = new();

        [SerializeField] NpcCatVisual _visual;
        [SerializeField] float _walkSpeed = 1.1f;
        [SerializeField] float _fleeSpeed = 2.2f;
        [SerializeField] float _turnSpeed = 360f;

        [Networked] public NetworkBool Sitting { get; set; }
        [Networked] public byte Fur { get; set; }
        [Networked] NetworkBool Initialized { get; set; }
        [Networked] TickTimer WaitTimer { get; set; }
        [Networked] byte EmoteId { get; set; }
        [Networked] byte EmoteCount { get; set; }
        [Networked] TickTimer EmoteTimer { get; set; }
        [Networked] TickTimer CheerTimer { get; set; }
        [Networked] TickTimer SpookTimer { get; set; }
        [Networked] TickTimer HissTimer { get; set; }
        [Networked] TickTimer FleeTimer { get; set; }
        [Networked] Vector3 SpookedBy { get; set; }
        [Networked] byte HissCount { get; set; }

        static readonly CatEmote[] IdleEmotes = { CatEmote.Meow, CatEmote.Groom, CatEmote.Wiggle, CatEmote.Purr, CatEmote.Pounce };

        readonly List<Vector3> _path = new();
        NavMeshPath _navPath;
        PatronSpot _target;
        Vector3 _simPosition;
        int _pathIndex;
        int _seenEmoteCount = -1;
        int _seenHissCount = -1;
        bool _watching;
        bool _fleeing;
        /// <summary>This client has been simulating the cat since it took authority (see <see cref="TakeOver"/>).</summary>
        bool _simulating;
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
                _simulating = true;
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
            // Another client simulates the cat from now on, or this one has just taken over (TakeOver, next tick).
            _simulating = false;
            _path.Clear();
            _watching = false;
            _fleeing = false;
            ReleaseTarget();
        }

        /// <summary>
        /// This client just became the master (the last one left, or handed over from a background tab) and carries
        /// on with the cat from where it is. Which cat had claimed which spot lived on the old master only, so a cat
        /// sitting on a spot claims it again here and stays for the rest of its time. Otherwise the others would see
        /// the seat as free and sit down on top of it.
        /// </summary>
        void TakeOver()
        {
            _simulating = true;
            _simPosition = transform.position;
            _path.Clear();
            _watching = false;
            _fleeing = false;
            ReleaseTarget();

            var spot = SpotAt(transform.position);
            if (spot != null && spot.ClaimedBy == null && !spot.TakenByPlayer)
            {
                _target = spot;
                spot.ClaimedBy = this;
                if (WaitTimer.ExpiredOrNotRunning(Runner))
                    WaitTimer = TickTimer.CreateFromSeconds(Runner, Random.Range(1f, 4f));
                return;
            }

            // Walking somewhere (the path stayed behind with the old master), or a seat someone else has.
            Sitting = false;
            WaitTimer = TickTimer.CreateFromSeconds(Runner, Random.Range(0.5f, 1.5f));
        }

        /// <summary>The spot this cat is on, if any.</summary>
        static PatronSpot SpotAt(Vector3 position)
        {
            foreach (var spot in PatronSpot.All)
            {
                var offset = spot.transform.position - position;
                if (Mathf.Abs(offset.y) < 0.5f && new Vector2(offset.x, offset.z).sqrMagnitude < 0.3f * 0.3f)
                    return spot;
            }

            return null;
        }

        /// <summary>
        /// Somebody else has this spot: a cat claimed it, a player sits there, or another cat is on it right now
        /// (whatever the claims say).
        /// </summary>
        bool SpotTaken(PatronSpot spot)
        {
            if ((spot.ClaimedBy != null && spot.ClaimedBy != this) || spot.TakenByPlayer)
                return true;
            var position = spot.transform.position;
            foreach (var other in All)
            {
                if (other == this || other == null)
                    continue;
                var offset = other.transform.position - position;
                if (Mathf.Abs(offset.y) < 0.5f && new Vector2(offset.x, offset.z).sqrMagnitude < SpotTakenDistance * SpotTakenDistance)
                    return true;
            }

            return false;
        }

        /// <summary>True for a while after a player pawed this cat: it won't come over to watch anyone.</summary>
        public bool IsSpooked => Object != null && Object.IsValid && SpookTimer.IsRunning && !SpookTimer.Expired(Runner);

        /// <summary>The patron within paw reach in front of a cat, or null.</summary>
        public static PatronBrain FindInReach(Transform cat, float reach, float maxAngle)
        {
            PatronBrain best = null;
            var bestDistance = float.MaxValue;
            foreach (var patron in All)
            {
                if (patron == null || !PawSwipe.InReach(cat, patron.transform.position + Vector3.up * 0.2f, reach, maxAngle, out var distance))
                    continue;
                if (distance < bestDistance)
                {
                    best = patron;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>A player pawed this cat: it stops what it's doing, turns to hiss at them, then trots off.</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Pawed(RpcInfo info = default)
        {
            var cat = PlayerCat.Find(info.Source);
            if (cat == null || IsSpooked || (cat.transform.position - transform.position).sqrMagnitude > PawRange * PawRange)
                return;

            WinnerCrowd.Release(this);
            _watching = false;
            _fleeing = false;
            ReleaseTarget();
            _path.Clear();
            WaitTimer = TickTimer.None;
            EmoteTimer = TickTimer.None;
            CheerTimer = TickTimer.None;

            var hissDelay = Sitting ? HissDelaySitting : HissDelayStanding;
            Sitting = false;
            SpookedBy = cat.transform.position;
            SpookTimer = TickTimer.CreateFromSeconds(Runner, SpookedSeconds);
            HissTimer = TickTimer.CreateFromSeconds(Runner, hissDelay);
            FleeTimer = TickTimer.CreateFromSeconds(Runner, hissDelay + HissDuration);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;
            if (!_simulating)
                TakeOver();

            WinnerCrowd.Tick(Runner);
            if (FleeTimer.IsRunning)
            {
                FaceTowards(SpookedBy, Runner.DeltaTime);
                if (HissTimer.Expired(Runner))
                {
                    HissTimer = TickTimer.None;
                    HissCount++;
                }

                if (FleeTimer.Expired(Runner))
                {
                    FleeTimer = TickTimer.None;
                    Flee();
                }

                return;
            }

            // A player sat down where this cat was sitting (or heading): make room.
            if (_target != null && _target.TakenByPlayer)
            {
                ReleaseTarget();
                _path.Clear();
                Sitting = false;
                WaitTimer = TickTimer.None;
            }

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

            if (_seenHissCount < 0)
                _seenHissCount = HissCount;
            if (HissCount != _seenHissCount)
            {
                _seenHissCount = HissCount;
                // The hiss emote needs a cat standing still; one that's still turning swats back instead.
                var reactions = _visual.Reactions;
                if (reactions == null || !reactions.PlayEmote(CatEmote.Hiss))
                {
                    reactions?.PlayAngry();
                    Sfx.PlayAt(SoundId.Hiss, transform.position + Vector3.up * 0.3f);
                }
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
            if (!TryPathTo(spot))
            {
                _path.Clear();
                _path.Add(_simPosition);
                _path.Add(spot);
                _pathIndex = 1;
            }
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

        /// <summary>Trot to a free spot on the far side from whoever pawed us, or at least a few metres away.</summary>
        void Flee()
        {
            var away = transform.position - SpookedBy;
            away.y = 0f;
            away = away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward;

            // Best first: straight away from the player, and not across the whole room.
            PatronSpot best = null;
            var bestScore = float.MinValue;
            foreach (var spot in PatronSpot.All)
            {
                if (SpotTaken(spot))
                    continue;
                var offset = spot.transform.position - transform.position;
                offset.y = 0f;
                var distance = offset.magnitude;
                if (distance < FleeMinDistance)
                    continue;
                var score = Vector3.Dot(offset / distance, away) - distance * 0.02f;
                if (score > 0f && score > bestScore && TryPathTo(spot.transform.position))
                {
                    best = spot;
                    bestScore = score;
                }
            }

            if (best != null && TryPathTo(best.transform.position))
            {
                _target = best;
                best.ClaimedBy = this;
            }
            else if (!NavMesh.SamplePosition(transform.position + away * FleeMinDistance, out var hit, 2f, NavMesh.AllAreas) ||
                     !TryPathTo(hit.position))
            {
                WaitTimer = TickTimer.CreateFromSeconds(Runner, 1f);
                return;
            }

            _fleeing = true;
        }

        /// <summary>
        /// Sets the walking path to <paramref name="destination"/> if the NavMesh gets there, or else to the NavMesh
        /// right next to it (a chair is off the NavMesh; <see cref="Arrive"/> puts a sitting cat onto it).
        /// </summary>
        bool TryPathTo(Vector3 destination)
        {
            if (!PathTo(destination) &&
                (!NavMesh.SamplePosition(destination, out var near, 1.2f, NavMesh.AllAreas) || !PathTo(near.position)))
                return false;
            _path.Clear();
            _path.AddRange(_navPath.corners);
            _pathIndex = 1;
            return true;
        }

        bool PathTo(Vector3 destination) =>
            NavMesh.CalculatePath(_simPosition, destination, NavMesh.AllAreas, _navPath) && _navPath.status == NavMeshPathStatus.PathComplete;

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
                if (SpotTaken(spot) || !TryPathTo(spot.transform.position))
                    continue;

                _target = spot;
                spot.ClaimedBy = this;
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
            var step = (_fleeing ? _fleeSpeed : _walkSpeed) * deltaTime;

            if (toWaypoint.magnitude <= Mathf.Max(step, ArriveDistance))
            {
                _simPosition = new Vector3(waypoint.x, _simPosition.y, waypoint.z);
                _pathIndex++;
            }
            else
            {
                _simPosition += toWaypoint.normalized * step;
            }

            // Follow the NavMesh's height, easing down off a chair (which is kept off the NavMesh) rather than
            // walking away at seat height.
            if (NavMesh.SamplePosition(_simPosition, out var hit, 1f, NavMesh.AllAreas))
                _simPosition.y = Mathf.MoveTowards(_simPosition.y, hit.position.y, StepSpeed * deltaTime);

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
            _fleeing = false;
            EmoteTimer = default;
            if (_watching)
                return;
            if (_target != null && _target.Sit && SpotTaken(_target))
            {
                // Someone got there first: find somewhere else in a moment rather than sitting in their lap.
                ReleaseTarget();
                WaitTimer = TickTimer.CreateFromSeconds(Runner, Random.Range(0.5f, 1.5f));
                return;
            }

            if (_target != null)
            {
                // A seat is an exact place: settle onto it, not wherever the NavMesh's rough heights near it ended.
                if (_target.Sit)
                    transform.position = _simPosition = _target.transform.position;
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
