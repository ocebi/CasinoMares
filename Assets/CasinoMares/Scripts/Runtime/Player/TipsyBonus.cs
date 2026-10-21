using CasinoMares.Baccarat;
using CasinoMares.Blackjack;
using CasinoMares.Core;
using CasinoMares.Poker;
using CasinoMares.Roulette;
using CasinoMares.Slots;
using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>The games that report a local result, for <see cref="TipsyBonus"/>.</summary>
    public enum TipsyGame
    {
        Blackjack,
        Poker,
        Baccarat,
        Roulette,
        Slots,
    }

    /// <summary>
    /// The upside of being tipsy (GDD §7.2): while drunk, a net win at a skill game pays extra into the local balance,
    /// shown with the "+N" gain label and a toast. Blackjack, the hard one, pays the most; the games of pure chance pay
    /// nothing. Trusted client, like welfare. Subscribes to the tables' local result events once at startup; they fire
    /// after the table has credited the payout.
    /// </summary>
    public static class TipsyBonus
    {
        const int MinBonus = 1;

        /// <summary>The share of a net win paid on top, per game. The one place the rates live.</summary>
        public static float RateFor(TipsyGame game) => game switch
        {
            TipsyGame.Blackjack => 0.20f,
            TipsyGame.Poker => 0.10f,
            _ => 0f,
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Subscribe()
        {
            BlackjackTable.LocalRoundResult += (_, net) => OnResult(TipsyGame.Blackjack, net);
            PokerTable.LocalHandResult += (_, net) => OnResult(TipsyGame.Poker, net);
            BaccaratTable.LocalRoundResult += (_, net) => OnResult(TipsyGame.Baccarat, net);
            RouletteTable.LocalRoundResult += (_, net) => OnResult(TipsyGame.Roulette, net);
            SlotMachine.LocalSpinResult += (_, net) => OnResult(TipsyGame.Slots, net);
        }

        /// <summary>
        /// The bonus a net result earns while drunk: the game's rate rounded down, at least one chip where the game has
        /// a rate, nothing on a push or loss.
        /// </summary>
        public static int BonusFor(TipsyGame game, int net)
        {
            var rate = RateFor(game);
            return net > 0 && rate > 0f ? Mathf.Max(MinBonus, Mathf.FloorToInt(net * rate)) : 0;
        }

        static void OnResult(TipsyGame game, int net)
        {
            var bonus = BonusFor(game, net);
            if (bonus <= 0 || !CatDrinking.LocalIsDrunk)
                return;
            ChipRewards.Grant(bonus);
            Toasts.Show($"Tipsy bonus! +{bonus:N0} chips on top.");
        }
    }
}
