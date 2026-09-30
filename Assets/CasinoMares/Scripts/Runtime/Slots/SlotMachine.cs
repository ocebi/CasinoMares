using System;
using System.Collections.Generic;
using System.Text;
using CasinoMares.Core;
using CasinoMares.Rules;
using Fusion;
using Unity.Cinemachine;
using UnityEngine;

namespace CasinoMares.Slots
{
    /// <summary>
    /// One slot machine (GDD §13.3): a single-seat game table. The seated player asks for a spin, the authority
    /// rolls the weighted reels, and everyone nearby watches the same reels stop. Chips are settled by the
    /// player's own client like every other game.
    /// </summary>
    public class SlotMachine : NetworkBehaviour, IGameTable, IPlayerLeft
    {
        public const float SpinDuration = 2.4f;

        public static readonly List<SlotMachine> All = new();

        /// <summary>Raised on the local client when its spin paid out. Arg: net chips (can be negative).</summary>
        public static event Action<SlotMachine, int> LocalSpinResult;

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

        int _chargedSpin;
        int _paidSpin;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            LocalSpinResult = null;
        }

        public string DisplayName => _displayName;

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
        public int LastPayout => SpinId == 0 ? 0 : SlotRules.Payout(Bet, Reels[0], Reels[1], Reels[2]);
        public float SpinProgress => IsSpinning && Runner != null ? 1f - Mathf.Clamp01((SpinTimer.RemainingTime(Runner) ?? 0f) / SpinDuration) : 1f;

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

            text.AppendLine(IsSpinning ? $"Spinning for {Bet:N0}..." : $"{SlotRules.Names[Reels[0]]} · {SlotRules.Names[Reels[1]]} · {SlotRules.Names[Reels[2]]}");
            if (!IsSpinning)
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

            for (var reel = 0; reel < SlotRules.Reels; reel++)
                Reels.Set(reel, (byte)SlotRules.SymbolForRoll(UnityEngine.Random.Range(0, SlotRules.WeightTotal)));
            Bet = bet;
            SpinOwner = info.Source;
            SpinId++;
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
                LocalSpinResult?.Invoke(this, payout - Bet);
            }
        }
    }
}
