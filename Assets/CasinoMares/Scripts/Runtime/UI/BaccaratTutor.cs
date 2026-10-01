using System.Collections.Generic;
using System.Text;
using CasinoMares.Baccarat;
using CasinoMares.Rules;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// The baccarat coach (teaching mode): the odds while betting, then a step-by-step account of the deal (card
    /// values, naturals, why each side draws or stands) and what the result means for your bets.
    /// </summary>
    public class BaccaratTutor
    {
        readonly StringBuilder _text = new();
        readonly List<byte> _player = new();
        readonly List<byte> _banker = new();

        public BaccaratTutor(VisualElement root)
        {
            Panel = new CoachPanel(root, "Coach.Baccarat", "Baccarat", GameGuides.Baccarat);
        }

        public CoachPanel Panel { get; }

        /// <summary>The bet spot the coach suggests right now, or -1.</summary>
        public int SuggestedSpot { get; private set; } = -1;

        public void Update(BaccaratTable table, int seat)
        {
            SuggestedSpot = -1;
            if (!Panel.Enabled)
                return;

            switch (table.Phase)
            {
                case BaccaratPhase.Betting:
                    Betting(table, seat);
                    break;
                case BaccaratPhase.Dealing:
                    Panel.Set("Coach · The deal", DealText(table), "No decisions now: the drawing rules are fixed. Just watch.");
                    break;
                case BaccaratPhase.Result:
                    Panel.Set("Coach · Result", DealText(table) + "\n\n" + ResultText(table, seat),
                        "Every coup starts fresh. Patterns in the road don't predict the next one.");
                    break;
                default:
                    Panel.Set("Coach · Baccarat", "Betting opens as soon as a player sits down.", "Open <b>Rules</b> any time for the drawing rules and payouts.");
                    break;
            }
        }

        void Betting(BaccaratTable table, int seat)
        {
            _text.Clear();
            _text.Append("Banker wins <b>45.9%</b> of coups, Player <b>44.6%</b>, Tie <b>9.5%</b>.\n");
            _text.Append("Player pays 1 to 1, Banker 0.95 to 1 (5% commission), Tie 8 to 1.");

            var streak = Streak(table, out var side);
            if (streak >= 3)
                _text.Append($"\n\nThe road shows {streak} {side} wins in a row. Fun to watch, but it doesn't change the odds of the next coup.");

            if (table.BetAt(seat, BaccaratRules.SpotTie) > 0)
                _text.Append("\n\nYour Tie bet pays 8 to 1 but only hits 9.5% of the time, so on average it loses about 14% of what you stake.");

            string tip;
            if (table.BetAt(seat, BaccaratRules.SpotBanker) == 0)
            {
                SuggestedSpot = BaccaratRules.SpotBanker;
                tip = "Suggest: bet on <b>Banker</b>. It has the lowest house edge in the casino (1.06%), even after the commission.";
            }
            else if (!table.IsDoneAt(seat))
            {
                tip = $"Happy with your bet? Press <b>Ready</b>{KeyHints.For("Space")} so the cards can come out sooner.";
            }
            else
            {
                tip = "Waiting for the other players to finish betting.";
            }

            Panel.Set("Coach · Place your bets", _text.ToString(), tip);
        }

        /// <summary>How many of the latest coups the same side won in a row (ties don't break or extend it).</summary>
        static int Streak(BaccaratTable table, out string side)
        {
            side = null;
            var count = 0;
            var current = BaccaratOutcome.None;
            for (var i = 0; i < BaccaratTable.HistoryLength; i++)
            {
                var outcome = table.HistoryAt(i);
                if (outcome == BaccaratOutcome.None)
                    break;
                if (outcome == BaccaratOutcome.Tie)
                    continue;
                if (current == BaccaratOutcome.None)
                    current = outcome;
                if (outcome != current)
                    break;
                count++;
            }

            side = current.ToString();
            return count;
        }

        string DealText(BaccaratTable table)
        {
            _player.Clear();
            _banker.Clear();
            for (var order = 0; order < 6; order++)
            {
                if (!table.TryGetRevealedCard(order, out var isBanker, out _, out var card))
                    break;
                (isBanker ? _banker : _player).Add(card);
            }

            if (_player.Count < 2 || _banker.Count < 2)
                return "Two cards each, dealt Player, Banker, Player, Banker.\nCard values: A = 1, 2 to 9 as shown, 10 J Q K = 0. Only the last digit of the total counts.";

            _text.Clear();
            var player2 = Total(_player, 2);
            var banker2 = Total(_banker, 2);
            _text.Append($"Player: {Sum(_player)}\nBanker: {Sum(_banker)}");

            if (BaccaratRules.IsNatural(player2) || BaccaratRules.IsNatural(banker2))
            {
                var who = BaccaratRules.IsNatural(player2) && BaccaratRules.IsNatural(banker2) ? "Both sides have"
                    : BaccaratRules.IsNatural(player2) ? "Player has" : "Banker has";
                _text.Append($"\n\n{who} a <b>natural</b> (8 or 9), so nobody draws. The coup is over.");
                return _text.ToString();
            }

            var playerDraws = BaccaratRules.PlayerDraws(player2);
            _text.Append(playerDraws
                ? $"\n\nPlayer has {player2}: on 0 to 5 the Player <b>draws</b> a third card."
                : $"\n\nPlayer has {player2}: on 6 or 7 the Player <b>stands</b>.");

            var thirdValue = -1;
            if (playerDraws)
            {
                if (_player.Count < 3)
                    return _text.ToString();
                thirdValue = BaccaratRules.CardValue(_player[2]);
                _text.Append($"\nPlayer's third card {Label(_player[2])} is worth {thirdValue}: Player now has <b>{Total(_player, 3)}</b>.");
            }

            var bankerDraws = BaccaratRules.BankerDraws(banker2, thirdValue);
            _text.Append($"\n\n{BankerReason(banker2, thirdValue)} So the Banker <b>{(bankerDraws ? "draws" : "stands")}</b>.");
            if (bankerDraws && _banker.Count >= 3)
                _text.Append($"\nBanker's third card {Label(_banker[2])}: Banker now has <b>{Total(_banker, 3)}</b>.");
            return _text.ToString();
        }

        static string BankerReason(int banker, int playerThird)
        {
            if (playerThird < 0)
                return $"The Player stood, so the Banker follows the same rule: draw on 0 to 5. Banker has {banker}.";
            return banker switch
            {
                <= 2 => $"Banker has {banker}: with 0 to 2 it always draws.",
                3 => $"Banker has 3: it draws unless the Player's third card was an 8 (it was {playerThird}).",
                4 => $"Banker has 4: it draws when the Player's third card is 2 to 7 (it was {playerThird}).",
                5 => $"Banker has 5: it draws when the Player's third card is 4 to 7 (it was {playerThird}).",
                6 => $"Banker has 6: it draws only when the Player's third card is 6 or 7 (it was {playerThird}).",
                _ => "Banker has 7: it always stands.",
            };
        }

        string ResultText(BaccaratTable table, int seat)
        {
            var player = table.VisibleTotal(false);
            var banker = table.VisibleTotal(true);
            var outcome = table.Outcome;
            var line = outcome == BaccaratOutcome.Tie
                ? $"Player {player}, Banker {banker}: a <b>tie</b>. Player and Banker bets come back."
                : $"Player {player}, Banker {banker}: <b>{outcome} wins</b>.";

            var bets = new StringBuilder();
            for (var spot = 0; spot < BaccaratRules.Spots; spot++)
            {
                var amount = table.BetAt(seat, spot);
                if (amount <= 0)
                    continue;
                var net = BaccaratRules.Payout(spot, amount, outcome) - amount;
                var result = net > 0 ? $"won {net:N0}" : net < 0 ? "lost" : "returned";
                if (net > 0 && spot == BaccaratRules.SpotBanker)
                    result += " (95% of the stake, after the 5% commission)";
                bets.Append($"\nYour {amount:N0} on {BaccaratRules.SpotNames[spot]}: {result}.");
            }

            return bets.Length == 0 ? line + "\nYou sat this one out." : line + bets;
        }

        static int Total(List<byte> cards, int count)
        {
            var total = 0;
            for (var i = 0; i < count && i < cards.Count; i++)
                total += BaccaratRules.CardValue(cards[i]);
            return total % 10;
        }

        /// <summary>"7♦ + 8♣ = 15 → 5", or just "K♠ + 4♥ = 4" when nothing needs dropping.</summary>
        static string Sum(List<byte> cards)
        {
            var raw = BaccaratRules.CardValue(cards[0]) + BaccaratRules.CardValue(cards[1]);
            var cardsText = $"{Label(cards[0])} + {Label(cards[1])}";
            return raw >= 10 ? $"{cardsText} = {raw} → <b>{raw % 10}</b>" : $"{cardsText} = <b>{raw}</b>";
        }

        static string Label(byte card) => Cards.IsRed(card) ? $"<color=#E0605A>{Cards.Label(card)}</color>" : Cards.Label(card);
    }
}
