using System;
using System.Collections.Generic;
using System.Text;
using CasinoMares.Core;
using CasinoMares.Player;
using CasinoMares.Rules;
using Fusion;
using Unity.Cinemachine;
using UnityEngine;

namespace CasinoMares.Slots
{
    public enum SlotKind
    {
        /// <summary>Three reels and one line (<see cref="SlotRules"/>).</summary>
        Reels,
        /// <summary>A 5×5 grid that cascades (<see cref="YarnTumbleRules"/>).</summary>
        YarnTumble,
    }

    /// <summary>
    /// One slot machine (GDD §13.3): a single-seat game table. The seated player asks for a spin, the authority
    /// rolls it (the reels, or for a Yarn Tumble a seed that every client plays out the same way), and everyone
    /// nearby watches the same result land. Chips are settled by the player's own client like every other game.
    /// <para>
    /// The seated player's client takes State Authority over its machine, so its own spins start the moment it
    /// asks, however slow the network is; everyone else watches a little behind. The master client holds the
    /// machine while nobody's seated, so seat requests go to someone who's there. Machines in scenes built before
    /// this (master client objects) stay with the master client throughout.
    /// </para>
    /// </summary>
    public class SlotMachine : NetworkBehaviour, IGameTable, IPlayerLeft
    {
        /// <summary>How long the reels spin.</summary>
        public const float ReelSpinDuration = 2.4f;
        /// <summary>Seconds into a spin when each reel stops (the third later when it teases).</summary>
        public static readonly float[] ReelStops = { 1.1f, 1.7f, 2.3f };
        /// <summary>How much longer the third reel takes when it slows down to tease (the first two match).</summary>
        public const float TeaseDuration = 1.4f;
        /// <summary>How often to ask again for State Authority while it hasn't come.</summary>
        const float AuthorityRetry = 0.5f;
        /// <summary>
        /// How long the master client waits after a machine empties before taking it back from the player who sat
        /// there, so a seat that player has just given to someone else has time to show up first.
        /// </summary>
        const float ReclaimDelay = 1f;

        public static readonly List<SlotMachine> All = new();

        /// <summary>Raised on the local client when its spin paid out. Arg: net chips (can be negative).</summary>
        public static event Action<SlotMachine, int> LocalSpinResult;

        /// <summary>Raised on every client when a big or top win lands. Arg: the machine's position.</summary>
        public static event Action<Vector3> BigWin;

        [SerializeField] SlotKind _kind;
        [SerializeField] string _displayName = "Slot machine";
        [SerializeField] Transform _seatAnchor;
        [SerializeField] CinemachineCamera _seatCamera;

        [Networked] public PlayerRef Occupant { get; set; }
        [Networked] public PlayerRef SpinOwner { get; set; }
        [Networked] public int SpinId { get; set; }
        [Networked] public int SettledSpinId { get; set; }
        [Networked] public int Bet { get; set; }
        [Networked] TickTimer SpinTimer { get; set; }
        [Networked, Capacity(SlotRules.Reels)] NetworkArray<byte> Reels => default;
        /// <summary>A Yarn Tumble spin's seed.</summary>
        [Networked] int Seed { get; set; }

        int _chargedSpin;
        int _paidSpin;
        YarnTumbleRules.Spin _tumble;
        int _tumbleSpin;
        int _tumbleSeed;
        /// <summary>The spin this client is showing, and since when (see <see cref="ShownTime"/>).</summary>
        int _shownSpin;
        float _shownSince;
        float _nextAuthorityRequest;
        float _lastOccupied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            LocalSpinResult = null;
            BigWin = null;
        }

        public string DisplayName => _displayName;
        public SlotKind Kind => _kind;

