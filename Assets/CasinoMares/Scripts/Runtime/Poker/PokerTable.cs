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

namespace CasinoMares.Poker
{
    public enum PokerPhase : byte
    {
        WaitingForPlayers,
        PreFlop,
        Flop,
        Turn,
        River,
        Showdown,
    }

    public enum PokerOccupant : byte
    {
        Empty,
        Player,
        Npc,
    }

    public enum PokerAction : byte
    {
        None,
        Fold,
        Check,
        Call,
        Raise,
        AllIn,
        SmallBlind,
        BigBlind,
    }

    [Flags]
    public enum PokerSeatFlags : byte
    {
        None = 0,
        InHand = 1,
        Folded = 2,
        AllIn = 4,
        Acted = 8,
    }

    /// <summary>
    /// No-limit Texas Hold'em (GDD §13.2): players against each other, optional NPC bots, no house edge.
    /// Hole cards are private: the authority keeps them in local memory and sends each player theirs through a
    /// targeted RPC; they only become networked state for hands that reach a showdown. If authority moves
    /// mid-hand (the master left) the new authority can't know the hole cards, so the hand is voided and every
    /// contribution is refunded.
    /// </summary>
    public class PokerTable : NetworkBehaviour, IGameTable, IPlayerLeft, IStateAuthorityChanged
    {
        public const int SeatCount = 6;
        public const int MinBuyIn = 100;
        public const int MaxBuyIn = 1000;
        public const int NpcStack = 500;

        public static readonly int[] SmallBlindOptions = { 5, 10, 25 };
        public static readonly byte[] TurnTimerOptions = { 15, 30, 45, 60 };

        const float StreetPause = 0.9f;
        const float ShowdownDuration = 6f;
        const float NewHandDelay = 2.5f;
        const float NpcThinkMin = 0.9f;
        const float NpcThinkMax = 2f;

        public static readonly List<PokerTable> All = new();

        /// <summary>Raised on the local client when a hand it played ends. Arg: net chips won (can be negative).</summary>
        public static event Action<PokerTable, int> LocalHandResult;

        [SerializeField] string _displayName = "Poker";
        [SerializeField] Transform[] _seatAnchors = Array.Empty<Transform>();
        [SerializeField] CinemachineCamera[] _seatCameras = Array.Empty<CinemachineCamera>();
        [SerializeField] CinemachineCamera _overheadCamera;

        // ---------------------------------------------------------------- Networked state

        [Networked] NetworkBool Initialized { get; set; }
        [Networked] public PokerPhase Phase { get; set; }
        [Networked] public int HandId { get; set; }
        [Networked] public sbyte ButtonSeat { get; set; }
        [Networked] public sbyte ActiveSeat { get; set; }
        [Networked] public int CurrentBet { get; set; }
        [Networked] public int MinRaise { get; set; }
        [Networked] public byte BlindOption { get; set; }
        [Networked] public byte TurnTimerOption { get; set; }
        [Networked] public byte NpcFill { get; set; }
        [Networked] NpcCrew Crew { get; set; }
        [Networked] TickTimer PhaseTimer { get; set; }
        [Networked] TickTimer StepTimer { get; set; }
        [Networked] public byte CommunityCount { get; set; }
        [Networked, Capacity(5)] NetworkArray<byte> Community => default;
        [Networked] public NetworkString<_64> LastResult { get; set; }
        /// <summary>
        /// At a showdown the cards are turned over first and the pot is awarded <see cref="GameTables.ResultPause"/>
        /// later; this turns true then (and right away when everyone else folded).
        /// </summary>
        [Networked] public NetworkBool ShowdownSettled { get; set; }
        /// <summary>When everyone sees the latest hand event (<see cref="TableReveal"/>).</summary>
        [Networked] int RevealTick { get; set; }

        [Networked, Capacity(SeatCount)] NetworkArray<byte> SeatOccupants => default;
        [Networked, Capacity(SeatCount)] NetworkArray<PlayerRef> SeatPlayers => default;
        [Networked, Capacity(SeatCount)] NetworkArray<byte> SeatNpcFur => default;
        [Networked, Capacity(SeatCount)] NetworkArray<TickTimer> SeatArrivals => default;
        [Networked, Capacity(SeatCount)] NetworkArray<int> Stacks => default;
        [Networked, Capacity(SeatCount)] NetworkArray<int> RoundBets => default;
        [Networked, Capacity(SeatCount)] NetworkArray<int> Contributed => default;
        [Networked, Capacity(SeatCount)] NetworkArray<byte> Flags => default;
        [Networked, Capacity(SeatCount)] NetworkArray<byte> LastActions => default;
        [Networked, Capacity(SeatCount)] NetworkArray<int> LastAmounts => default;
        [Networked, Capacity(SeatCount)] NetworkArray<int> WonAmounts => default;
        [Networked, Capacity(SeatCount * 2)] NetworkArray<byte> ShownCards => default;
        [Networked, Capacity(SeatCount)] NetworkArray<int> BuyIns => default;
        [Networked, Capacity(SeatCount)] NetworkArray<int> Sessions => default;

        // Authority-only secrets.
        readonly byte[] _hole = new byte[SeatCount * 2];
        Shoe _deck;

        // Local player's own hole cards, received privately.
        readonly byte[] _myHole = { Cards.None, Cards.None };
        int _myHoleHand = -1;

