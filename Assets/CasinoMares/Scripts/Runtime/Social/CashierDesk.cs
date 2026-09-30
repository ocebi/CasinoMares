using CasinoMares.Baccarat;
using CasinoMares.Blackjack;
using CasinoMares.Core;
using CasinoMares.Player;
using CasinoMares.Poker;
using CasinoMares.Roulette;
using CasinoMares.Slots;
using CasinoMares.Rules;

namespace CasinoMares.Social
{
    /// <summary>"Cat Welfare" (GDD §7.2): a broke cat with no chips in play can get a 500-chip refill.</summary>
    public class CashierDesk : Interactable
    {
        public const int WelfareAmount = 500;

        public override string GetPrompt(PlayerCat player)
        {
            if (ProfileStore.Current.Balance >= BlackjackRules.MinBet)
                return "Cashier: come back when you're broke";
            return AnyChipsInPlay()
                ? "Cashier: finish your game first"
                : $"Cat Welfare: get {WelfareAmount} chips";
        }

        public override bool CanInteract(PlayerCat player) =>
            ProfileStore.Current.Balance < BlackjackRules.MinBet && !AnyChipsInPlay();

        static bool AnyChipsInPlay() =>
            BlackjackTable.AnyLocalActiveWager() || RouletteTable.AnyLocalActiveWager() ||
            BaccaratTable.AnyLocalActiveWager() || SlotMachine.AnyLocalSpinInProgress() || PokerTable.AnyLocalChipsAtTable();

        public override void Interact(PlayerCat player)
        {
            if (!CanInteract(player))
                return;

            ProfileStore.SetBalance(ProfileStore.Current.Balance + WelfareAmount);
            Toasts.Show($"The cashier slides you {WelfareAmount} chips. Good luck!");
            player.Announce($"{player.DisplayName} got a Cat Welfare refill (+{WelfareAmount}).");
        }
    }
}
