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

namespace CasinoMares.Blackjack
{
    public enum BlackjackPhase : byte
    {
        WaitingForPlayers,
        Betting,
        Dealing,
        PlayerTurns,
        DealerTurn,
        Settlement,
    }

    public enum SeatOccupant : byte
    {
        Empty,
        Player,
        Npc,
    }

    [Flags]
    public enum HandFlags : byte
    {
        None = 0,
        Doubled = 1,
        FromSplit = 2,
        SplitAces = 4,
        Done = 8,
    }

    /// <summary>
    /// One blackjack table (GDD §5, §9). A scene object whose state authority is the master client (it moves if
    /// the master leaves). The authority runs the round state machine in FixedUpdateNetwork; players act through
    /// RPCs; every client renders from the networked state and settles its own chips (§9.4).
    /// </summary>
    public class BlackjackTable : NetworkBehaviour, IGameTable, IPlayerLeft
    {
        public const int SeatCount = 5;
        public const int MaxHands = BlackjackRules.MaxHands;
        public const int MaxCards = BlackjackRules.MaxCardsPerHand;
        public const int HandSlots = SeatCount * MaxHands;

        public static readonly byte[] TurnTimerOptions = { 0, 10, 20, 30, 60 };
        public static readonly byte[] BetTimerOptions = { 0, 10, 15, 30 };

        const float DealInterval = 0.32f;
        const float DealerDrawInterval = 0.75f;
        const float SettlementDuration = 4.5f;
        const float NpcThinkMin = 0.7f;
        const float NpcThinkMax = 1.5f;

        public static readonly List<BlackjackTable> All = new();

        /// <summary>Raised on every client when a hand wins big nearby (patrons react). Arg: table position.</summary>
        public static event Action<Vector3> BigWin;

        /// <summary>Raised on every client when a round settles, for reactions and effects.</summary>
        public event Action RoundSettled;

        /// <summary>Raised on the local client when its own hands were paid. Arg: net chips won (can be negative).</summary>
        public static event Action<BlackjackTable, int> LocalRoundResult;

        [SerializeField] string _displayName = "Blackjack";
        [SerializeField] Transform[] _seatAnchors = Array.Empty<Transform>();
        [SerializeField] CinemachineCamera[] _seatCameras = Array.Empty<CinemachineCamera>();

        // ---------------------------------------------------------------- Networked state

        [Networked] public NetworkBool Initialized { get; set; }
        [Networked] public BlackjackPhase Phase { get; set; }
        [Networked] TickTimer PhaseTimer { get; set; }
        [Networked] TickTimer StepTimer { get; set; }
        [Networked] public int RoundId { get; set; }
        [Networked] public int SettledRoundId { get; set; }
        [Networked] public byte TurnTimerOption { get; set; }
        [Networked] public byte BetTimerOption { get; set; }
        [Networked] public byte NpcFill { get; set; }
        [Networked] NpcCrew Crew { get; set; }
        [Networked] int ShoeSeed { get; set; }
        [Networked] short ShoeIndex { get; set; }
        [Networked] byte DealStep { get; set; }

        [Networked, Capacity(SeatCount)] NetworkArray<byte> SeatOccupants => default;
        [Networked, Capacity(SeatCount)] NetworkArray<PlayerRef> SeatPlayers => default;
        [Networked, Capacity(SeatCount)] NetworkArray<byte> SeatNpcFur => default;
        [Networked, Capacity(SeatCount)] NetworkArray<TickTimer> SeatArrivals => default;
        [Networked, Capacity(SeatCount)] NetworkArray<int> SeatPendingBets => default;
        [Networked, Capacity(SeatCount)] NetworkArray<NetworkBool> SeatLocked => default;
        [Networked, Capacity(SeatCount)] NetworkArray<PlayerRef> SeatRoundOwners => default;
        [Networked, Capacity(SeatCount)] NetworkArray<byte> SeatHandCounts => default;

        [Networked, Capacity(HandSlots)] NetworkArray<int> HandBets => default;
        [Networked, Capacity(HandSlots)] NetworkArray<byte> HandFlagBits => default;
        [Networked, Capacity(HandSlots)] NetworkArray<byte> HandOutcomes => default;
        [Networked, Capacity(HandSlots)] NetworkArray<byte> HandCardCounts => default;
        [Networked, Capacity(HandSlots * MaxCards)] NetworkArray<byte> HandCards => default;

        [Networked, Capacity(MaxCards)] NetworkArray<byte> DealerCards => default;
        [Networked] public byte DealerCardCount { get; set; }
        [Networked] public NetworkBool HoleRevealed { get; set; }
        /// <summary>The dealer has drawn all its cards; the result follows after <see cref="GameTables.ResultPause"/>.</summary>
        [Networked] NetworkBool DealerFinished { get; set; }
        [Networked] public sbyte ActiveSeat { get; set; }
        [Networked] public byte ActiveHand { get; set; }
        /// <summary>When everyone sees the latest round event (<see cref="TableReveal"/>).</summary>
        [Networked] int RevealTick { get; set; }

        readonly byte[] _handScratch = new byte[MaxCards];
        readonly byte[] _dealerScratch = new byte[MaxCards];
        Shoe _shoe;
        RevealQueue<Snapshot> _reveal;
        int _stampedTick = -1;