        // Local wallet: buy-ins leave the balance, the last seen stack comes back on leaving (GDD §9.4).
        int _chargedSession;
        int _lastSeenStack;
        bool _walletSeated;
        int _reportedHand = -1;

        RevealQueue<Snapshot> _reveal;
        int _stampedTick = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            LocalHandResult = null;
        }

        // ---------------------------------------------------------------- Read API

        public string DisplayName => _displayName;
        public int SmallBlind => SmallBlindOptions[Mathf.Clamp(BlindOption, 0, SmallBlindOptions.Length - 1)];
        public int BigBlind => SmallBlind * 2;
        public int TurnTimerSeconds => TurnTimerOptions[Mathf.Clamp(TurnTimerOption, 0, TurnTimerOptions.Length - 1)];
        public PokerOccupant OccupantAt(int seat) => (PokerOccupant)SeatOccupants[seat];
        public PlayerRef PlayerAt(int seat) => SeatPlayers[seat];
        public int NpcFurAt(int seat) => SeatNpcFur[seat];

        /// <summary>Seconds until the NPC in this seat, still walking in from the entrance, sits down (0 once seated).</summary>
        public float NpcArrivalAt(int seat) => SeatArrivals[seat].IsRunning && Runner != null ? Mathf.Max(0f, SeatArrivals[seat].RemainingTime(Runner) ?? 0f) : 0f;

        /// <summary>An NPC sits here and has arrived, so it plays.</summary>
        bool NpcPresent(int seat) => OccupantAt(seat) == PokerOccupant.Npc && SeatArrivals[seat].ExpiredOrNotRunning(Runner);
        public int StackAt(int seat) => Stacks[seat];
        public int RoundBetAt(int seat) => RoundBets[seat];
        public int ContributedAt(int seat) => Contributed[seat];
        public PokerSeatFlags FlagsAt(int seat) => (PokerSeatFlags)Flags[seat];
        public PokerAction LastActionAt(int seat) => (PokerAction)LastActions[seat];
        public int LastAmountAt(int seat) => LastAmounts[seat];
        public int WonAt(int seat) => WonAmounts[seat];
        public byte CommunityAt(int index) => Community[index];
        public byte ShownCardAt(int seat, int index) => ShownCards[seat * 2 + index];
        public Transform GetSeatAnchor(int seat) => _seatAnchors[seat];
        public CinemachineCamera GetSeatCamera(int seat) => _seatCameras[seat];
        public CinemachineCamera GetOverheadCamera() => _overheadCamera;

        public bool IsActiveTurn(int seat) => IsBettingPhase && ActiveSeat == seat;
        public bool IsBettingPhase => Phase is PokerPhase.PreFlop or PokerPhase.Flop or PokerPhase.Turn or PokerPhase.River;
        public int ToCall(int seat) => Mathf.Max(0, CurrentBet - RoundBets[seat]);

        public int TotalPot
        {
            get
            {
                var total = 0;
                for (var i = 0; i < SeatCount; i++)
                    total += Contributed[i];
                return total;
            }
        }

        public int MinRaiseTo => CurrentBet == 0 ? BigBlind : CurrentBet + Mathf.Max(MinRaise, BigBlind);

        public bool LocalSeated => Runner != null && TryGetSeatOf(Runner.LocalPlayer, out _);

        public static bool AnyLocalChipsAtTable()
        {
            foreach (var table in All)
            {
                if (table != null && table.Object != null && table.Object.IsValid && table.LocalSeated)
                    return true;
            }

            return false;
        }

        // ---------------------------------------------------------------- What players see (every client)

        /// <summary>
        /// The hand as players see it right now: the networked hand state, with each event (a bet, a fold, the turn
        /// passing, the flop, the showdown) held back until its reveal tick so it happens on every screen at once.
        /// Views, HUDs and the coach read this; the hand logic reads the live state. Seats, buy-ins and settings
        /// stay live, and so does the stack the wallet cashes out.
        /// </summary>
        public Snapshot Shown => (_reveal ??= new RevealQueue<Snapshot>(Capture)).Get(Runner, RevealTick);

        public sealed class Snapshot
        {
            public PokerPhase Phase;
            public int HandId;
            public sbyte ButtonSeat;
            public sbyte ActiveSeat;
            public int CurrentBet;
            public int MinRaise;
            public int SmallBlind;
            public byte CommunityCount;
            public NetworkString<_64> LastResult;
            public bool ShowdownSettled;
            internal PokerTable Table;
            internal NetworkRunner Runner;
            internal TickTimer PhaseTimer;
            internal readonly byte[] Community = new byte[5];
            internal readonly int[] Stacks = new int[SeatCount];
            internal readonly int[] RoundBets = new int[SeatCount];
            internal readonly int[] Contributed = new int[SeatCount];
            internal readonly byte[] Flags = new byte[SeatCount];
            internal readonly byte[] LastActions = new byte[SeatCount];
            internal readonly int[] LastAmounts = new int[SeatCount];
            internal readonly int[] WonAmounts = new int[SeatCount];
            internal readonly byte[] ShownCards = new byte[SeatCount * 2];