        public static bool AnyLocalSpinInProgress()
        {
            foreach (var machine in All)
            {
                if (machine != null && machine.Object != null && machine.Object.IsValid && machine.IsSpinning &&
                    machine.SpinOwner == machine.Runner.LocalPlayer)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The current spin hasn't landed on this client yet: it's still playing out here (see
        /// <see cref="SpinElapsed"/>), or the authority hasn't settled it.
        /// </summary>
        public bool IsSpinning => Unsettled || ShownTime < SpinDuration;

        /// <summary>The authority hasn't settled the current spin yet.</summary>
        bool Unsettled => SpinId != SettledSpinId;

        public int ReelAt(int reel) => Reels[reel];

        public int LastPayout =>
            SpinId == 0 ? 0 :
            _kind == SlotKind.YarnTumble ? YarnTumbleRules.Payout(Bet, Tumble.Pay) :
            SlotRules.Payout(Bet, Reels[0], Reels[1], Reels[2]);

        public SlotWin LastWin => SlotRules.Tier(Bet, LastPayout);

        /// <summary>The first two reels match, so the third slows down before it stops.</summary>
        public bool Teases => _kind == SlotKind.Reels && SpinId != 0 && SlotRules.Teases(Reels[0], Reels[1]);

        /// <summary>The current Yarn Tumble spin (the last one once it's over), played out from its seed.</summary>
        public YarnTumbleRules.Spin Tumble
        {
            get
            {
                if (_tumble == null || _tumbleSpin != SpinId || _tumbleSeed != Seed)
                {
                    _tumbleSpin = SpinId;
                    _tumbleSeed = Seed;
                    _tumble = YarnTumbleRules.Play(Seed);
                }

                return _tumble;
            }
        }

        /// <summary>How long the current spin takes to land.</summary>
        public float SpinDuration => _kind == SlotKind.YarnTumble
            ? YarnTumbleTiming.Duration(Tumble.Cascades.Count)
            : ReelSpinDuration + (Teases ? TeaseDuration : 0f);

        /// <summary>
        /// How far into the current spin this client is showing it, up to its whole duration. Each client plays a
        /// spin from the start when it first hears of it, rather than from the authority's timer, so one that
        /// arrives late (a stalled connection) still plays out in full instead of jumping ahead to its result.
        /// </summary>
        public float SpinElapsed => Mathf.Clamp(ShownTime, 0f, SpinDuration);

        /// <summary>Seconds since this client first looked at the current spin; a new one starts showing then.</summary>
        float ShownTime
        {
            get
            {
                if (_shownSpin != SpinId)
                {
                    _shownSpin = SpinId;
                    _shownSince = Time.time;
                }

                return Time.time - _shownSince;
            }
        }

        /// <summary>Where the result label floats, over the machine.</summary>
        public Vector3 LabelPosition => transform.position + Vector3.up * (_kind == SlotKind.YarnTumble ? 1.85f : 1.55f);

        public override void Spawned()
        {
            All.Add(this);
            GameTables.All.Add(this);
            _chargedSpin = _paidSpin = SpinId;
            // A spin already under way when we arrive isn't played out here (the views show its result).
            _shownSpin = SpinId;
            _shownSince = float.NegativeInfinity;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            GameTables.All.Remove(this);
        }

        internal void RaiseBigWin() => BigWin?.Invoke(transform.position);

        // ---------------------------------------------------------------- IGameTable

        public bool TryGetSeatOf(PlayerRef player, out int seat)
        {
            seat = 0;
            return Object != null && Object.IsValid && player != PlayerRef.None && Occupant == player;
        }

        public Transform GetSeatAnchor(int seat) => _seatAnchor;
        public CinemachineCamera GetSeatCamera(int seat) => _seatCamera;
        public void RequestLeave(int seat) => RPC_Leave();

        public bool TryDescribePlayer(PlayerRef player, StringBuilder text)
        {
            if (!TryGetSeatOf(player, out _))
                return false;
            text.AppendLine(_displayName);
            if (SpinId == 0)
            {
                text.AppendLine("Hasn't spun yet");
                return true;
            }

            if (IsSpinning)
            {
                text.AppendLine($"Spinning for {Bet:N0}...");
                return true;
            }

            if (_kind == SlotKind.Reels)
                text.AppendLine($"{SlotRules.Names[Reels[0]]} · {SlotRules.Names[Reels[1]]} · {SlotRules.Names[Reels[2]]}");
            else if (Tumble.Cascades.Count > 1)
                text.AppendLine($"{Tumble.Cascades.Count} chains, up to ×{YarnTumbleRules.MultiplierText(Tumble.Cascades[^1].Multiplier)}");
            text.AppendLine(LastPayout == Bet ? $"Bet back ({Bet:N0})" : LastPayout > 0 ? $"Won {LastPayout:N0} on a {Bet:N0} bet" : $"Lost {Bet:N0}");
            return true;
        }

        // ---------------------------------------------------------------- Authority

        /// <summary>
        /// Players can take State Authority over this machine (built with AllowStateAuthorityOverride). A machine
        /// built as a master client object can't be taken, so it stays with the master client as before.
        /// </summary>
        bool PlayersTakeAuthority => (Object.Flags & NetworkObjectFlags.AllowStateAuthorityOverride) != 0;

        void Update()
        {
            if (Object == null || !Object.IsValid || !PlayersTakeAuthority)
                return;

            if (Occupant != PlayerRef.None)
                _lastOccupied = Time.time;

            // The seated player takes the machine, so RPC_Spin runs on its own client in the same frame. Until it
            // arrives, its spins go to whoever holds the machine now. It keeps the machine while its tab is hidden:
            // a spin settles on its tick timer whenever the tab next runs, and nobody else can use the seat anyway.
            if (Occupant == Runner.LocalPlayer)
            {
                if (!HasStateAuthority)
                    RequestAuthority();
                return;
            }

            if (!Runner.IsSharedModeMasterClient || HasStateAuthority)
                return;

            // The master client takes it back when the player who held it left (nobody holds it then), or stood up
            // and the seat has stayed empty for a moment. Seat requests then go to a client that's there and in
            // front, not to a player who walked off or whose tab is in the background.
            if (Object.StateAuthority == PlayerRef.None ||
                (Occupant == PlayerRef.None && Time.time - _lastOccupied >= ReclaimDelay))
                RequestAuthority();
        }

        void RequestAuthority()
        {
            // It takes a round trip to arrive; don't ask again every frame meanwhile.
            if (Time.time < _nextAuthorityRequest)
                return;
            _nextAuthorityRequest = Time.time + AuthorityRetry;
            Object.RequestStateAuthority();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            if (Unsettled && SpinTimer.ExpiredOrNotRunning(Runner))
                SettledSpinId = SpinId;

            // A seated player who held the machine and left the room took its authority along, so PlayerLeft below
            // found nobody to free the seat. The master client adopts the machine then (Update) and frees it here.
            if (Occupant != PlayerRef.None && Occupant != Runner.LocalPlayer && Runner.IsSharedModeMasterClient &&
                PlayersTakeAuthority && !Runner.IsPlayerCommitted(Occupant))
                Occupant = PlayerRef.None;
        }

        void IPlayerLeft.PlayerLeft(PlayerRef player)
        {
            if (HasStateAuthority && Occupant == player)
                Occupant = PlayerRef.None;
        }

        public void RequestSeat() => RPC_Sit();
        /// <summary>Runs straight away on this client when it holds the machine (the seated player, normally).</summary>
        public void Spin(int bet) => RPC_Spin(bet);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_Sit(RpcInfo info = default)
        {
            if (Occupant == PlayerRef.None && !GameTables.TryFindSeat(info.Source, out _, out _))
                Occupant = info.Source;
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_Leave(RpcInfo info = default)
        {
            if (Occupant == info.Source)
                Occupant = PlayerRef.None;
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_Spin(int bet, RpcInfo info = default)
        {
            if (Unsettled || Occupant != info.Source || Array.IndexOf(SlotRules.BetOptions, bet) < 0)
                return;

            if (_kind == SlotKind.YarnTumble)
            {
                Seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            }
            else
            {
                for (var reel = 0; reel < SlotRules.Reels; reel++)
                    Reels.Set(reel, (byte)SlotRules.SymbolForRoll(UnityEngine.Random.Range(0, SlotRules.WeightTotal)));
            }

            Bet = bet;
            SpinOwner = info.Source;
            SpinId++;
            // After the new reels or seed, which decide how long it takes.
            SpinTimer = TickTimer.CreateFromSeconds(Runner, SpinDuration);
        }

        // ---------------------------------------------------------------- Local wallet (GDD §9.4)

        public override void Render()
        {
            if (Runner == null || SpinOwner != Runner.LocalPlayer)
                return;

            if (_chargedSpin != SpinId)
            {
                _chargedSpin = SpinId;
                ProfileStore.SetBalance(ProfileStore.Current.Balance - Bet);
            }

            if (!IsSpinning && _paidSpin != SpinId)
            {
                _paidSpin = SpinId;
                var payout = LastPayout;
                ChipRewards.PayRound(payout);
                var me = PlayerCat.Local;
                if (LastWin == SlotWin.Top && me != null)
                    me.Announce($"{me.DisplayName} won {payout:N0} chips on {_displayName}!");
                LocalSpinResult?.Invoke(this, payout - Bet);
            }
        }
    }
}
