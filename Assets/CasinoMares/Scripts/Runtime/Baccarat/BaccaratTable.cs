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

namespace CasinoMares.Baccarat
{
    public enum BaccaratPhase : byte
    {
        WaitingForPlayers,
        Betting,
        Dealing,
        Result,
    }

    public enum BaccaratOccupant : byte
    {
        Empty,
        Player,
        Npc,
    }

    /// <summary>
    /// A Punto Banco table (GDD §13.4). Everyone bets Player / Banker / Tie in one window; the croupier deals by
    /// the fixed tableau; each client settles its own chips. Same authority model as the other tables.
    /// </summary>
    public class BaccaratTable : NetworkBehaviour, IGameTable, IPlayerLeft
    {
        public const int SeatCount = 5;
        public const int Spots = BaccaratRules.Spots;
        public const int HistoryLength = 16;

        public static readonly byte[] BetTimerOptions = { 10, 15, 20, 30 };
        const float RevealInterval = 0.7f;
        const float ResultDuration = 5f;

        public static readonly List<BaccaratTable> All = new();

        public static event Action<BaccaratTable, int> LocalRoundResult;

        [SerializeField] string _displayName = "Baccarat";
        [SerializeField] Transform[] _seatAnchors = Array.Empty<Transform>();
        [SerializeField] CinemachineCamera[] _seatCameras = Array.Empty<CinemachineCamera>();
        [SerializeField] CinemachineCamera _overheadCamera;

        [Networked] NetworkBool Initialized { get; set; }
        [Networked] public BaccaratPhase Phase { get; set; }
        [Networked] TickTimer PhaseTimer { get; set; }
        [Networked] TickTimer StepTimer { get; set; }
        [Networked] public int RoundId { get; set; }
        [Networked] public int SettledRoundId { get; set; }
        [Networked] public byte BetTimerOption { get; set; }
        [Networked] public byte NpcFill { get; set; }
        [Networked] NpcCrew Crew { get; set; }
        [Networked] int ShoeSeed { get; set; }
        [Networked] short ShoeIndex { get; set; }
        [Networked] public BaccaratOutcome Outcome { get; set; }

        [Networked, Capacity(3)] NetworkArray<byte> PlayerCards => default;
        [Networked, Capacity(3)] NetworkArray<byte> BankerCards => default;
        [Networked] byte PlayerCount { get; set; }
        [Networked] byte BankerCount { get; set; }
        [Networked] public byte Revealed { get; set; }
        /// <summary>When everyone sees the latest round event (<see cref="TableReveal"/>).</summary>
        [Networked] int RevealTick { get; set; }

        [Networked, Capacity(SeatCount)] NetworkArray<byte> SeatOccupants => default;
        [Networked, Capacity(SeatCount)] NetworkArray<PlayerRef> SeatPlayers => default;
        [Networked, Capacity(SeatCount)] NetworkArray<byte> SeatNpcFur => default;
        [Networked, Capacity(SeatCount)] NetworkArray<TickTimer> SeatArrivals => default;
        /// <summary>Counts each time a player pawed the NPC in a seat away (SeatCats plays the hiss and the run).</summary>
        [Networked, Capacity(SeatCount)] NetworkArray<byte> SeatScares => default;
        /// <summary>Runs while a seat rests after a scare: no NPC sits down there.</summary>
        [Networked, Capacity(SeatCount)] NetworkArray<TickTimer> SeatNpcRests => default;
        [Networked, Capacity(SeatCount)] NetworkArray<PlayerRef> SeatRoundOwners => default;
        [Networked, Capacity(SeatCount)] NetworkArray<NetworkBool> SeatDone => default;
        [Networked, Capacity(SeatCount * Spots)] NetworkArray<int> Bets => default;
        [Networked, Capacity(HistoryLength)] NetworkArray<byte> History => default;

        int _walletRound;
        int _walletCharged;
        int _walletPaidRound;
        RevealQueue<Snapshot> _reveal;
        int _stampedTick = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            LocalRoundResult = null;
        }

