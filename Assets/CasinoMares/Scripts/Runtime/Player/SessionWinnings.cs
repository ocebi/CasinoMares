using CasinoMares.Baccarat;
using CasinoMares.Blackjack;
using CasinoMares.Core;
using CasinoMares.Poker;
using CasinoMares.Roulette;
using CasinoMares.Slots;
using Fusion;

namespace CasinoMares.Player
{
    /// <summary>
    /// How far up the player is at the table or machine they're sitting at. The owner adds up its own results from
    /// every game while seated and starts over when it gets up. Networked so the master client's patrons can
    /// gather around a winning streak and cheer each win (GDD §4.4).
    /// </summary>
    public class SessionWinnings : NetworkBehaviour
    {
        /// <summary>Net chips won since sitting down (can be negative).</summary>
        [Networked] public int Amount { get; set; }

        /// <summary>Net result of the latest round, and a counter that ticks once per round so watchers can react.</summary>
        [Networked] public int LastResult { get; set; }
        [Networked] public int ResultCount { get; set; }

        public override void Spawned()
        {
            if (!HasStateAuthority)
                return;
            SeatingController.LocalSeatChanged += OnSeatChanged;
            BlackjackTable.LocalRoundResult += OnBlackjack;
            RouletteTable.LocalRoundResult += OnRoulette;
            BaccaratTable.LocalRoundResult += OnBaccarat;
            PokerTable.LocalHandResult += OnPoker;
            SlotMachine.LocalSpinResult += OnSlots;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            SeatingController.LocalSeatChanged -= OnSeatChanged;
            BlackjackTable.LocalRoundResult -= OnBlackjack;
            RouletteTable.LocalRoundResult -= OnRoulette;
            BaccaratTable.LocalRoundResult -= OnBaccarat;
            PokerTable.LocalHandResult -= OnPoker;
            SlotMachine.LocalSpinResult -= OnSlots;
        }

        void OnSeatChanged(IGameTable table, int seat) => Amount = 0;

        void OnBlackjack(BlackjackTable table, int net) => Add(net);
        void OnRoulette(RouletteTable table, int net) => Add(net);
        void OnBaccarat(BaccaratTable table, int net) => Add(net);
        void OnPoker(PokerTable table, int net) => Add(net);
        void OnSlots(SlotMachine machine, int net) => Add(net);

        void Add(int net)
        {
            // Hands still settling after the player got up don't count toward a new streak.
            if (Object == null || !Object.IsValid || SeatingController.LocalTable == null)
                return;
            Amount += net;
            LastResult = net;
            ResultCount++;
        }
    }
}