        // Local chip accounting (§9.4), per client, for the local player's seat only.
        int _walletRound;
        int _walletCharged;
        int _walletPaidRound;
        int _lastSeenSettledRound;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            BigWin = null;
            LocalRoundResult = null;
        }

        // ---------------------------------------------------------------- Public read API (view, UI)

        public string DisplayName => _displayName;
        public int ShoeSeedValue => ShoeSeed;
        public int TurnTimerSeconds => TurnTimerOptions[Mathf.Clamp(TurnTimerOption, 0, TurnTimerOptions.Length - 1)];
        public int BetTimerSeconds => BetTimerOptions[Mathf.Clamp(BetTimerOption, 0, BetTimerOptions.Length - 1)];
        public bool IsInRound => Phase is BlackjackPhase.Dealing or BlackjackPhase.PlayerTurns or BlackjackPhase.DealerTurn or BlackjackPhase.Settlement;

        public SeatOccupant OccupantAt(int seat) => (SeatOccupant)SeatOccupants[seat];
        public PlayerRef PlayerAt(int seat) => SeatPlayers[seat];
        public PlayerRef RoundOwnerAt(int seat) => SeatRoundOwners[seat];
        public int NpcFurAt(int seat) => SeatNpcFur[seat];

        /// <summary>Seconds until the NPC in this seat, still walking in from the entrance, sits down (0 once seated).</summary>
        public float NpcArrivalAt(int seat) => SeatArrivals[seat].IsRunning && Runner != null ? Mathf.Max(0f, SeatArrivals[seat].RemainingTime(Runner) ?? 0f) : 0f;

        /// <summary>An NPC sits here and has arrived, so it plays.</summary>
        bool NpcPresent(int seat) => OccupantAt(seat) == SeatOccupant.Npc && SeatArrivals[seat].ExpiredOrNotRunning(Runner);
        public int PendingBetAt(int seat) => SeatPendingBets[seat];
        public bool IsLockedAt(int seat) => SeatLocked[seat];
        public int HandCountAt(int seat) => SeatHandCounts[seat];
        public int HandBetAt(int seat, int hand) => HandBets[Slot(seat, hand)];
        public HandFlags HandFlagsAt(int seat, int hand) => (HandFlags)HandFlagBits[Slot(seat, hand)];
        public HandOutcome OutcomeAt(int seat, int hand) => (HandOutcome)HandOutcomes[Slot(seat, hand)];
        public int CardCountAt(int seat, int hand) => HandCardCounts[Slot(seat, hand)];
        public byte CardAt(int seat, int hand, int index) => HandCards[Slot(seat, hand) * MaxCards + index];
        public byte DealerCardAt(int index) => DealerCards[index];
        public Transform GetSeatAnchor(int seat) => _seatAnchors[seat];
        public CinemachineCamera GetSeatCamera(int seat) => _seatCameras[seat];

        public HandValue EvaluateHand(int seat, int hand) => BlackjackRules.Evaluate(HandSpan(seat, hand));

        public bool IsActiveTurn(int seat) => Phase == BlackjackPhase.PlayerTurns && ActiveSeat == seat;

        public bool CanHit(int seat) => IsActiveTurn(seat);
        public bool CanStand(int seat) => IsActiveTurn(seat);

        public bool CanDouble(int seat)
        {
            if (!IsActiveTurn(seat))
                return false;
            var flags = HandFlagsAt(seat, ActiveHand);
            return BlackjackRules.CanDouble(HandSpan(seat, ActiveHand), (flags & HandFlags.SplitAces) != 0);
        }

        public bool CanSplit(int seat)
        {
            if (!IsActiveTurn(seat))
                return false;
            var flags = HandFlagsAt(seat, ActiveHand);
            return BlackjackRules.CanSplit(HandSpan(seat, ActiveHand), SeatHandCounts[seat], (flags & HandFlags.SplitAces) != 0);
        }

        /// <summary>True while the local player has chips riding on a hand here that isn't settled yet.</summary>
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

        static int Slot(int seat, int hand) => seat * MaxHands + hand;

        ReadOnlySpan<byte> HandSpan(int seat, int hand)
        {
            var slot = Slot(seat, hand);
            var count = HandCardCounts[slot];
            for (var i = 0; i < count; i++)
                _handScratch[i] = HandCards[slot * MaxCards + i];
            return new ReadOnlySpan<byte>(_handScratch, 0, count);
        }

        ReadOnlySpan<byte> DealerSpan()
        {
            for (var i = 0; i < DealerCardCount; i++)
                _dealerScratch[i] = DealerCards[i];
            return new ReadOnlySpan<byte>(_dealerScratch, 0, DealerCardCount);
        }

        // ---------------------------------------------------------------- What players see (every client)

        /// <summary>
        /// The round as players see it right now: the networked round state, with each event held back until its
        /// reveal tick so a card turns over on every screen at once. Views, HUDs and the local wallet read this; the
        /// round logic reads the live state above. Seats, pending bets and settings stay live.
        /// </summary>
        public Snapshot Shown => (_reveal ??= new RevealQueue<Snapshot>(Capture)).Get(Runner, RevealTick);