            public int BigBlind => SmallBlind * 2;
            public int StackAt(int seat) => Stacks[seat];
            public int RoundBetAt(int seat) => RoundBets[seat];
            public int ContributedAt(int seat) => Contributed[seat];
            public PokerSeatFlags FlagsAt(int seat) => (PokerSeatFlags)Flags[seat];
            public PokerAction LastActionAt(int seat) => (PokerAction)LastActions[seat];
            public int LastAmountAt(int seat) => LastAmounts[seat];
            public int WonAt(int seat) => WonAmounts[seat];
            public byte CommunityAt(int index) => Community[index];
            public byte ShownCardAt(int seat, int index) => ShownCards[seat * 2 + index];

            public bool IsInHand(int seat) => (FlagsAt(seat) & PokerSeatFlags.InHand) != 0 && (FlagsAt(seat) & PokerSeatFlags.Folded) == 0;
            public bool IsActiveTurn(int seat) => IsBettingPhase && ActiveSeat == seat;
            public bool IsBettingPhase => Phase is PokerPhase.PreFlop or PokerPhase.Flop or PokerPhase.Turn or PokerPhase.River;
            public int ToCall(int seat) => Mathf.Max(0, CurrentBet - RoundBets[seat]);
            public int MinRaiseTo => CurrentBet == 0 ? BigBlind : CurrentBet + Mathf.Max(MinRaise, BigBlind);
            public float TimeRemaining => PhaseTimer.IsRunning && Runner != null ? Mathf.Max(0f, PhaseTimer.RemainingTime(Runner) ?? 0f) : -1f;

            /// <summary>Chips in the middle, not counting bets still in front of players this street.</summary>
            public int PotInMiddle
            {
                get
                {
                    var total = 0;
                    for (var i = 0; i < SeatCount; i++)
                        total += Contributed[i] - RoundBets[i];
                    return total;
                }
            }

            public int TotalPot
            {
                get
                {
                    var total = 0;
                    for (var i = 0; i < SeatCount; i++)
                        total += Contributed[i];
                    return total;
                }
            }

            /// <summary>The local player's own hole cards, once the hand they were dealt for is on screen.</summary>
            public bool TryGetMyHoleCards(out byte first, out byte second)
            {
                first = Table._myHole[0];
                second = Table._myHole[1];
                return Table._myHoleHand == HandId && first != Cards.None && Phase != PokerPhase.WaitingForPlayers;
            }
        }

        void Capture(Snapshot shown)
        {
            shown.Phase = Phase;
            shown.HandId = HandId;
            shown.ButtonSeat = ButtonSeat;
            shown.ActiveSeat = ActiveSeat;
            shown.CurrentBet = CurrentBet;
            shown.MinRaise = MinRaise;
            shown.SmallBlind = SmallBlind;
            shown.CommunityCount = CommunityCount;
            shown.LastResult = LastResult;
            shown.ShowdownSettled = ShowdownSettled;
            shown.Table = this;
            shown.Runner = Runner;
            shown.PhaseTimer = PhaseTimer;
            Community.CopyTo(shown.Community, false);
            Stacks.CopyTo(shown.Stacks, false);
            RoundBets.CopyTo(shown.RoundBets, false);
            Contributed.CopyTo(shown.Contributed, false);
            Flags.CopyTo(shown.Flags, false);
            LastActions.CopyTo(shown.LastActions, false);
            LastAmounts.CopyTo(shown.LastAmounts, false);
            WonAmounts.CopyTo(shown.WonAmounts, false);
            ShownCards.CopyTo(shown.ShownCards, false);
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
                BlindOption = 0;
                TurnTimerOption = 1;
                NpcFill = NpcSeating.Auto;
                ButtonSeat = -1;
                ActiveSeat = -1;
                for (var i = 0; i < SeatCount * 2; i++)
                    ShownCards.Set(i, Cards.None);
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            GameTables.All.Remove(this);

            // Quitting or disconnecting while seated: cash out the last stack we saw so it isn't lost.
            if (_walletSeated && _lastSeenStack > 0)
                ProfileStore.SetBalance(ProfileStore.Current.Balance + _lastSeenStack);
            _walletSeated = false;
            _lastSeenStack = 0;
        }

        void IStateAuthorityChanged.StateAuthorityChanged()
        {
            // The previous authority took the hole cards with it; void the hand and refund everyone.
            if (HasStateAuthority && Phase != PokerPhase.WaitingForPlayers && Phase != PokerPhase.Showdown)
                VoidHand("The dealer changed mid-hand, so the hand is void and every bet is returned.");
        }

        // ---------------------------------------------------------------- IGameTable

        public bool TryGetSeatOf(PlayerRef player, out int seat)
        {
            seat = -1;
            if (Object == null || !Object.IsValid || player == PlayerRef.None)
                return false;
            for (var i = 0; i < SeatCount; i++)
            {
                if (OccupantAt(i) == PokerOccupant.Player && SeatPlayers[i] == player)
                {
                    seat = i;
                    return true;
                }
            }

            return false;
        }

        public void RequestLeave(int seat) => RPC_Leave((byte)seat);

