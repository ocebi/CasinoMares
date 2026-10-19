using System;
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
    /// <summary>
    /// "Cat Welfare" (GDD §7.2): a broke cat with no chips in play can get a 500-chip refill, at most once an hour
    /// (the cooldown is kept in the profile by <see cref="ChipRewards"/>).
    /// </summary>
    public class CashierDesk : Interactable
    {
        public const int WelfareAmount = 500;
        const string WelfareKey = "welfare";
        static readonly TimeSpan WelfareCooldown = TimeSpan.FromHours(1);

        public override string GetPrompt(PlayerCat player)
        {
            if (ProfileStore.Current.Balance >= BlackjackRules.MinBet)
                return "Cashier: come back when you're broke";
            if (AnyChipsInPlay())
                return "Cashier: finish your game first";
            var wait = ChipRewards.SecondsLeft(WelfareKey, WelfareCooldown);
            return wait > 0
                ? $"Cat Welfare again in {ChipRewards.FormatWait(wait)} (paw things for chips)"
                : $"Cat Welfare: get {WelfareAmount} chips";
        }

        public override bool CanInteract(PlayerCat player) =>
            ProfileStore.Current.Balance < BlackjackRules.MinBet && !AnyChipsInPlay() &&
            ChipRewards.SecondsLeft(WelfareKey, WelfareCooldown) == 0;

        static bool AnyChipsInPlay() =>
            BlackjackTable.AnyLocalActiveWager() || RouletteTable.AnyLocalActiveWager() ||
            BaccaratTable.AnyLocalActiveWager() || SlotMachine.AnyLocalSpinInProgress() || PokerTable.AnyLocalChipsAtTable();

        public override void Interact(PlayerCat player)
        {
            if (!CanInteract(player))
                return;

            if (!ChipRewards.TryEarn(WelfareKey, WelfareAmount, WelfareCooldown, big: true, countsForSession: false))
                return;
            Toasts.Show($"The cashier slides you {WelfareAmount} chips. Good luck!");
            player.Announce($"{player.DisplayName} got a Cat Welfare refill (+{WelfareAmount}).");
        }
    }
}