        public sealed class Snapshot
        {
            public BlackjackPhase Phase;
            public int RoundId;
            public int SettledRoundId;
            public sbyte ActiveSeat;
            public byte ActiveHand;
            public byte DealerCardCount;
            public bool HoleRevealed;
            internal NetworkRunner Runner;
            internal TickTimer PhaseTimer;
            internal readonly PlayerRef[] RoundOwners = new PlayerRef[SeatCount];
            internal readonly byte[] HandCounts = new byte[SeatCount];
            internal readonly int[] Bets = new int[HandSlots];
            internal readonly byte[] FlagBits = new byte[HandSlots];
            internal readonly byte[] Outcomes = new byte[HandSlots];
            internal readonly byte[] CardCounts = new byte[HandSlots];
            internal readonly byte[] HandCards = new byte[HandSlots * MaxCards];
            internal readonly byte[] Dealer = new byte[MaxCards];

            public bool IsInRound => Phase is BlackjackPhase.Dealing or BlackjackPhase.PlayerTurns or BlackjackPhase.DealerTurn or BlackjackPhase.Settlement;
            public PlayerRef RoundOwnerAt(int seat) => RoundOwners[seat];
            public int HandCountAt(int seat) => HandCounts[seat];
            public int HandBetAt(int seat, int hand) => Bets[Slot(seat, hand)];
            public HandFlags HandFlagsAt(int seat, int hand) => (HandFlags)FlagBits[Slot(seat, hand)];
            public HandOutcome OutcomeAt(int seat, int hand) => (HandOutcome)Outcomes[Slot(seat, hand)];
            public int CardCountAt(int seat, int hand) => CardCounts[Slot(seat, hand)];
            public byte CardAt(int seat, int hand, int index) => HandCards[Slot(seat, hand) * MaxCards + index];
            public byte DealerCardAt(int index) => Dealer[index];
            public bool IsActiveTurn(int seat) => Phase == BlackjackPhase.PlayerTurns && ActiveSeat == seat;

            /// <summary>Seconds left on the betting window or the current turn, or -1 when no timer is running.</summary>
            public float TimeRemaining => PhaseTimer.IsRunning && Runner != null ? Mathf.Max(0f, PhaseTimer.RemainingTime(Runner) ?? 0f) : -1f;

            ReadOnlySpan<byte> HandSpan(int seat, int hand) =>
                new(HandCards, Slot(seat, hand) * MaxCards, CardCounts[Slot(seat, hand)]);

            public HandValue EvaluateHand(int seat, int hand) => BlackjackRules.Evaluate(HandSpan(seat, hand));

            /// <summary>The dealer's total as players can see it (the hole card is hidden until revealed).</summary>
            public HandValue EvaluateDealerVisible() =>
                BlackjackRules.Evaluate(new ReadOnlySpan<byte>(Dealer, 0, HoleRevealed ? DealerCardCount : Mathf.Min(1, (int)DealerCardCount)));

            public int TotalWagerAt(int seat)
            {
                var total = 0;
                for (var h = 0; h < HandCounts[seat]; h++)
                    total += Bets[Slot(seat, h)];
                return total;
            }

            public bool CanDouble(int seat) =>
                IsActiveTurn(seat) && BlackjackRules.CanDouble(HandSpan(seat, ActiveHand), (HandFlagsAt(seat, ActiveHand) & HandFlags.SplitAces) != 0);

            public bool CanSplit(int seat) =>
                IsActiveTurn(seat) && BlackjackRules.CanSplit(HandSpan(seat, ActiveHand), HandCounts[seat], (HandFlagsAt(seat, ActiveHand) & HandFlags.SplitAces) != 0);
        }

        void Capture(Snapshot shown)
        {
            shown.Phase = Phase;
            shown.RoundId = RoundId;
            shown.SettledRoundId = SettledRoundId;
            shown.ActiveSeat = ActiveSeat;
            shown.ActiveHand = ActiveHand;
            shown.DealerCardCount = DealerCardCount;
            shown.HoleRevealed = HoleRevealed;
            shown.Runner = Runner;
            shown.PhaseTimer = PhaseTimer;
            SeatRoundOwners.CopyTo(shown.RoundOwners, false);
            SeatHandCounts.CopyTo(shown.HandCounts, false);
            HandBets.CopyTo(shown.Bets, false);
            HandFlagBits.CopyTo(shown.FlagBits, false);
            HandOutcomes.CopyTo(shown.Outcomes, false);
            HandCardCounts.CopyTo(shown.CardCounts, false);
            HandCards.CopyTo(shown.HandCards, false);
            DealerCards.CopyTo(shown.Dealer, false);
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
                TurnTimerOption = 2; // 20 s
                BetTimerOption = 2; // 15 s
                NpcFill = NpcSeating.Auto;
                ShoeSeed = NewSeed();
                ShoeIndex = 0;
                ActiveSeat = -1;
                Phase = BlackjackPhase.WaitingForPlayers;
            }

