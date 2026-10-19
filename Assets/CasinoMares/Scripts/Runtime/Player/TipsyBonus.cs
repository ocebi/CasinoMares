using CasinoMares.Baccarat;
using CasinoMares.Blackjack;
using CasinoMares.Core;
using CasinoMares.Poker;
using CasinoMares.Roulette;
using CasinoMares.Slots;
using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// The upside of being tipsy (GDD §7.2): while drunk, a net win at any table or machine pays 10% extra into the
    /// local balance, shown with the "+N" gain label and a toast. Trusted client, like welfare. Subscribes to the
    /// tables' local result events once at startup; they fire after the table has credited the payout.
    /// </summary>
    public static class TipsyBonus
    {
        public const float BonusRate = 0.10f;
        const int MinBonus = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Subscribe()
        {
            BlackjackTable.LocalRoundResult += (_, net) => OnResult(net);
            RouletteTable.LocalRoundResult += (_, net) => OnResult(net);
            BaccaratTable.LocalRoundResult += (_, net) => OnResult(net);
            PokerTable.LocalHandResult += (_, net) => OnResult(net);
            SlotMachine.LocalSpinResult += (_, net) => OnResult(net);
        }

        /// <summary>The bonus a net result earns while drunk: 10% rounded down, at least one chip, nothing on a push or loss.</summary>
        public static int BonusFor(int net) => net > 0 ? Mathf.Max(MinBonus, Mathf.FloorToInt(net * BonusRate)) : 0;

        static void OnResult(int net)
        {
            var bonus = BonusFor(net);
            if (bonus <= 0 || !PlayerCat.LocalIsDrunk)
                return;
            ChipRewards.Grant(bonus);
            Toasts.Show($"Tipsy bonus! +{bonus:N0} chips on top.");
        }
    }
}
