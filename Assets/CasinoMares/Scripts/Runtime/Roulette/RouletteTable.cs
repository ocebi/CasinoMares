using System;
using System.Collections.Generic;
using System.Text;
using CasinoMares.Core;
using CasinoMares.Npc;
using CasinoMares.Player;
using CasinoMares.Rules;
using Fusion;
using Unity.Cinemachine;
using UnityEngine;

namespace CasinoMares.Roulette
{
    public enum RoulettePhase : byte
    {
        WaitingForPlayers,
        Betting,
        Spinning,
        Result,
    }

    public enum RouletteOccupant : byte
    {
        Empty,
        Player,
        Npc,
    }

    /// <summary>
    /// A European roulette table (GDD §13.1). Everyone bets during one timed window, the wheel spins, one shared
    /// result. Same authority model as blackjack: master client runs the round, players bet through RPCs, each
    /// client settles its own chips.
    /// </summary>
    public class RouletteTable : NetworkBehaviour, IGameTable, IPlayerLeft
    {
        public const int SeatCount = 6;
        public const int Spots = RouletteRules.SpotCount;
        public const int HistoryLength = 12;

        public static readonly byte[] BetTimerOptions = { 15, 20, 30, 45 };
        /// <summary>From the croupier's push to the ball at rest; the result is called <see cref="GameTables.ResultPause"/> later.</summary>
        public const float SpinDuration = RouletteBallPath.Duration;
        public const float ResultDuration = 5f;

        public static readonly List<RouletteTable> All = new();

        /// <summary>Raised on the local client when its bets were paid. Arg: net chips won (can be negative).</summary>
        public static event Action<RouletteTable, int> LocalRoundResult;

        [SerializeField] string _displayName = "Roulette";
        [SerializeField] Transform[] _seatAnchors = Array.Empty<Transform>();
        [SerializeField] CinemachineCamera[] _seatCameras = Array.Empty<CinemachineCamera>();
        [SerializeField] CinemachineCamera _wheelCamera;

        [Networked] NetworkBool Initialized { get; set; }
        [Networked] public RoulettePhase Phase { get; set; }
        [Networked] TickTimer PhaseTimer { get; set; }
        [Networked] public int RoundId { get; set; }
        [Networked] public int SettledRoundId { get; set; }
        [Networked] public byte BetTimerOption { get; set; }
        [Networked] public byte NpcFill { get; set; }
        [Networked] NpcCrew Crew { get; set; }
        [Networked] sbyte PendingResult { get; set; }
        [Networked] public sbyte Result { get; set; }

        [Networked, Capacity(SeatCount)] NetworkArray<byte> SeatOccupants => default;
        [Networked, Capacity(SeatCount)] NetworkArray<PlayerRef> SeatPlayers => default;
        [Networked, Capacity(SeatCount)] NetworkArray<byte> SeatNpcFur => default;
        [Networked, Capacity(SeatCount)] NetworkArray<TickTimer> SeatArrivals => default;
        [Networked, Capacity(SeatCount)] NetworkArray<PlayerRef> SeatRoundOwners => default;
        [Networked, Capacity(SeatCount)] NetworkArray<NetworkBool> SeatDone => default;
        [Networked, Capacity(SeatCount * Spots)] NetworkArray<int> Bets => default;
        [Networked, Capacity(HistoryLength)] NetworkArray<sbyte> History => default;

        int _walletRound;
        int _walletCharged;
        int _walletPaidRound;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            LocalRoundResult = null;
        }

        // ---------------------------------------------------------------- Read API

        public string DisplayName => _displayName;
        public int BetTimerSeconds => BetTimerOptions[Mathf.Clamp(BetTimerOption, 0, BetTimerOptions.Length - 1)];
        public RouletteOccupant OccupantAt(int seat) => (RouletteOccupant)SeatOccupants[seat];
        public PlayerRef PlayerAt(int seat) => SeatPlayers[seat];
        public PlayerRef RoundOwnerAt(int seat) => SeatRoundOwners[seat];
        public int NpcFurAt(int seat) => SeatNpcFur[seat];

        /// <summary>Seconds until the NPC in this seat, still walking in from the entrance, sits down (0 once seated).</summary>
        public float NpcArrivalAt(int seat) => SeatArrivals[seat].IsRunning && Runner != null ? Mathf.Max(0f, SeatArrivals[seat].RemainingTime(Runner) ?? 0f) : 0f;

        /// <summary>An NPC sits here and has arrived, so it plays.</summary>
        bool NpcPresent(int seat) => OccupantAt(seat) == RouletteOccupant.Npc && SeatArrivals[seat].ExpiredOrNotRunning(Runner);
        public bool IsDoneAt(int seat) => SeatDone[seat];
        public int BetAt(int seat, int spot) => Bets[seat * Spots + spot];
        public Transform GetSeatAnchor(int seat) => _seatAnchors[seat];
        public CinemachineCamera GetSeatCamera(int seat) => _seatCameras[seat];