        // ---------------------------------------------------------------- Read API

        public string DisplayName => _displayName;
        public int BetTimerSeconds => BetTimerOptions[Mathf.Clamp(BetTimerOption, 0, BetTimerOptions.Length - 1)];
        public BaccaratOccupant OccupantAt(int seat) => (BaccaratOccupant)SeatOccupants[seat];
        public PlayerRef PlayerAt(int seat) => SeatPlayers[seat];
        public PlayerRef RoundOwnerAt(int seat) => SeatRoundOwners[seat];
        public int NpcFurAt(int seat) => SeatNpcFur[seat];
        public int NpcScaresAt(int seat) => SeatScares[seat];

        /// <summary>Seconds until the NPC in this seat, still walking in from the entrance, sits down (0 once seated).</summary>
        public float NpcArrivalAt(int seat) => SeatArrivals[seat].IsRunning && Runner != null ? Mathf.Max(0f, SeatArrivals[seat].RemainingTime(Runner) ?? 0f) : 0f;

        /// <summary>An NPC sits here and has arrived, so it plays.</summary>
        bool NpcPresent(int seat) => OccupantAt(seat) == BaccaratOccupant.Npc && SeatArrivals[seat].ExpiredOrNotRunning(Runner);
        public bool IsDoneAt(int seat) => SeatDone[seat];
        public int BetAt(int seat, int spot) => Bets[seat * Spots + spot];
        public Transform GetSeatAnchor(int seat) => _seatAnchors[seat];
        public CinemachineCamera GetSeatCamera(int seat) => _seatCameras[seat];
        public CinemachineCamera GetOverheadCamera() => _overheadCamera;
        public int ShoeSeedValue => ShoeSeed;