        public bool TryDescribePlayer(PlayerRef player, StringBuilder text)
        {
            if (!TryGetSeatOf(player, out var seat))
                return false;

            var shown = Shown;
            text.AppendLine($"{_displayName} · seat {seat + 1}");
            text.AppendLine($"Stack {shown.StackAt(seat):N0}" + (shown.ContributedAt(seat) > 0 ? $" · in pot {shown.ContributedAt(seat):N0}" : ""));
            var flags = shown.FlagsAt(seat);
            if ((flags & PokerSeatFlags.Folded) != 0)
                text.AppendLine("Folded");
            else if ((flags & PokerSeatFlags.AllIn) != 0)
                text.AppendLine("All in!");
            else if (shown.LastActionAt(seat) != PokerAction.None)
                text.AppendLine(ActionLabel(shown.LastActionAt(seat), shown.LastAmountAt(seat)));
            if (shown.IsActiveTurn(seat))
                text.AppendLine($"Thinking... {Mathf.CeilToInt(shown.TimeRemaining)}s");
            if (shown.ShownCardAt(seat, 0) != Cards.None)
                text.AppendLine($"Shows {Cards.Label(shown.ShownCardAt(seat, 0))} {Cards.Label(shown.ShownCardAt(seat, 1))}");
            if (shown.WonAt(seat) > 0)
                text.AppendLine($"Won {shown.WonAt(seat):N0}");
            return true;
        }

        public static string ActionLabel(PokerAction action, int amount) => action switch
        {
            PokerAction.Fold => "Fold",
            PokerAction.Check => "Check",
            PokerAction.Call => $"Call {amount:N0}",
            PokerAction.Raise => $"Raise to {amount:N0}",
            PokerAction.AllIn => $"All in {amount:N0}",
            PokerAction.SmallBlind => $"Small blind {amount:N0}",
            PokerAction.BigBlind => $"Big blind {amount:N0}",
            _ => "",
        };

        // ---------------------------------------------------------------- Authority: flow

        public override void FixedUpdateNetwork()
        {
            switch (Phase)
            {
                case PokerPhase.WaitingForPlayers:
                    MaintainNpcs();
                    if (CanStartHand())
                    {
                        if (!StepTimer.IsRunning)
                            StepTimer = TickTimer.CreateFromSeconds(Runner, NewHandDelay);
                        else if (StepTimer.Expired(Runner))
                            StartHand();
                    }
                    else
                    {
                        StepTimer = TickTimer.None;
                    }

                    break;

                case PokerPhase.Showdown:
                    if (!PhaseTimer.ExpiredOrNotRunning(Runner))
                        break;
                    if (ShowdownSettled)
                        EndHand();
                    else
                        AwardShowdown();
                    break;

                default:
                    TickBetting();
                    break;
            }
        }

        bool CanStartHand()
        {
            var players = 0;
            var humans = 0;
            for (var i = 0; i < SeatCount; i++)
            {
                if (!CanBeDealtIn(i))
                    continue;
                players++;
                if (OccupantAt(i) == PokerOccupant.Player)
                    humans++;
            }

            return players >= 2 && humans >= 1;
        }

        /// <summary>A seated player, or an NPC that has reached its seat, with chips to play.</summary>
        bool CanBeDealtIn(int seat) => (OccupantAt(seat) == PokerOccupant.Player || NpcPresent(seat)) && Stacks[seat] > 0;

        void StartHand()
        {
            HandId++;
            LastResult = "";
            ShowdownSettled = false;
            CommunityCount = 0;
            CurrentBet = 0;
            MinRaise = BigBlind;
            for (var i = 0; i < SeatCount; i++)
            {
                var dealtIn = CanBeDealtIn(i);
                Flags.Set(i, (byte)(dealtIn ? PokerSeatFlags.InHand : PokerSeatFlags.None));
                RoundBets.Set(i, 0);
                Contributed.Set(i, 0);
                LastActions.Set(i, 0);
                LastAmounts.Set(i, 0);
                WonAmounts.Set(i, 0);
                ShownCards.Set(i * 2, Cards.None);
                ShownCards.Set(i * 2 + 1, Cards.None);
            }

            ButtonSeat = (sbyte)NextSeat(ButtonSeat, s => HasFlag(s, PokerSeatFlags.InHand));
            var headsUp = CountSeats(s => HasFlag(s, PokerSeatFlags.InHand)) == 2;
            var smallBlindSeat = headsUp ? ButtonSeat : NextSeat(ButtonSeat, s => HasFlag(s, PokerSeatFlags.InHand));
            var bigBlindSeat = NextSeat(smallBlindSeat, s => HasFlag(s, PokerSeatFlags.InHand));
            Pay(smallBlindSeat, SmallBlind, PokerAction.SmallBlind);
            Pay(bigBlindSeat, BigBlind, PokerAction.BigBlind);
            CurrentBet = Mathf.Max(RoundBets[smallBlindSeat], RoundBets[bigBlindSeat]);

            // A fresh private deck every hand. The seed never leaves this machine.
            _deck = new Shoe(1, new System.Random().Next(1, int.MaxValue));
            for (var i = 0; i < SeatCount; i++)
            {
                _hole[i * 2] = Cards.None;
                _hole[i * 2 + 1] = Cards.None;
                if (!HasFlag(i, PokerSeatFlags.InHand))
                    continue;
                _hole[i * 2] = _deck.Draw();
                _hole[i * 2 + 1] = _deck.Draw();
                if (OccupantAt(i) == PokerOccupant.Player)
                    RPC_DealHoleCards(SeatPlayers[i], HandId, _hole[i * 2], _hole[i * 2 + 1]);
            }

            Phase = PokerPhase.PreFlop;
            ActiveSeat = (sbyte)bigBlindSeat;
            Reveal();
            AdvanceTurn();
        }