        /// <summary>Close-up of the wheel that seated players watch the spin through.</summary>
        public CinemachineCamera WheelCamera => _wheelCamera;

        /// <summary>Most recent results, newest first (-1 = empty).</summary>
        public int HistoryAt(int index) => History[index];

        public float TimeRemaining => PhaseTimer.IsRunning && Runner != null ? Mathf.Max(0f, PhaseTimer.RemainingTime(Runner) ?? 0f) : -1f;

        /// <summary>
        /// Seconds since the croupier spun the wheel. The spin phase lasts <see cref="GameTables.ResultPause"/>
        /// longer than the ball runs, so it rests in its pocket for a moment before the result is called.
        /// </summary>
        public float SpinElapsed => Phase == RoulettePhase.Spinning && TimeRemaining >= 0f ? Mathf.Max(0f, SpinDuration + GameTables.ResultPause - TimeRemaining) : 0f;

        /// <summary>The number the ball is heading for; the view needs it during the spin to land in the right pocket.</summary>
        public int LandingNumber => Phase == RoulettePhase.Spinning ? PendingResult : Result;

        public int TotalBetAt(int seat)
        {
            var total = 0;
            for (var spot = 0; spot < Spots; spot++)
                total += Bets[seat * Spots + spot];
            return total;
        }

        public int PayoutAt(int seat, int result)
        {
            var total = 0;
            for (var spot = 0; spot < Spots; spot++)
                total += RouletteRules.Payout(spot, Bets[seat * Spots + spot], result);
            return total;
        }

        public bool LocalHasActiveWager => _walletCharged > 0 && _walletPaidRound != _walletRound;

        public static bool AnyLocalActiveWager()
        {
            foreach (var table in All)
            {
                if (table != null && table.Object != null && table.LocalHasActiveWager)
                    return true;
            }

            return false;
        }

        // ---------------------------------------------------------------- Lifecycle

        public override void Spawned()
        {
            All.Add(this);
            GameTables.All.Add(this);
            if (HasStateAuthority && !Initialized)
            {
                Initialized = true;
                BetTimerOption = 1;
                NpcFill = NpcSeating.Auto;
                Result = -1;
                for (var i = 0; i < HistoryLength; i++)
                    History.Set(i, -1);
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            GameTables.All.Remove(this);
        }

        // ---------------------------------------------------------------- IGameTable

        public bool TryGetSeatOf(PlayerRef player, out int seat)
        {
            seat = -1;
            if (Object == null || !Object.IsValid || player == PlayerRef.None)
                return false;
            for (var i = 0; i < SeatCount; i++)
            {
                if (OccupantAt(i) == RouletteOccupant.Player && SeatPlayers[i] == player)
                {
                    seat = i;
                    return true;
                }
            }

            return false;
        }

        public void RequestLeave(int seat) => RPC_LeaveSeat((byte)seat);

        public bool TryDescribePlayer(PlayerRef player, StringBuilder text)
        {
            if (!TryGetSeatOf(player, out var seat))
                return false;

            text.AppendLine($"{_displayName} · seat {seat + 1}");
            var total = TotalBetAt(seat);
            if (total == 0)
            {
                text.AppendLine(Phase == RoulettePhase.Betting ? "Choosing bets..." : "No bets this spin");
            }
            else
            {
                text.AppendLine($"Total bet {total:N0} on:");
                var shown = 0;
                for (var spot = 0; spot < Spots && shown < 6; spot++)
                {
                    var amount = BetAt(seat, spot);
                    if (amount <= 0)
                        continue;
                    text.AppendLine($"  {RouletteRules.SpotLabel(spot)}: {amount:N0}");
                    shown++;
                }
            }

            text.AppendLine(Phase switch
            {
                RoulettePhase.Spinning => "The wheel is spinning...",
                RoulettePhase.Result => $"Result: {Result} {RouletteRules.ColorName(Result)}",
                _ => "",
            });
            return true;
        }

        // ---------------------------------------------------------------- Authority

        public override void FixedUpdateNetwork()
        {
            switch (Phase)
            {
                case RoulettePhase.WaitingForPlayers:
                    MaintainNpcs();
                    if (HumanCount() > 0)
                        EnterBetting();
                    break;

                case RoulettePhase.Betting:
                    MaintainNpcs();
                    if (HumanCount() == 0)
                    {
                        ClearAllBets();
                        Phase = RoulettePhase.WaitingForPlayers;
                        PhaseTimer = TickTimer.None;
                        break;
                    }

                    if (PhaseTimer.ExpiredOrNotRunning(Runner) || AllHumansDone())
                    {
                        StartSpin();
                        break;
                    }

                    // NPCs that just sat down bet on this spin.
                    for (var i = 0; i < SeatCount; i++)
                    {
                        if (NpcPresent(i) && !SeatDone[i])
                            PlaceNpcBets(i);
                    }

                    break;

                case RoulettePhase.Spinning:
                    if (PhaseTimer.ExpiredOrNotRunning(Runner))
                        Land();
                    break;

                case RoulettePhase.Result:
                    if (PhaseTimer.ExpiredOrNotRunning(Runner))
                    {
                        ClearAllBets();
                        if (HumanCount() > 0)
                            EnterBetting();
                        else
                            Phase = RoulettePhase.WaitingForPlayers;
                    }

                    break;
            }
        }

        int HumanCount()
        {
            var count = 0;
            for (var i = 0; i < SeatCount; i++)
            {
                if (OccupantAt(i) == RouletteOccupant.Player)
                    count++;
            }

            return count;
        }

        bool AllHumansDone()
        {
            for (var i = 0; i < SeatCount; i++)
            {
                if (OccupantAt(i) == RouletteOccupant.Player && !SeatDone[i])
                    return false;
            }

            return true;
        }

        void EnterBetting()
        {
            RoundId++;
            Phase = RoulettePhase.Betting;
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, BetTimerSeconds);
            for (var i = 0; i < SeatCount; i++)
            {
                SeatDone.Set(i, false);
                SeatRoundOwners.Set(i, OccupantAt(i) == RouletteOccupant.Player ? SeatPlayers[i] : PlayerRef.None);
                if (NpcPresent(i))
                    PlaceNpcBets(i);
            }
        }