        public int TotalBetAt(int seat)
        {
            var total = 0;
            for (var spot = 0; spot < Spots; spot++)
                total += Bets[seat * Spots + spot];
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

        // ---------------------------------------------------------------- What players see (every client)

        /// <summary>
        /// The coup as players see it right now: the networked round state, with each event (a card turned over,
        /// the result) held back until its reveal tick so it happens on every screen at once. Views, HUDs and the
        /// local wallet read this; the round logic reads the live state. Seats and settings stay live.
        /// </summary>
        public Snapshot Shown => (_reveal ??= new RevealQueue<Snapshot>(Capture)).Get(Runner, RevealTick);

        public sealed class Snapshot
        {
            public BaccaratPhase Phase;
            public int RoundId;
            public int SettledRoundId;
            public BaccaratOutcome Outcome;
            public byte Revealed;
            internal byte PlayerCount;
            internal NetworkRunner Runner;
            internal TickTimer PhaseTimer;
            internal readonly byte[] PlayerCards = new byte[3];
            internal readonly byte[] BankerCards = new byte[3];
            internal readonly PlayerRef[] RoundOwners = new PlayerRef[SeatCount];
            internal readonly NetworkBool[] Done = new NetworkBool[SeatCount];
            internal readonly int[] Bets = new int[SeatCount * Spots];
            internal readonly byte[] History = new byte[HistoryLength];
            readonly byte[] _scratch = new byte[3];

            public PlayerRef RoundOwnerAt(int seat) => RoundOwners[seat];
            public bool IsDoneAt(int seat) => Done[seat];
            public int BetAt(int seat, int spot) => Bets[seat * Spots + spot];
            public BaccaratOutcome HistoryAt(int index) => (BaccaratOutcome)History[index];
            public float TimeRemaining => PhaseTimer.IsRunning && Runner != null ? Mathf.Max(0f, PhaseTimer.RemainingTime(Runner) ?? 0f) : -1f;

            /// <summary>Card i of the coup in dealing order (P, B, P, B, P3, B3) as the players see it: side and card.</summary>
            public bool TryGetRevealedCard(int order, out bool isBanker, out int index, out byte card)
            {
                isBanker = false;
                index = -1;
                card = Cards.None;
                if (order >= Revealed)
                    return false;

                switch (order)
                {
                    case < 4:
                        isBanker = order % 2 == 1;
                        index = order / 2;
                        break;
                    case 4 when PlayerCount == 3:
                        index = 2;
                        break;
                    default:
                        isBanker = true;
                        index = 2;
                        break;
                }

                card = isBanker ? BankerCards[index] : PlayerCards[index];
                return true;
            }

            public int VisibleTotal(bool banker)
            {
                var count = 0;
                for (var order = 0; order < Revealed; order++)
                {
                    if (TryGetRevealedCard(order, out var isBanker, out _, out var card) && isBanker == banker)
                        _scratch[count++] = card;
                }

                return BaccaratRules.Total(new ReadOnlySpan<byte>(_scratch, 0, count));
            }

            public int TotalBetAt(int seat)
            {
                var total = 0;
                for (var spot = 0; spot < Spots; spot++)
                    total += Bets[seat * Spots + spot];
                return total;
            }

            public int PayoutAt(int seat)
            {
                var total = 0;
                for (var spot = 0; spot < Spots; spot++)
                    total += BaccaratRules.Payout(spot, Bets[seat * Spots + spot], Outcome);
                return total;
            }
        }

        void Capture(Snapshot shown)
        {
            shown.Phase = Phase;
            shown.RoundId = RoundId;
            shown.SettledRoundId = SettledRoundId;
            shown.Outcome = Outcome;
            shown.Revealed = Revealed;
            shown.PlayerCount = PlayerCount;
            shown.Runner = Runner;
            shown.PhaseTimer = PhaseTimer;
            PlayerCards.CopyTo(shown.PlayerCards, false);
            BankerCards.CopyTo(shown.BankerCards, false);
            SeatRoundOwners.CopyTo(shown.RoundOwners, false);
            SeatDone.CopyTo(shown.Done, false);
            Bets.CopyTo(shown.Bets, false);
            History.CopyTo(shown.History, false);
        }

        /// <summary>Authority: marks what this tick changed as an event for everyone to see at once; returns the seconds until they do.</summary>
        float Reveal()
        {
            RevealTick = TableReveal.Stamp(Runner, RevealTick, ref _stampedTick);
            return TableReveal.SecondsUntil(Runner, RevealTick);
        }

        // ---------------------------------------------------------------- Lifecycle

        public override void Spawned()
        {
            All.Add(this);
            GameTables.All.Add(this);
            _reveal = null; // a fresh session starts from what's on the table now
            if (HasStateAuthority && !Initialized)
            {
                Initialized = true;
                BetTimerOption = 1;
                NpcFill = NpcSeating.Auto;
                ShoeSeed = UnityEngine.Random.Range(1, int.MaxValue);
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
                if (OccupantAt(i) == BaccaratOccupant.Player && SeatPlayers[i] == player)
                {
                    seat = i;
                    return true;
                }
            }

            return false;
        }

        public void RequestLeave(int seat) => RPC_LeaveSeat((byte)seat);

        public void ScareNpc(int seat) => RPC_ScareNpc((byte)seat);

        public bool TryDescribePlayer(PlayerRef player, StringBuilder text)
        {
            if (!TryGetSeatOf(player, out var seat))
                return false;
            var shown = Shown;
            text.AppendLine($"{_displayName} · seat {seat + 1}");
            var any = false;
            for (var spot = 0; spot < Spots; spot++)
            {
                var amount = shown.BetAt(seat, spot);
                if (amount <= 0)
                    continue;
                text.AppendLine($"{BaccaratRules.SpotNames[spot]}: {amount:N0}");
                any = true;
            }

            if (!any)
                text.AppendLine(shown.Phase == BaccaratPhase.Betting ? "Choosing a bet..." : "Sitting this coup out");
            if (shown.Revealed > 0)
                text.AppendLine($"Player {shown.VisibleTotal(false)} · Banker {shown.VisibleTotal(true)}");
            if (shown.Phase == BaccaratPhase.Result)
                text.AppendLine($"{shown.Outcome} wins");
            return true;
        }

        // ---------------------------------------------------------------- Authority

        public override void FixedUpdateNetwork()
        {
            switch (Phase)
            {
                case BaccaratPhase.WaitingForPlayers:
                    MaintainNpcs();
                    if (HumanCount() > 0)
                        EnterBetting();
                    break;

                case BaccaratPhase.Betting:
                    MaintainNpcs();
                    if (HumanCount() == 0)
                    {
                        ClearAllBets();
                        Phase = BaccaratPhase.WaitingForPlayers;
                        PhaseTimer = TickTimer.None;
                        Reveal();
                    }
                    else if (PhaseTimer.ExpiredOrNotRunning(Runner) || AllHumansDone())
                    {
                        StartDeal();
                    }
                    else
                    {
                        // NPCs that just sat down bet on this coup.
                        for (var i = 0; i < SeatCount; i++)
                        {
                            if (NpcPresent(i) && !SeatDone[i])
                                PlaceNpcBet(i);
                        }
                    }

                    break;

                case BaccaratPhase.Dealing:
                    if (!StepTimer.ExpiredOrNotRunning(Runner))
                        break;
                    if (Revealed < PlayerCount + BankerCount)
                        RevealNext();
                    else
                        Settle();
                    break;

                case BaccaratPhase.Result:
                    if (PhaseTimer.ExpiredOrNotRunning(Runner))
                    {
                        ClearAllBets();
                        Revealed = 0;
                        Reveal();
                        if (HumanCount() > 0)
                            EnterBetting();
                        else
                            Phase = BaccaratPhase.WaitingForPlayers;
                    }

                    break;
            }
        }

        int HumanCount()
        {
            var count = 0;
            for (var i = 0; i < SeatCount; i++)
            {
                if (OccupantAt(i) == BaccaratOccupant.Player)
                    count++;
            }

            return count;
        }

        bool AllHumansDone()
        {
            for (var i = 0; i < SeatCount; i++)
            {
                if (OccupantAt(i) == BaccaratOccupant.Player && !SeatDone[i])
                    return false;
            }

            return true;
        }

        void EnterBetting()
        {
            RoundId++;
            Phase = BaccaratPhase.Betting;
            Outcome = BaccaratOutcome.None;
            Revealed = 0;
            // Betting opens when everyone sees it, so the window runs from the reveal.
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, BetTimerSeconds + Reveal());
            for (var i = 0; i < SeatCount; i++)
            {
                SeatDone.Set(i, false);
                SeatRoundOwners.Set(i, OccupantAt(i) == BaccaratOccupant.Player ? SeatPlayers[i] : PlayerRef.None);
                if (NpcPresent(i))
                    PlaceNpcBet(i);
            }
        }