        void TickBetting()
        {
            if (ActiveSeat < 0)
            {
                // Everyone left is all in (or only one can act): run the board out slowly.
                if (StepTimer.ExpiredOrNotRunning(Runner))
                    NextStreet();
                return;
            }

            var seat = ActiveSeat;
            if (OccupantAt(seat) == PokerOccupant.Npc)
            {
                if (StepTimer.ExpiredOrNotRunning(Runner))
                    DecideNpc(seat);
                return;
            }

            // The player left, or ran out of time: check if free, otherwise fold.
            if (OccupantAt(seat) != PokerOccupant.Player || (PhaseTimer.IsRunning && PhaseTimer.Expired(Runner)))
                Apply(seat, ToCall(seat) == 0 ? PokerAction.Check : PokerAction.Fold, 0);
        }

        /// <summary>Gives the turn to the next player who can act, or ends the street.</summary>
        void AdvanceTurn()
        {
            if (CountSeats(IsLive) <= 1)
            {
                AwardUncontested();
                return;
            }

            var next = NextSeat(ActiveSeat, NeedsToAct);
            if (next < 0)
            {
                ActiveSeat = -1;
                PhaseTimer = TickTimer.None;
                StepTimer = TickTimer.CreateFromSeconds(Runner, StreetPause);
                Reveal();
                return;
            }

            ActiveSeat = (sbyte)next;
            // The turn starts when the player sees it, so the reveal delay doesn't eat into their time.
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, TurnTimerSeconds + Reveal());
            StepTimer = TickTimer.CreateFromSeconds(Runner, UnityEngine.Random.Range(NpcThinkMin, NpcThinkMax));
        }

        bool IsLive(int seat) => HasFlag(seat, PokerSeatFlags.InHand) && !HasFlag(seat, PokerSeatFlags.Folded);

        bool CanAct(int seat) => IsLive(seat) && !HasFlag(seat, PokerSeatFlags.AllIn);

        bool NeedsToAct(int seat) => CanAct(seat) && (!HasFlag(seat, PokerSeatFlags.Acted) || RoundBets[seat] < CurrentBet);

        void NextStreet()
        {
            // Bets move to the middle.
            for (var i = 0; i < SeatCount; i++)
            {
                RoundBets.Set(i, 0);
                SetFlag(i, PokerSeatFlags.Acted, false);
            }

            CurrentBet = 0;
            MinRaise = BigBlind;

            switch (Phase)
            {
                case PokerPhase.PreFlop:
                    DealCommunity(3);
                    Phase = PokerPhase.Flop;
                    break;
                case PokerPhase.Flop:
                    DealCommunity(1);
                    Phase = PokerPhase.Turn;
                    break;
                case PokerPhase.Turn:
                    DealCommunity(1);
                    Phase = PokerPhase.River;
                    break;
                default:
                    Showdown();
                    return;
            }

            Reveal();

            // With fewer than two players able to bet, just keep dealing.
            if (CountSeats(CanAct) < 2)
            {
                ActiveSeat = -1;
                StepTimer = TickTimer.CreateFromSeconds(Runner, StreetPause * 1.5f);
                return;
            }

            ActiveSeat = ButtonSeat;
            AdvanceTurn();
        }

        void DealCommunity(int count)
        {
            for (var i = 0; i < count && CommunityCount < 5; i++)
            {
                Community.Set(CommunityCount, _deck.Draw());
                CommunityCount++;
            }
        }

        // ---------------------------------------------------------------- Authority: actions

        void Apply(int seat, PokerAction action, int raiseTo)
        {
            var toCall = ToCall(seat);
            switch (action)
            {
                case PokerAction.Fold:
                    SetFlag(seat, PokerSeatFlags.Folded, true);
                    break;

                case PokerAction.Check:
                    if (toCall > 0)
                        return;
                    break;

                case PokerAction.Call:
                    if (toCall == 0)
                    {
                        action = PokerAction.Check;
                        break;
                    }

                    Pay(seat, toCall, PokerAction.Call);
                    break;

                case PokerAction.Raise:
                {
                    var maxTo = RoundBets[seat] + Stacks[seat];
                    raiseTo = Mathf.Min(raiseTo, maxTo);
                    if (raiseTo >= maxTo)
                    {
                        action = PokerAction.AllIn;
                        goto case PokerAction.AllIn;
                    }

                    if (raiseTo < MinRaiseTo)
                        return;
                    var raiseSize = raiseTo - CurrentBet;
                    Pay(seat, raiseTo - RoundBets[seat], PokerAction.Raise);
                    MinRaise = Mathf.Max(MinRaise, raiseSize);
                    CurrentBet = raiseTo;
                    ReopenBetting(seat);
                    break;
                }

                case PokerAction.AllIn:
                {
                    var before = CurrentBet;
                    Pay(seat, Stacks[seat], PokerAction.AllIn);
                    if (RoundBets[seat] > before)
                    {
                        var raiseSize = RoundBets[seat] - before;
                        CurrentBet = RoundBets[seat];
                        if (raiseSize >= MinRaise)
                        {
                            MinRaise = raiseSize;
                            ReopenBetting(seat);
                        }
                    }

                    break;
                }

                default:
                    return;
            }

            SetFlag(seat, PokerSeatFlags.Acted, true);
            LastActions.Set(seat, (byte)action);
            if (action is PokerAction.Fold or PokerAction.Check)
                LastAmounts.Set(seat, 0);
            AdvanceTurn();
        }

        /// <summary>Move chips from a stack into the pot (capped by the stack; going to zero means all in).</summary>
        void Pay(int seat, int amount, PokerAction action)
        {
            amount = Mathf.Clamp(amount, 0, Stacks[seat]);
            Stacks.Set(seat, Stacks[seat] - amount);
            RoundBets.Set(seat, RoundBets[seat] + amount);
            Contributed.Set(seat, Contributed[seat] + amount);
            LastActions.Set(seat, (byte)action);
            LastAmounts.Set(seat, RoundBets[seat]);
            if (Stacks[seat] == 0)
                SetFlag(seat, PokerSeatFlags.AllIn, true);
        }

        void ReopenBetting(int raiser)
        {
            for (var i = 0; i < SeatCount; i++)
            {
                if (i != raiser)
                    SetFlag(i, PokerSeatFlags.Acted, false);
            }
        }

        void AwardUncontested()
        {
            var winner = NextSeat(-1, IsLive);
            var pot = TotalPot;
            if (winner >= 0)
            {
                Stacks.Set(winner, Stacks[winner] + pot);
                WonAmounts.Set(winner, pot);
                LastResult = $"{SeatName(winner)} takes {pot:N0}";
            }

            EnterShowdownDisplay();
        }

        /// <summary>Runs out the board and turns over every live hand; the pot is awarded after a pause.</summary>
        void Showdown()
        {
            // Run out the board if needed (everyone went all in before the river).
            DealCommunity(5 - CommunityCount);
            for (var seat = 0; seat < SeatCount; seat++)
            {
                if (!IsLive(seat))
                    continue;
                ShownCards.Set(seat * 2, _hole[seat * 2]);
                ShownCards.Set(seat * 2 + 1, _hole[seat * 2 + 1]);
            }

            Phase = PokerPhase.Showdown;
            ActiveSeat = -1;
            ShowdownSettled = false;
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, GameTables.ResultPause);
            Reveal();
        }

        /// <summary>
        /// Awards the pots from the networked shown cards and board, so a new authority can finish a showdown the
        /// old one started.
        /// </summary>
        void AwardShowdown()
        {
            Span<byte> seven = stackalloc byte[7];
            for (var i = 0; i < CommunityCount; i++)
                seven[i] = Community[i];

            var values = new PokerHandValue[SeatCount];
            var contributed = new int[SeatCount];
            var folded = new bool[SeatCount];
            for (var seat = 0; seat < SeatCount; seat++)
            {
                contributed[seat] = Contributed[seat];
                folded[seat] = !IsLive(seat) || ShownCards[seat * 2] == Cards.None;
                if (folded[seat])
                    continue;
                seven[5] = ShownCards[seat * 2];
                seven[6] = ShownCards[seat * 2 + 1];
                values[seat] = PokerRules.Evaluate(seven);
            }

            string summary = null;
            foreach (var pot in PokerRules.BuildPots(contributed, folded))
            {
                var winners = new List<int>();
                var best = new PokerHandValue(-1);
                foreach (var seat in pot.Eligible)
                {
                    if (values[seat] > best)
                    {
                        best = values[seat];
                        winners.Clear();
                        winners.Add(seat);
                    }
                    else if (values[seat].Score == best.Score)
                    {
                        winners.Add(seat);
                    }
                }

                if (winners.Count == 0)
                    continue;

                // Odd chips go to the first winner clockwise from the button.
                winners.Sort((a, b) => ClockwiseFromButton(a).CompareTo(ClockwiseFromButton(b)));
                var share = pot.Amount / winners.Count;
                var odd = pot.Amount - share * winners.Count;
                for (var w = 0; w < winners.Count; w++)
                {
                    var amount = share + (w < odd ? 1 : 0);
                    Stacks.Set(winners[w], Stacks[winners[w]] + amount);
                    WonAmounts.Set(winners[w], WonAmounts[winners[w]] + amount);
                }

                summary ??= winners.Count == 1
                    ? $"{SeatName(winners[0])} wins {pot.Amount:N0} with {best}"
                    : $"Split pot ({best})";
            }

            LastResult = summary ?? "";
            EnterShowdownDisplay();
        }

        void EnterShowdownDisplay()
        {
            Phase = PokerPhase.Showdown;
            ShowdownSettled = true;
            ActiveSeat = -1;
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, ShowdownDuration);
            Reveal();
            if (LastResult.Length > 0)
                RPC_Notice($"{_displayName}: {LastResult}");
        }

        void EndHand()
        {
            for (var i = 0; i < SeatCount; i++)
            {
                RoundBets.Set(i, 0);
                Contributed.Set(i, 0);
                Flags.Set(i, 0);
                // NPC bots rebuy when they bust so the table stays lively.
                if (OccupantAt(i) == PokerOccupant.Npc && Stacks[i] < BigBlind)
                    Stacks.Set(i, NpcStack);
            }

            CommunityCount = 0;
            Phase = PokerPhase.WaitingForPlayers;
            StepTimer = TickTimer.None;
            Reveal();
        }

        void VoidHand(string reason)
        {
            for (var i = 0; i < SeatCount; i++)
            {
                Stacks.Set(i, Stacks[i] + Contributed[i]);
                RoundBets.Set(i, 0);
                Contributed.Set(i, 0);
                Flags.Set(i, 0);
            }

            CommunityCount = 0;
            ActiveSeat = -1;
            Phase = PokerPhase.WaitingForPlayers;
            StepTimer = TickTimer.None;
            Reveal();
            RPC_Notice($"{_displayName}: {reason}");
        }

        // ---------------------------------------------------------------- NPC bots

        void DecideNpc(int seat)
        {
            var toCall = ToCall(seat);
            var strength = EstimateStrength(seat);
            var pot = TotalPot;
            var odds = toCall / (float)Mathf.Max(1, pot + toCall);
            var bluff = UnityEngine.Random.value < 0.07f;

            if ((strength > 0.78f || bluff) && Stacks[seat] > toCall)
            {
                var target = CurrentBet + Mathf.Max(MinRaise, pot / 2);
                Apply(seat, PokerAction.Raise, Mathf.Max(target, MinRaiseTo));
                return;
            }

            if (toCall == 0)
            {
                Apply(seat, PokerAction.Check, 0);
                return;
            }

            Apply(seat, strength > odds + 0.12f ? PokerAction.Call : PokerAction.Fold, 0);
        }

        float EstimateStrength(int seat)
        {
            var a = _hole[seat * 2];
            var b = _hole[seat * 2 + 1];
            if (CommunityCount < 3)
            {
                var high = Mathf.Max(PokerRules.Rank(a), PokerRules.Rank(b));
                var low = Mathf.Min(PokerRules.Rank(a), PokerRules.Rank(b));
                var score = (high + low) / 28f * 0.6f;
                if (high == low)
                    score = 0.55f + high / 30f;
                if (Cards.Suit(a) == Cards.Suit(b))
                    score += 0.05f;
                if (high - low == 1)
                    score += 0.03f;
                return Mathf.Clamp01(score);
            }

            Span<byte> cards = stackalloc byte[2 + CommunityCount];
            cards[0] = a;
            cards[1] = b;
            for (var i = 0; i < CommunityCount; i++)
                cards[2 + i] = Community[i];
            var value = PokerRules.Evaluate(cards);
            return value.Category switch
            {
                PokerCategory.HighCard => 0.12f + value.Kicker(0) / 100f,
                PokerCategory.Pair => 0.4f + value.Kicker(0) / 60f,
                PokerCategory.TwoPair => 0.68f,
                PokerCategory.Trips => 0.8f,
                _ => 0.92f,
            };
        }

        // ---------------------------------------------------------------- Seats

        /// <summary>NPC cats come and go between rounds, per the table setting (GDD §5.6, <see cref="NpcSeating"/>).</summary>
        void MaintainNpcs()
        {
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
                SeatOccupants.Set(seat, (byte)PokerOccupant.Empty);
                SeatArrivals.Set(seat, TickTimer.None);
                Stacks.Set(seat, 0);
                return;
            }

            SeatOccupants.Set(seat, (byte)PokerOccupant.Npc);
            SeatPlayers.Set(seat, PlayerRef.None);
            SeatNpcFur.Set(seat, (byte)UnityEngine.Random.Range(0, 6));
            SeatArrivals.Set(seat, instant ? TickTimer.None : TickTimer.CreateFromSeconds(Runner, NpcSeating.ArrivalSeconds(_seatAnchors[seat])));
            Stacks.Set(seat, NpcStack);
        }

        void IPlayerLeft.PlayerLeft(PlayerRef player)
        {
            if (!HasStateAuthority)
                return;
            for (var i = 0; i < SeatCount; i++)
            {
                if (OccupantAt(i) == PokerOccupant.Player && SeatPlayers[i] == player)
                    Vacate(i);
            }
        }

        void Vacate(int seat)
        {
            var wasActive = ActiveSeat == seat;
            if (IsLive(seat))
                SetFlag(seat, PokerSeatFlags.Folded, true);
            SeatOccupants.Set(seat, (byte)PokerOccupant.Empty);
            SeatPlayers.Set(seat, PlayerRef.None);
            // The leaving client cashes out the stack it last saw; chips already in the pot stay there.
            Stacks.Set(seat, 0);
            if (wasActive && IsBettingPhase)
                AdvanceTurn();
            else if (IsBettingPhase && CountSeats(IsLive) <= 1)
                AwardUncontested();
        }

        string SeatName(int seat) =>
            OccupantAt(seat) == PokerOccupant.Player ? PlayerCat.NameOf(SeatPlayers[seat]) : $"Seat {seat + 1} (NPC)";

        bool HasFlag(int seat, PokerSeatFlags flag) => (Flags[seat] & (byte)flag) != 0;

        void SetFlag(int seat, PokerSeatFlags flag, bool on)
        {
            var value = on ? Flags[seat] | (byte)flag : Flags[seat] & ~(byte)flag;
            Flags.Set(seat, (byte)value);
        }

        int CountSeats(Func<int, bool> predicate)
        {
            var count = 0;
            for (var i = 0; i < SeatCount; i++)
            {
                if (predicate(i))
                    count++;
            }

            return count;
        }

        /// <summary>The next seat clockwise after <paramref name="from"/> matching the predicate, or -1.</summary>
        int NextSeat(int from, Func<int, bool> predicate)
        {
            for (var step = 1; step <= SeatCount; step++)
            {
                var seat = ((from < 0 ? -1 : from) + step + SeatCount) % SeatCount;
                if (predicate(seat))
                    return seat;
            }

            return -1;
        }

        int ClockwiseFromButton(int seat) => (seat - ButtonSeat - 1 + SeatCount * 2) % SeatCount;

        // ---------------------------------------------------------------- RPCs

        public void Sit(int seat, int buyIn) => RPC_Sit((byte)seat, buyIn);
        public void Rebuy(int seat, int amount) => RPC_Rebuy((byte)seat, amount);
        public void Act(int seat, PokerAction action, int raiseTo = 0) => RPC_Act((byte)seat, (byte)action, raiseTo);
        public void ChangeSettings(int blindOption, int turnTimerOption, int npcFill) =>
            RPC_SetSettings((byte)blindOption, (byte)turnTimerOption, (byte)npcFill);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_Sit(byte seat, int buyIn, RpcInfo info = default)
        {
            if (seat >= SeatCount || TryGetSeatOf(info.Source, out _) || OccupantAt(seat) == PokerOccupant.Player)
                return;
            if (OccupantAt(seat) == PokerOccupant.Npc && (FlagsAt(seat) & PokerSeatFlags.InHand) != 0 && IsBettingPhase)
                return;

            buyIn = Mathf.Clamp(buyIn, MinBuyIn, MaxBuyIn);
            SeatOccupants.Set(seat, (byte)PokerOccupant.Player);
            SeatPlayers.Set(seat, info.Source);
            SeatArrivals.Set(seat, TickTimer.None);
            Flags.Set(seat, 0);
            Stacks.Set(seat, buyIn);
            BuyIns.Set(seat, buyIn);
            Sessions.Set(seat, Sessions[seat] + 1);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_Rebuy(byte seat, int amount, RpcInfo info = default)
        {
            if (seat >= SeatCount || SeatPlayers[seat] != info.Source || OccupantAt(seat) != PokerOccupant.Player)
                return;
            if (Stacks[seat] >= BigBlind || (IsLive(seat) && IsBettingPhase))
                return;
            amount = Mathf.Clamp(amount, MinBuyIn, MaxBuyIn);
            Stacks.Set(seat, Stacks[seat] + amount);
            BuyIns.Set(seat, amount);
            Sessions.Set(seat, Sessions[seat] + 1);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_Leave(byte seat, RpcInfo info = default)
        {
            if (seat < SeatCount && OccupantAt(seat) == PokerOccupant.Player && SeatPlayers[seat] == info.Source)
                Vacate(seat);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_Act(byte seat, byte action, int raiseTo, RpcInfo info = default)
        {
            if (!IsBettingPhase || ActiveSeat != seat || SeatPlayers[seat] != info.Source || OccupantAt(seat) != PokerOccupant.Player)
                return;
            if (action is < (byte)PokerAction.Fold or > (byte)PokerAction.AllIn)
                return;
            Apply(seat, (PokerAction)action, raiseTo);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_SetSettings(byte blindOption, byte turnTimerOption, byte npcFill, RpcInfo info = default)
        {
            if (!TryGetSeatOf(info.Source, out _))
                return;
            BlindOption = (byte)Mathf.Clamp(blindOption, 0, SmallBlindOptions.Length - 1);
            TurnTimerOption = (byte)Mathf.Clamp(turnTimerOption, 0, TurnTimerOptions.Length - 1);
            NpcFill = NpcSeating.Clamp(npcFill);
            RPC_Notice($"{PlayerCat.NameOf(info.Source)} set {_displayName}: blinds {SmallBlind}/{BigBlind}, turn timer {TurnTimerSeconds}s, NPC players {NpcSeating.Describe(NpcFill)}.");
        }

        /// <summary>Private delivery of hole cards: only the target player receives this.</summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_DealHoleCards([RpcTarget] PlayerRef target, int handId, byte first, byte second)
        {
            _myHoleHand = handId;
            _myHole[0] = first;
            _myHole[1] = second;
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_Notice(string text) => ChatLog.AddSystem(text);

        // ---------------------------------------------------------------- Local wallet

        public override void Render()
        {
            if (Runner == null)
                return;

            if (TryGetSeatOf(Runner.LocalPlayer, out var seat))
            {
                if (!_walletSeated)
                {
                    _walletSeated = true;
                    _chargedSession = 0;
                }

                if (_chargedSession != Sessions[seat])
                {
                    _chargedSession = Sessions[seat];
                    ProfileStore.SetBalance(ProfileStore.Current.Balance - BuyIns[seat]);
                }

                // The live stack, so leaving never cashes out less (or more) than the table holds.
                _lastSeenStack = Stacks[seat];

                // The result follows the shown hand, so the toast comes with the pot, not before it.
                var shown = Shown;
                if (shown.Phase == PokerPhase.Showdown && shown.ShowdownSettled && _reportedHand != shown.HandId && (shown.FlagsAt(seat) & PokerSeatFlags.InHand) != 0)
                {
                    _reportedHand = shown.HandId;
                    LocalHandResult?.Invoke(this, shown.WonAt(seat) - shown.ContributedAt(seat));
                }
            }
            else if (_walletSeated)
            {
                _walletSeated = false;
                if (_lastSeenStack > 0)
                {
                    ProfileStore.SetBalance(ProfileStore.Current.Balance + _lastSeenStack);
                    Toasts.Show($"Cashed out {_lastSeenStack:N0} chips from {_displayName}.");
                }

                _lastSeenStack = 0;
            }
        }
    }
}
