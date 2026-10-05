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
    /// </summary>
    public class SlotMachine : NetworkBehaviour, IGameTable, IPlayerLeft
    {
        /// <summary>How long the reels spin.</summary>
        public const float ReelSpinDuration = 2.4f;
        /// <summary>Seconds into a spin when each reel stops (the third later when it teases).</summary>
        public static readonly float[] ReelStops = { 1.1f, 1.7f, 2.3f };
        /// <summary>How much longer the third reel takes when it slows down to tease (the first two match).</summary>
        public const float TeaseDuration = 1.4f;

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

        public bool IsSpinning => SpinId != SettledSpinId;
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

        /// <summary>Seconds since the current spin started; the whole duration once it's over.</summary>
        public float SpinElapsed
        {
            get
            {
                var duration = SpinDuration;
                if (!IsSpinning || Runner == null)
                    return duration;
                return Mathf.Clamp(duration - (SpinTimer.RemainingTime(Runner) ?? 0f), 0f, duration);
            }
        }

        /// <summary>Where the result label floats, over the machine.</summary>
        public Vector3 LabelPosition => transform.position + Vector3.up * (_kind == SlotKind.YarnTumble ? 1.85f : 1.55f);

        public override void Spawned()
        {
            All.Add(this);
            GameTables.All.Add(this);
            _chargedSpin = _paidSpin = SpinId;
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
            text.AppendLine(LastPayout > 0 ? $"Won {LastPayout:N0} on a {Bet:N0} bet" : $"Lost {Bet:N0}");
            return true;
        }

        // ---------------------------------------------------------------- Authority

        public override void FixedUpdateNetwork()
        {
            if (IsSpinning && SpinTimer.ExpiredOrNotRunning(Runner))
                SettledSpinId = SpinId;
        }

        void IPlayerLeft.PlayerLeft(PlayerRef player)
        {
            if (HasStateAuthority && Occupant == player)
                Occupant = PlayerRef.None;
        }

        public void RequestSeat() => RPC_Sit();
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
            if (IsSpinning || Occupant != info.Source || Array.IndexOf(SlotRules.BetOptions, bet) < 0)
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
                if (payout > 0)
                    ProfileStore.SetBalance(ProfileStore.Current.Balance + payout);
                var me = PlayerCat.Local;
                if (LastWin == SlotWin.Top && me != null)
                    me.Announce($"{me.DisplayName} won {payout:N0} chips on {_displayName}!");
                LocalSpinResult?.Invoke(this, payout - Bet);
            }
        }
    }
}