        void PlaceNpcBet(int seat)
        {
            var roll = UnityEngine.Random.value;
            var spot = roll < 0.46f ? BaccaratRules.SpotBanker : roll < 0.92f ? BaccaratRules.SpotPlayer : BaccaratRules.SpotTie;
            Bets.Set(seat * Spots + spot, BaccaratRules.MinBet * UnityEngine.Random.Range(1, 5));
            SeatDone.Set(seat, true);
        }

        void StartDeal()
        {
            var shoe = new Shoe(BaccaratRules.Decks, ShoeSeed, ShoeIndex);
            if (shoe.CutCardReached(0.8))
            {
                ShoeSeed = UnityEngine.Random.Range(1, int.MaxValue);
                shoe = new Shoe(BaccaratRules.Decks, ShoeSeed);
                RPC_Notice($"{_displayName}: fresh shoe.");
            }

            Span<byte> player = stackalloc byte[3];
            Span<byte> banker = stackalloc byte[3];
            var (playerCount, bankerCount) = BaccaratRules.DealCoup(shoe, player, banker);
            ShoeIndex = (short)shoe.DrawIndex;
            for (var i = 0; i < 3; i++)
            {
                PlayerCards.Set(i, i < playerCount ? player[i] : Cards.None);
                BankerCards.Set(i, i < bankerCount ? banker[i] : Cards.None);
            }

            PlayerCount = (byte)playerCount;
            BankerCount = (byte)bankerCount;
            Revealed = 0;
            Phase = BaccaratPhase.Dealing;
            PhaseTimer = TickTimer.None;
            StepTimer = TickTimer.CreateFromSeconds(Runner, 0.4f);
            Reveal();
        }