            _lastSeenSettledRound = SettledRoundId;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            GameTables.All.Remove(this);
        }

        static int NewSeed() => UnityEngine.Random.Range(1, int.MaxValue);

        // ---------------------------------------------------------------- IGameTable

        public bool TryGetSeatOf(PlayerRef player, out int seat)
        {
            seat = -1;
            if (Object == null || !Object.IsValid || player == PlayerRef.None)
                return false;

            for (var i = 0; i < SeatCount; i++)
            {
                if (OccupantAt(i) == SeatOccupant.Player && SeatPlayers[i] == player)
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

            var shown = Shown;
            text.AppendLine($"{_displayName} · seat {seat + 1}");
            var hands = shown.HandCountAt(seat);
            if (hands == 0)
            {
                text.AppendLine(shown.Phase == BlackjackPhase.Betting
                    ? IsLockedAt(seat) ? $"Bet locked: {PendingBetAt(seat):N0}" : "Choosing a bet..."
                    : "Waiting for the next round");
            }
            else
            {
                for (var h = 0; h < hands; h++)
                {
                    var outcome = shown.OutcomeAt(seat, h);
                    var result = outcome == HandOutcome.None ? "" : $" · {OutcomeLabel(outcome)}";
                    var marker = shown.IsActiveTurn(seat) && shown.ActiveHand == h ? "▶ " : "";
                    text.AppendLine($"{marker}Hand {h + 1}: {shown.EvaluateHand(seat, h)} (bet {shown.HandBetAt(seat, h):N0}){result}");
                }
            }

            if (shown.IsActiveTurn(seat))
            {
                var remaining = shown.TimeRemaining;
                text.AppendLine(remaining >= 0f ? $"Their turn · {Mathf.CeilToInt(remaining)}s" : "Their turn");
            }

            text.AppendLine($"Dealer shows {shown.EvaluateDealerVisible()}");
            return true;
        }

        public static string OutcomeLabel(HandOutcome outcome) => outcome switch
        {
            HandOutcome.Blackjack => "Blackjack!",
            HandOutcome.Win => "Win",
            HandOutcome.Push => "Push",
            HandOutcome.Lose => "Lose",
            HandOutcome.Bust => "Bust",
            _ => "",
        };

        // ---------------------------------------------------------------- Round state machine (authority)

        public override void FixedUpdateNetwork()
        {
            switch (Phase)
            {
                case BlackjackPhase.WaitingForPlayers:
                    MaintainNpcs();
                    if (HumanCount() > 0)
                        EnterBetting();
                    break;

                case BlackjackPhase.Betting:
                    TickBetting();
                    break;

                case BlackjackPhase.Dealing:
                    if (StepTimer.ExpiredOrNotRunning(Runner))
                        DealNextCard();
                    break;

                case BlackjackPhase.PlayerTurns:
                    TickPlayerTurns();
                    break;

                case BlackjackPhase.DealerTurn:
                    if (StepTimer.ExpiredOrNotRunning(Runner))
                        DealerStep();
                    break;

                case BlackjackPhase.Settlement:
                    if (PhaseTimer.ExpiredOrNotRunning(Runner))
                        FinishRound();
                    break;
            }
        }

        int HumanCount()
        {
            var count = 0;
            for (var i = 0; i < SeatCount; i++)
            {
                if (OccupantAt(i) == SeatOccupant.Player)
                    count++;
            }

            return count;
        }

        void EnterBetting()
        {
            Phase = BlackjackPhase.Betting;
            for (var i = 0; i < SeatCount; i++)
            {
                SeatLocked.Set(i, false);
                if (NpcPresent(i))
                    LockNpcBet(i);
            }

            // The window opens when everyone sees the table cleared, so it runs from the reveal.
            var delay = Reveal();
            var seconds = BetTimerSeconds;
            PhaseTimer = seconds > 0 ? TickTimer.CreateFromSeconds(Runner, seconds + delay) : TickTimer.None;
            MaintainNpcs();
        }

        void LockNpcBet(int seat)
        {
            var units = UnityEngine.Random.Range(1, 6);
            SeatPendingBets.Set(seat, BlackjackRules.MinBet * units);
            SeatLocked.Set(seat, true);
        }

        void TickBetting()
        {
            MaintainNpcs();

            // NPCs that just sat down join this round.
            for (var i = 0; i < SeatCount; i++)
            {
                if (NpcPresent(i) && !SeatLocked[i])
                    LockNpcBet(i);
            }

            if (HumanCount() == 0)
            {
                Phase = BlackjackPhase.WaitingForPlayers;
                PhaseTimer = TickTimer.None;
                Reveal();
                return;
            }

            var allHumansDecided = true;
            var anyHumanBet = false;
            for (var i = 0; i < SeatCount; i++)
            {
                if (OccupantAt(i) != SeatOccupant.Player)
                    continue;
                if (!SeatLocked[i])
                    allHumansDecided = false;
                else if (SeatPendingBets[i] >= BlackjackRules.MinBet)
                    anyHumanBet = true;
            }

            var timerExpired = PhaseTimer.IsRunning && PhaseTimer.Expired(Runner);
            if (!allHumansDecided && !timerExpired)
                return;

            if (anyHumanBet)
            {
                StartRound();
            }
            else
            {
                // Everyone sat this one out (or let the timer run out); open a fresh betting window.
                EnterBetting();
            }
        }

        void StartRound()
        {
            RoundId++;

            var shoe = CurrentShoe();
            if (shoe.CutCardReached(BlackjackRules.Penetration))
            {
                ShoeSeed = NewSeed();
                ShoeIndex = 0;
                _shoe = null;
                RPC_Notice($"{_displayName}: the dealer shuffles a fresh shoe.");
            }

            for (var seat = 0; seat < SeatCount; seat++)
            {
                for (var h = 0; h < MaxHands; h++)
                    ClearHand(Slot(seat, h));

                var occupant = OccupantAt(seat);
                var plays = occupant != SeatOccupant.Empty && SeatLocked[seat] && SeatPendingBets[seat] >= BlackjackRules.MinBet;
                if (!plays)
                {
                    SeatHandCounts.Set(seat, 0);
                    SeatRoundOwners.Set(seat, PlayerRef.None);
                    continue;
                }

                SeatHandCounts.Set(seat, 1);
                SeatRoundOwners.Set(seat, occupant == SeatOccupant.Player ? SeatPlayers[seat] : PlayerRef.None);
                HandBets.Set(Slot(seat, 0), Mathf.Clamp(SeatPendingBets[seat], BlackjackRules.MinBet, BlackjackRules.MaxBet));
            }

            DealerCardCount = 0;
            HoleRevealed = false;
            DealerFinished = false;
            ActiveSeat = -1;
            ActiveHand = 0;
            DealStep = 0;
            PhaseTimer = TickTimer.None;
            Phase = BlackjackPhase.Dealing;
            StepTimer = TickTimer.CreateFromSeconds(Runner, 0.4f);
            Reveal();
        }

        void ClearHand(int slot)
        {
            HandBets.Set(slot, 0);
            HandFlagBits.Set(slot, 0);
            HandOutcomes.Set(slot, 0);
            HandCardCounts.Set(slot, 0);
        }

        Shoe CurrentShoe()
        {
            if (_shoe == null || _shoe.Seed != ShoeSeed || _shoe.DrawIndex != ShoeIndex)
                _shoe = new Shoe(BlackjackRules.Decks, ShoeSeed, ShoeIndex);
            return _shoe;
        }

        byte DrawCard()
        {
            var shoe = CurrentShoe();
            if (shoe.Remaining == 0)
            {
                // Should never happen with a cut card at 75%, but never deadlock the table.
                ShoeSeed = NewSeed();
                ShoeIndex = 0;
                shoe = CurrentShoe();
            }

            var card = shoe.Draw();
            ShoeIndex = (short)shoe.DrawIndex;
            return card;
        }

        void GiveCard(int seat, int hand)
        {
            var slot = Slot(seat, hand);
            var count = HandCardCounts[slot];
            if (count >= MaxCards)
                return;
            HandCards.Set(slot * MaxCards + count, DrawCard());
            HandCardCounts.Set(slot, (byte)(count + 1));
        }

        void GiveDealerCard()
        {
            if (DealerCardCount >= MaxCards)
                return;
            DealerCards.Set(DealerCardCount, DrawCard());
            DealerCardCount++;
        }

        void DealNextCard()
        {
            // Casino order: one card to each playing seat, one to the dealer, then again (the second dealer
            // card is the hole card).
            Span<int> participants = stackalloc int[SeatCount];
            var count = 0;
            for (var seat = 0; seat < SeatCount; seat++)
            {
                if (SeatHandCounts[seat] > 0)
                    participants[count++] = seat;
            }

            var stepsPerPass = count + 1;
            var totalSteps = stepsPerPass * 2;
            var position = DealStep % stepsPerPass;
            if (position < count)
                GiveCard(participants[position], 0);
            else
                GiveDealerCard();
            Reveal();

            DealStep++;
            if (DealStep < totalSteps)
            {
                StepTimer = TickTimer.CreateFromSeconds(Runner, DealInterval);
                return;
            }

            AfterDeal();
        }

        void AfterDeal()
        {
            if (BlackjackRules.DealerShouldPeek(DealerCards[0]) && BlackjackRules.IsNatural(DealerSpan(), false))
            {
                // Show the dealer's blackjack for a moment before settling.
                HoleRevealed = true;
                DealerFinished = true;
                Phase = BlackjackPhase.DealerTurn;
                ActiveSeat = -1;
                PhaseTimer = TickTimer.None;
                StepTimer = TickTimer.CreateFromSeconds(Runner, GameTables.ResultPause);
                Reveal();
                RPC_Notice($"{_displayName}: dealer has blackjack.");
                return;
            }

            ActiveSeat = 0;
            ActiveHand = 0;
            AdvanceTurn();
        }

        bool IsDone(int seat, int hand) => (HandFlagsAt(seat, hand) & HandFlags.Done) != 0;

        void MarkDone(int seat, int hand)
        {
            var slot = Slot(seat, hand);
            HandFlagBits.Set(slot, (byte)(HandFlagBits[slot] | (byte)HandFlags.Done));
        }

        /// <summary>Moves the turn to the next hand that still needs a decision, or on to the dealer.</summary>
        void AdvanceTurn()
        {
            Phase = BlackjackPhase.PlayerTurns;
            for (var seat = Mathf.Max(0, ActiveSeat); seat < SeatCount; seat++)
            {
                var firstHand = seat == ActiveSeat ? ActiveHand : 0;
                for (var hand = firstHand; hand < SeatHandCounts[seat]; hand++)
                {
                    if (IsDone(seat, hand))
                        continue;

                    // Naturals and 21s need no decision.
                    if (EvaluateHand(seat, hand).Total >= 21)
                    {
                        MarkDone(seat, hand);
                        continue;
                    }

                    ActiveSeat = (sbyte)seat;
                    ActiveHand = (byte)hand;
                    StartTurnTimers();
                    return;
                }
            }

            EnterDealerTurn();
        }

        void StartTurnTimers()
        {
            // The turn starts when the player sees it, so the reveal delay doesn't eat into their time.
            var delay = Reveal();
            var seconds = TurnTimerSeconds;
            PhaseTimer = seconds > 0 ? TickTimer.CreateFromSeconds(Runner, seconds + delay) : TickTimer.None;
            StepTimer = TickTimer.CreateFromSeconds(Runner, UnityEngine.Random.Range(NpcThinkMin, NpcThinkMax));
        }

        void TickPlayerTurns()
        {
            if (ActiveSeat < 0)
            {
                AdvanceTurn();
                return;
            }

            var seat = ActiveSeat;
            var owner = SeatRoundOwners[seat];
            if (owner == PlayerRef.None)
            {
                // NPC hand: basic strategy after a short "thinking" pause.
                if (StepTimer.ExpiredOrNotRunning(Runner))
                    ApplyAction(seat, DecideNpcAction(seat));
                return;
            }

            var ownerStillSeated = OccupantAt(seat) == SeatOccupant.Player && SeatPlayers[seat] == owner;
            if (!ownerStillSeated || (PhaseTimer.IsRunning && PhaseTimer.Expired(Runner)))
                ApplyAction(seat, BlackjackAction.Stand);
        }

        BlackjackAction DecideNpcAction(int seat)
        {
            var hand = HandSpan(seat, ActiveHand).ToArray();
            return BasicStrategy.Decide(hand, DealerCards[0], CanDouble(seat), CanSplit(seat));
        }

        void ApplyAction(int seat, BlackjackAction action)
        {
            var hand = ActiveHand;
            var slot = Slot(seat, hand);

            switch (action)
            {
                case BlackjackAction.Hit:
                    GiveCard(seat, hand);
                    if (EvaluateHand(seat, hand).Total >= 21)
                        MarkDone(seat, hand);
                    break;

                case BlackjackAction.Stand:
                    MarkDone(seat, hand);
                    break;

                case BlackjackAction.Double:
                    if (!CanDouble(seat))
                        return;
                    HandBets.Set(slot, HandBets[slot] * 2);
                    HandFlagBits.Set(slot, (byte)(HandFlagBits[slot] | (byte)HandFlags.Doubled));
                    GiveCard(seat, hand);
                    MarkDone(seat, hand);
                    break;

                case BlackjackAction.Split:
                    if (!CanSplit(seat))
                        return;
                    Split(seat, hand);
                    break;
            }

            Reveal();
            if (IsDone(seat, hand))
                AdvanceTurn();
            else
                StartTurnTimers();
        }

        void Split(int seat, int hand)
        {
            var slot = Slot(seat, hand);
            var newHand = SeatHandCounts[seat];
            var newSlot = Slot(seat, newHand);
            var secondCard = HandCards[slot * MaxCards + 1];
            var aces = Cards.Rank(HandCards[slot * MaxCards]) == Cards.Ace;

            HandCardCounts.Set(slot, 1);
            HandCards.Set(newSlot * MaxCards, secondCard);
            HandCardCounts.Set(newSlot, 1);
            HandBets.Set(newSlot, HandBets[slot]);

            var flags = (byte)(HandFlags.FromSplit | (aces ? HandFlags.SplitAces : HandFlags.None));
            HandFlagBits.Set(slot, flags);
            HandFlagBits.Set(newSlot, flags);
            HandOutcomes.Set(newSlot, 0);
            SeatHandCounts.Set(seat, (byte)(newHand + 1));

            GiveCard(seat, hand);
            GiveCard(seat, newHand);

            if (aces)
            {
                MarkDone(seat, hand);
                MarkDone(seat, newHand);
            }
        }

        void EnterDealerTurn()
        {
            Phase = BlackjackPhase.DealerTurn;
            ActiveSeat = -1;
            PhaseTimer = TickTimer.None;
            StepTimer = TickTimer.CreateFromSeconds(Runner, 0.5f);
            Reveal();
        }

        void DealerStep()
        {
            if (!HoleRevealed)
            {
                HoleRevealed = true;
                StepTimer = TickTimer.CreateFromSeconds(Runner, DealerDrawInterval);
                Reveal();
                return;
            }

            if (!DealerFinished && AnyLiveHand() && BlackjackRules.DealerShouldHit(DealerSpan()))
            {
                GiveDealerCard();
                StepTimer = TickTimer.CreateFromSeconds(Runner, DealerDrawInterval);
                Reveal();
                return;
            }

            // A beat to see the dealer's final hand before the results (the last draw already waited a bit).
            if (!DealerFinished)
            {
                DealerFinished = true;
                StepTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, GameTables.ResultPause - DealerDrawInterval));
                return;
            }

            EnterSettlement();
        }

        /// <summary>The dealer only draws if some hand is still waiting on the dealer's total.</summary>
        bool AnyLiveHand()
        {
            for (var seat = 0; seat < SeatCount; seat++)
            {
                for (var hand = 0; hand < SeatHandCounts[seat]; hand++)
                {
                    var flags = HandFlagsAt(seat, hand);
                    var span = HandSpan(seat, hand);
                    if (!BlackjackRules.Evaluate(span).IsBust && !BlackjackRules.IsNatural(span, (flags & HandFlags.FromSplit) != 0))
                        return true;
                }
            }

            return false;
        }

        void EnterSettlement()
        {
            var dealer = DealerSpan().ToArray();
            for (var seat = 0; seat < SeatCount; seat++)
            {
                for (var hand = 0; hand < SeatHandCounts[seat]; hand++)
                {
                    var fromSplit = (HandFlagsAt(seat, hand) & HandFlags.FromSplit) != 0;
                    var outcome = BlackjackRules.Settle(HandSpan(seat, hand), fromSplit, dealer);
                    HandOutcomes.Set(Slot(seat, hand), (byte)outcome);
                }
            }

            ActiveSeat = -1;
            SettledRoundId = RoundId;
            Phase = BlackjackPhase.Settlement;
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, SettlementDuration);
            Reveal();
        }

        void FinishRound()
        {
            for (var seat = 0; seat < SeatCount; seat++)
            {
                SeatHandCounts.Set(seat, 0);
                SeatRoundOwners.Set(seat, PlayerRef.None);
            }

            DealerCardCount = 0;
            HoleRevealed = false;
            ActiveSeat = -1;
            Reveal();

            if (HumanCount() > 0)
                EnterBetting();
            else
                Phase = BlackjackPhase.WaitingForPlayers;
        }

        /// <summary>NPC cats come and go between rounds, per the table setting (GDD §5.6, <see cref="NpcSeating"/>).</summary>
        void MaintainNpcs()
        {
            if (IsInRound)
                return;

            Span<byte> occupants = stackalloc byte[SeatCount];
            for (var i = 0; i < SeatCount; i++)
                occupants[i] = SeatOccupants[i];
            var crew = Crew;
            var change = NpcSeating.Plan(Runner, ref crew, NpcFill, occupants, out var seat, out var join, out var instant);
            Crew = crew;
            if (!change)
                return;

            SeatLocked.Set(seat, false);
            SeatPendingBets.Set(seat, 0);
            if (!join)
            {
                SeatOccupants.Set(seat, (byte)SeatOccupant.Empty);
                SeatArrivals.Set(seat, TickTimer.None);
                return;
            }

            SeatOccupants.Set(seat, (byte)SeatOccupant.Npc);
            SeatPlayers.Set(seat, PlayerRef.None);
            SeatNpcFur.Set(seat, (byte)UnityEngine.Random.Range(0, 6));
            SeatArrivals.Set(seat, instant ? TickTimer.None : TickTimer.CreateFromSeconds(Runner, NpcSeating.ArrivalSeconds(_seatAnchors[seat])));
            if (Phase == BlackjackPhase.Betting && NpcPresent(seat))
                LockNpcBet(seat);
        }

        void IPlayerLeft.PlayerLeft(PlayerRef player)
        {
            if (!HasStateAuthority)
                return;

            for (var i = 0; i < SeatCount; i++)
            {
                if (OccupantAt(i) == SeatOccupant.Player && SeatPlayers[i] == player)
                    VacateSeat(i);
            }
        }

        void VacateSeat(int seat)
        {
            SeatOccupants.Set(seat, (byte)SeatOccupant.Empty);
            SeatPlayers.Set(seat, PlayerRef.None);
            SeatPendingBets.Set(seat, 0);
            SeatLocked.Set(seat, false);
            // Hands already in play stay; the round auto-stands them and they settle normally (GDD §5.4).
        }

        // ---------------------------------------------------------------- RPCs from players

        public void RequestSeat(int seat) => RPC_RequestSeat((byte)seat);

        public void PlaceBet(int seat, int amount, bool lockIn) => RPC_SetBet((byte)seat, amount, lockIn);

        /// <summary>
        /// <paramref name="hand"/> is the hand the player saw as active (-1: don't check). The shown round runs a moment
        /// behind the live one, so this keeps a late second click from landing on the next hand after a split.
        /// </summary>
        public void Act(int seat, BlackjackAction action, int hand = -1) => RPC_Act((byte)seat, (byte)action, hand);

        public void ChangeSettings(int turnOption, int betOption, int npcFill) =>
            RPC_SetSettings((byte)turnOption, (byte)betOption, (byte)npcFill);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_RequestSeat(byte seat, RpcInfo info = default)
        {
            if (seat >= SeatCount || TryGetSeatOf(info.Source, out _))
                return;

            var occupant = OccupantAt(seat);
            var npcCanMove = occupant == SeatOccupant.Npc && SeatHandCounts[seat] == 0;
            if (occupant != SeatOccupant.Empty && !npcCanMove)
                return;

            SeatOccupants.Set(seat, (byte)SeatOccupant.Player);
            SeatPlayers.Set(seat, info.Source);
            SeatArrivals.Set(seat, TickTimer.None);
            SeatPendingBets.Set(seat, 0);
            SeatLocked.Set(seat, false);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_LeaveSeat(byte seat, RpcInfo info = default)
        {
            if (seat < SeatCount && OccupantAt(seat) == SeatOccupant.Player && SeatPlayers[seat] == info.Source)
                VacateSeat(seat);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_SetBet(byte seat, int amount, NetworkBool lockIn, RpcInfo info = default)
        {
            if (Phase != BlackjackPhase.Betting || seat >= SeatCount || SeatPlayers[seat] != info.Source || OccupantAt(seat) != SeatOccupant.Player)
                return;

            amount = amount <= 0 ? 0 : Mathf.Clamp(amount, BlackjackRules.MinBet, BlackjackRules.MaxBet);
            SeatPendingBets.Set(seat, amount);
            SeatLocked.Set(seat, lockIn);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_Act(byte seat, byte action, int hand, RpcInfo info = default)
        {
            if (Phase != BlackjackPhase.PlayerTurns || ActiveSeat != seat || SeatRoundOwners[seat] != info.Source)
                return;
            if (hand >= 0 && hand != ActiveHand)
                return;
            if (action > (byte)BlackjackAction.Split)
                return;
            ApplyAction(seat, (BlackjackAction)action);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_SetSettings(byte turnOption, byte betOption, byte npcFill, RpcInfo info = default)
        {
            if (!TryGetSeatOf(info.Source, out _))
                return;

            TurnTimerOption = (byte)Mathf.Clamp(turnOption, 0, TurnTimerOptions.Length - 1);
            BetTimerOption = (byte)Mathf.Clamp(betOption, 0, BetTimerOptions.Length - 1);
            NpcFill = NpcSeating.Clamp(npcFill);

            string Describe(int seconds) => seconds == 0 ? "off" : $"{seconds}s";
            RPC_Notice($"{PlayerCat.NameOf(info.Source)} set {_displayName}: turn timer {Describe(TurnTimerSeconds)}, " +
                       $"betting timer {Describe(BetTimerSeconds)}, NPC players {NpcSeating.Describe(NpcFill)}.");
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_Notice(string text)
        {
            ChatLog.AddSystem(text);
        }

        // ---------------------------------------------------------------- Every client: chips & events

        public override void Render()
        {
            // Chips, toasts and reactions follow the shown round, so nobody hears about a result before its card.
            var shown = Shown;
            UpdateLocalWallet(shown);

            if (shown.SettledRoundId != _lastSeenSettledRound)
            {
                _lastSeenSettledRound = shown.SettledRoundId;
                if (shown.Phase == BlackjackPhase.Settlement)
                    OnRoundSettledLocally(shown);
            }
        }

        /// <summary>
        /// GDD §9.4: the local client takes chips out when its wager grows (bet placed, double, split) and pays
        /// itself once per round when the results are in. Everything is derived from networked state, so no
        /// double-spends even if messages arrive in odd orders.
        /// </summary>
        void UpdateLocalWallet(Snapshot shown)
        {
            if (Runner == null)
                return;

            var local = Runner.LocalPlayer;
            var seat = -1;
            for (var i = 0; i < SeatCount; i++)
            {
                if (shown.RoundOwnerAt(i) == local && shown.HandCountAt(i) > 0)
                {
                    seat = i;
                    break;
                }
            }

            if (seat < 0)
                return;

            if (_walletRound != shown.RoundId)
            {
                _walletRound = shown.RoundId;
                _walletCharged = 0;
            }

            var wager = shown.TotalWagerAt(seat);
            if (wager > _walletCharged)
            {
                ProfileStore.SetBalance(ProfileStore.Current.Balance - (wager - _walletCharged));
                _walletCharged = wager;
            }

            if (shown.SettledRoundId == shown.RoundId && _walletPaidRound != shown.RoundId)
            {
                _walletPaidRound = shown.RoundId;
                var payout = 0;
                for (var h = 0; h < shown.HandCountAt(seat); h++)
                    payout += BlackjackRules.Payout(shown.OutcomeAt(seat, h), shown.HandBetAt(seat, h));

                if (payout > 0)
                    ProfileStore.SetBalance(ProfileStore.Current.Balance + payout);

                var net = payout - _walletCharged;
                LocalRoundResult?.Invoke(this, net);
            }
        }

        void OnRoundSettledLocally(Snapshot shown)
        {
            RoundSettled?.Invoke();

            for (var seat = 0; seat < SeatCount; seat++)
            {
                var net = 0;
                for (var h = 0; h < shown.HandCountAt(seat); h++)
                    net += BlackjackRules.Payout(shown.OutcomeAt(seat, h), shown.HandBetAt(seat, h)) - shown.HandBetAt(seat, h);
                if (net >= 250)
                {
                    BigWin?.Invoke(transform.position);
                    break;
                }
            }
        }
    }
}