        void PlaceNpcBets(int seat)
        {
            var count = UnityEngine.Random.Range(1, 4);
            for (var i = 0; i < count; i++)
            {
                var spot = UnityEngine.Random.value < 0.4f ? UnityEngine.Random.Range(0, RouletteRules.Numbers) : UnityEngine.Random.Range(RouletteRules.Red, Spots);
                var index = seat * Spots + spot;
                Bets.Set(index, Bets[index] + RouletteRules.MinBet * UnityEngine.Random.Range(1, 5));
            }

            SeatDone.Set(seat, true);
        }

        void StartSpin()
        {
            Phase = RoulettePhase.Spinning;
            PendingResult = (sbyte)UnityEngine.Random.Range(0, RouletteRules.Numbers);
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, SpinDuration + GameTables.ResultPause);
        }

        void Land()
        {
            Result = PendingResult;
            for (var i = HistoryLength - 1; i > 0; i--)
                History.Set(i, History[i - 1]);
            History.Set(0, Result);
            SettledRoundId = RoundId;
            Phase = RoulettePhase.Result;
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, ResultDuration);
        }

        void ClearAllBets()
        {
            for (var i = 0; i < SeatCount * Spots; i++)
                Bets.Set(i, 0);
        }

        void ClearSeatBets(int seat)
        {
            for (var spot = 0; spot < Spots; spot++)
                Bets.Set(seat * Spots + spot, 0);
        }

        /// <summary>NPC cats come and go between rounds, per the table setting (GDD §5.6, <see cref="NpcSeating"/>).</summary>
        void MaintainNpcs()
        {
            if (Phase is RoulettePhase.Spinning or RoulettePhase.Result)
                return;

            Span<byte> occupants = stackalloc byte[SeatCount];
            for (var i = 0; i < SeatCount; i++)
                occupants[i] = SeatOccupants[i];
            var crew = Crew;
            var change = NpcSeating.Plan(Runner, ref crew, NpcFill, occupants, out var seat, out var join, out var instant);
            Crew = crew;
            if (!change)
                return;

            if (!join)
            {
                SeatOccupants.Set(seat, (byte)RouletteOccupant.Empty);
                SeatArrivals.Set(seat, TickTimer.None);
                ClearSeatBets(seat);
                return;
            }

            SeatOccupants.Set(seat, (byte)RouletteOccupant.Npc);
            SeatPlayers.Set(seat, PlayerRef.None);
            SeatNpcFur.Set(seat, (byte)UnityEngine.Random.Range(0, 6));
            SeatArrivals.Set(seat, instant ? TickTimer.None : TickTimer.CreateFromSeconds(Runner, NpcSeating.ArrivalSeconds(_seatAnchors[seat])));
            if (Phase == RoulettePhase.Betting && NpcPresent(seat))
                PlaceNpcBets(seat);
        }

        void IPlayerLeft.PlayerLeft(PlayerRef player)
        {
            if (!HasStateAuthority)
                return;
            for (var i = 0; i < SeatCount; i++)
            {
                if (OccupantAt(i) == RouletteOccupant.Player && SeatPlayers[i] == player)
                    Vacate(i);
            }
        }

        void Vacate(int seat)
        {
            SeatOccupants.Set(seat, (byte)RouletteOccupant.Empty);
            SeatPlayers.Set(seat, PlayerRef.None);
            // Bets placed for a spin already under way still ride; during betting they're simply withdrawn.
            if (Phase == RoulettePhase.Betting)
                ClearSeatBets(seat);
        }

        // ---------------------------------------------------------------- RPCs

        public void RequestSeat(int seat) => RPC_RequestSeat((byte)seat);
        public void AddBet(int seat, int spot, int amount) => RPC_AddBet((byte)seat, (byte)spot, amount);
        public void ClearBets(int seat) => RPC_ClearBets((byte)seat);
        public void SetDone(int seat, bool done) => RPC_SetDone((byte)seat, done);
        public void ChangeSettings(int betTimerOption, int npcFill) => RPC_SetSettings((byte)betTimerOption, (byte)npcFill);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_RequestSeat(byte seat, RpcInfo info = default)
        {
            if (seat >= SeatCount || TryGetSeatOf(info.Source, out _))
                return;
            var occupant = OccupantAt(seat);
            if (occupant == RouletteOccupant.Player)
                return;
            if (occupant == RouletteOccupant.Npc && Phase is RoulettePhase.Spinning or RoulettePhase.Result)
                return;

            ClearSeatBets(seat);
            SeatOccupants.Set(seat, (byte)RouletteOccupant.Player);
            SeatPlayers.Set(seat, info.Source);
            SeatArrivals.Set(seat, TickTimer.None);
            SeatDone.Set(seat, false);
            if (Phase == RoulettePhase.Betting)
                SeatRoundOwners.Set(seat, info.Source);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_LeaveSeat(byte seat, RpcInfo info = default)
        {
            if (seat < SeatCount && OccupantAt(seat) == RouletteOccupant.Player && SeatPlayers[seat] == info.Source)
                Vacate(seat);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_AddBet(byte seat, byte spot, int amount, RpcInfo info = default)
        {
            if (Phase != RoulettePhase.Betting || seat >= SeatCount || spot >= Spots || SeatPlayers[seat] != info.Source || amount <= 0)
                return;
            if (TotalBetAt(seat) + amount > RouletteRules.MaxTotalBet)
                return;
            var index = seat * Spots + spot;
            Bets.Set(index, Bets[index] + amount);
            SeatDone.Set(seat, false);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_ClearBets(byte seat, RpcInfo info = default)
        {
            if (Phase == RoulettePhase.Betting && seat < SeatCount && SeatPlayers[seat] == info.Source)
                ClearSeatBets(seat);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_SetDone(byte seat, NetworkBool done, RpcInfo info = default)
        {
            if (Phase == RoulettePhase.Betting && seat < SeatCount && SeatPlayers[seat] == info.Source)
                SeatDone.Set(seat, done);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_SetSettings(byte betTimerOption, byte npcFill, RpcInfo info = default)
        {
            if (!TryGetSeatOf(info.Source, out _))
                return;
            BetTimerOption = (byte)Mathf.Clamp(betTimerOption, 0, BetTimerOptions.Length - 1);
            NpcFill = NpcSeating.Clamp(npcFill);
            RPC_Notice($"{PlayerCat.NameOf(info.Source)} set {_displayName}: betting timer {BetTimerSeconds}s, NPC players {NpcSeating.Describe(NpcFill)}.");
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_Notice(string text) => ChatLog.AddSystem(text);

        // ---------------------------------------------------------------- Local wallet (GDD §9.4)

        public override void Render()
        {
            if (Runner == null)
                return;

            var local = Runner.LocalPlayer;
            var seat = -1;
            for (var i = 0; i < SeatCount; i++)
            {
                if (SeatRoundOwners[i] == local)
                {
                    seat = i;
                    break;
                }
            }

            if (_walletRound != RoundId)
            {
                _walletRound = RoundId;
                _walletCharged = 0;
            }

            // Bets can grow or shrink while betting is open (clear, leave the seat), so keep the balance in step.
            var wager = seat >= 0 && SeatPlayers[seat] == local || Phase != RoulettePhase.Betting ? (seat >= 0 ? TotalBetAt(seat) : 0) : 0;
            if (wager != _walletCharged && _walletPaidRound != RoundId)
            {
                ProfileStore.SetBalance(ProfileStore.Current.Balance - (wager - _walletCharged));
                _walletCharged = wager;
            }

            if (seat >= 0 && SettledRoundId == RoundId && _walletPaidRound != RoundId && Phase == RoulettePhase.Result)
            {
                _walletPaidRound = RoundId;
                var payout = PayoutAt(seat, Result);
                if (payout > 0)
                    ProfileStore.SetBalance(ProfileStore.Current.Balance + payout);
                if (_walletCharged > 0)
                    LocalRoundResult?.Invoke(this, payout - _walletCharged);
            }
        }
    }
}