        /// <summary>Turns over the next card; after the last one there's a pause before the result.</summary>
        void RevealNext()
        {
            Revealed++;
            var last = Revealed >= PlayerCount + BankerCount;
            StepTimer = TickTimer.CreateFromSeconds(Runner, last ? GameTables.ResultPause : RevealInterval);
            Reveal();
        }

        void Settle()
        {
            Span<byte> player = stackalloc byte[3];
            Span<byte> banker = stackalloc byte[3];
            for (var i = 0; i < 3; i++)
            {
                player[i] = PlayerCards[i];
                banker[i] = BankerCards[i];
            }

            Outcome = BaccaratRules.Compare(BaccaratRules.Total(player.Slice(0, PlayerCount)), BaccaratRules.Total(banker.Slice(0, BankerCount)));
            for (var i = HistoryLength - 1; i > 0; i--)
                History.Set(i, History[i - 1]);
            History.Set(0, (byte)Outcome);
            SettledRoundId = RoundId;
            Phase = BaccaratPhase.Result;
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, ResultDuration);
            Reveal();
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
            if (Phase is BaccaratPhase.Dealing or BaccaratPhase.Result)
                return;

            Span<byte> occupants = stackalloc byte[SeatCount];
            for (var i = 0; i < SeatCount; i++)
                occupants[i] = NpcSeating.PlanOccupant(SeatOccupants[i], SeatNpcRests[i], Runner);
            var crew = Crew;
            var change = NpcSeating.Plan(Runner, ref crew, NpcFill, occupants, out var seat, out var join, out var instant);
            Crew = crew;
            if (!change)
                return;

            if (!join)
            {
                SeatOccupants.Set(seat, (byte)BaccaratOccupant.Empty);
                SeatArrivals.Set(seat, TickTimer.None);
                ClearSeatBets(seat);
                return;
            }

            SeatOccupants.Set(seat, (byte)BaccaratOccupant.Npc);
            SeatPlayers.Set(seat, PlayerRef.None);
            SeatNpcFur.Set(seat, (byte)UnityEngine.Random.Range(0, 6));
            SeatArrivals.Set(seat, instant ? TickTimer.None : TickTimer.CreateFromSeconds(Runner, NpcSeating.ArrivalSeconds(_seatAnchors[seat])));
            if (Phase == BaccaratPhase.Betting && NpcPresent(seat))
                PlaceNpcBet(seat);
        }

        void IPlayerLeft.PlayerLeft(PlayerRef player)
        {
            if (!HasStateAuthority)
                return;
            for (var i = 0; i < SeatCount; i++)
            {
                if (OccupantAt(i) == BaccaratOccupant.Player && SeatPlayers[i] == player)
                    Vacate(i);
            }
        }

        void Vacate(int seat)
        {
            SeatOccupants.Set(seat, (byte)BaccaratOccupant.Empty);
            SeatPlayers.Set(seat, PlayerRef.None);
            if (Phase == BaccaratPhase.Betting)
                ClearSeatBets(seat);
        }

        // ---------------------------------------------------------------- RPCs

        public void RequestSeat(int seat) => RPC_RequestSeat((byte)seat);
        public void AddBet(int seat, int spot, int amount) => RPC_AddBet((byte)seat, (byte)spot, amount);
        public void ClearBets(int seat) => RPC_ClearBets((byte)seat);
        public void SetDone(int seat) => RPC_SetDone((byte)seat);
        public void ChangeSettings(int betTimerOption, int npcFill) => RPC_SetSettings((byte)betTimerOption, (byte)npcFill);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_RequestSeat(byte seat, RpcInfo info = default)
        {
            if (seat >= SeatCount || TryGetSeatOf(info.Source, out _) || OccupantAt(seat) == BaccaratOccupant.Player)
                return;
            if (OccupantAt(seat) == BaccaratOccupant.Npc && Phase is BaccaratPhase.Dealing or BaccaratPhase.Result)
                return;

            ClearSeatBets(seat);
            SeatOccupants.Set(seat, (byte)BaccaratOccupant.Player);
            SeatPlayers.Set(seat, info.Source);
            SeatArrivals.Set(seat, TickTimer.None);
            SeatDone.Set(seat, false);
            if (Phase == BaccaratPhase.Betting)
                SeatRoundOwners.Set(seat, info.Source);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_LeaveSeat(byte seat, RpcInfo info = default)
        {
            if (seat < SeatCount && OccupantAt(seat) == BaccaratOccupant.Player && SeatPlayers[seat] == info.Source)
                Vacate(seat);
        }

        /// <summary>
        /// A player pawed the NPC in this seat: it leaves the way a player leaving mid-round does (its hand or bets
        /// go on without it), and no NPC sits there again for a minute.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_ScareNpc(byte seat, RpcInfo info = default)
        {
            if (seat >= SeatCount || !NpcPresent(seat) || !NpcSeating.InPawReach(info.Source, _seatAnchors[seat]))
                return;
            Vacate(seat);
            SeatArrivals.Set(seat, TickTimer.None);
            SeatScares.Set(seat, (byte)(SeatScares[seat] + 1));
            SeatNpcRests.Set(seat, TickTimer.CreateFromSeconds(Runner, NpcSeating.ScareRestSeconds));
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_AddBet(byte seat, byte spot, int amount, RpcInfo info = default)
        {
            if (Phase != BaccaratPhase.Betting || seat >= SeatCount || spot >= Spots || SeatPlayers[seat] != info.Source || amount <= 0)
                return;
            if (TotalBetAt(seat) + amount > BaccaratRules.MaxTotalBet)
                return;
            var index = seat * Spots + spot;
            Bets.Set(index, Bets[index] + amount);
            SeatDone.Set(seat, false);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_ClearBets(byte seat, RpcInfo info = default)
        {
            if (Phase == BaccaratPhase.Betting && seat < SeatCount && SeatPlayers[seat] == info.Source)
                ClearSeatBets(seat);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_SetDone(byte seat, RpcInfo info = default)
        {
            if (Phase == BaccaratPhase.Betting && seat < SeatCount && SeatPlayers[seat] == info.Source)
                SeatDone.Set(seat, true);
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

            // The shown coup, so the payout and its toast come with the last card, not before it.
            var shown = Shown;
            var local = Runner.LocalPlayer;
            var seat = -1;
            for (var i = 0; i < SeatCount; i++)
            {
                if (shown.RoundOwnerAt(i) == local)
                {
                    seat = i;
                    break;
                }
            }

            if (_walletRound != shown.RoundId)
            {
                _walletRound = shown.RoundId;
                _walletCharged = 0;
            }

            var stillSeated = seat >= 0 && SeatPlayers[seat] == local;
            var wager = seat >= 0 && (stillSeated || shown.Phase != BaccaratPhase.Betting) ? shown.TotalBetAt(seat) : 0;
            if (wager != _walletCharged && _walletPaidRound != shown.RoundId)
            {
                ProfileStore.SetBalance(ProfileStore.Current.Balance - (wager - _walletCharged));
                _walletCharged = wager;
            }

            if (seat >= 0 && shown.SettledRoundId == shown.RoundId && _walletPaidRound != shown.RoundId && shown.Phase == BaccaratPhase.Result)
            {
                _walletPaidRound = shown.RoundId;
                var payout = shown.PayoutAt(seat);
                if (payout > 0)
                    ProfileStore.SetBalance(ProfileStore.Current.Balance + payout);
                if (_walletCharged > 0)
                    LocalRoundResult?.Invoke(this, payout - _walletCharged);
            }
        }
    }
}
