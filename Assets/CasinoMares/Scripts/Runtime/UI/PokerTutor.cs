using System;
using System.Text;
using System.Threading.Tasks;
using CasinoMares.Poker;
using CasinoMares.Rules;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace CasinoMares.UI
{
    /// <summary>
    /// The poker coach (teaching mode): what's happening at the table, how strong your cards are, your chance to
    /// win and what a sensible beginner would do about it. Only uses what the local player can see: their own hole
    /// cards and the shared ones. The win chance is simulated off the main thread.
    /// </summary>
    public class PokerTutor
    {
        const int Iterations = 2500;

        static readonly string[] CategoryNames =
        {
            "high card", "one pair", "two pair", "three of a kind", "a straight", "a flush", "a full house",
            "four of a kind", "a straight flush",
        };

        readonly StringBuilder _text = new();
        (int hand, int board, int opponents) _equityKey = (-1, -1, -1);
        Task<float> _equityTask;
        int _equityHand = -1;
        float _equity = -1f;

        public PokerTutor(VisualElement root)
        {
            Panel = new CoachPanel(root, "Coach.Poker", "Texas Hold'em", GameGuides.Poker);
        }

        public CoachPanel Panel { get; }

        /// <summary>What the coach suggests right now, if it's the local player's turn and the odds are in.</summary>
        public PokerAdvice? Suggestion { get; private set; }

        public int SuggestedRaiseTo { get; private set; }

        public void Update(PokerTable table, int seat)
        {
            Suggestion = null;
            if (!Panel.Enabled)
                return;

            var phase = table.Phase;
            if (phase == PokerPhase.WaitingForPlayers)
            {
                Panel.Set("Coach · Between hands",
                    "A new hand starts once two players have chips. The two players left of the dealer button post the " +
                    "blinds: small forced bets, so every hand has something to win.",
                    "Open <b>Rules</b> any time for the hand rankings.");
                return;
            }

            var haveCards = table.IsInHand(seat) & table.TryGetMyHoleCards(out var a, out var b);
            if (phase == PokerPhase.Showdown)
            {
                var result = table.ShowdownSettled ? table.LastResult.ToString() : "The cards are turned over. Who has the best five?";
                var mine = haveCards && table.CommunityCount >= 3 ? $"\nYour hand: <b>{BestHand(table, a, b)}</b>." : "";
                Panel.Set("Coach · Showdown", result + mine,
                    "The best five cards from each player's two plus the five shared cards win. Ties split the pot.");
                return;
            }

            if (!haveCards)
            {
                Panel.Set($"Coach · {StreetTitle(phase)}",
                    "You're not in this hand. Watch how the others bet: a big raise usually means a strong hand, a check often means a weak one.",
                    StreetExplanation(phase));
                return;
            }

            var opponents = 0;
            for (var i = 0; i < PokerTable.SeatCount; i++)
            {
                if (i != seat && table.IsInHand(i))
                    opponents++;
            }

            UpdateEquity(table, a, b, Math.Max(1, opponents));

            var myTurn = table.IsActiveTurn(seat);
            var toCall = table.ToCall(seat);
            var pot = table.TotalPot;

            _text.Clear();
            _text.Append(HandDescription(table, a, b));
            if (myTurn && toCall > 0)
            {
                var need = Percent(PokerCoach.PotOdds(toCall, pot));
                _text.Append($"\n\nCalling costs <b>{toCall:N0}</b> and would make the pot {pot + toCall:N0}, so calling pays off if you win at least <b>{need}%</b> of the time.");
            }

            var meterText = _equity >= 0f
                ? $"Chance to win: {Percent(_equity)}% against {opponents} {(opponents == 1 ? "player" : "players")}"
                : "Working out your chances...";

            string tip;
            if (!myTurn)
            {
                tip = StreetExplanation(phase);
            }
            else if (_equity < 0f)
            {
                tip = "Thinking...";
            }
            else
            {
                var advice = PokerCoach.Suggest(_equity, opponents, phase == PokerPhase.PreFlop, toCall, pot, table.StackAt(seat),
                    table.RoundBetAt(seat), table.CurrentBet, table.MinRaiseTo, table.BigBlind, out var raiseTo);
                Suggestion = advice;
                SuggestedRaiseTo = raiseTo;
                tip = AdviceText(advice, raiseTo, toCall, table.CurrentBet, Percent(_equity), Percent(PokerCoach.PotOdds(toCall, pot)));
            }

            Panel.Set($"Coach · {StreetTitle(phase)}", _text.ToString(), tip, _equity >= 0f ? _equity : 0f, meterText);
        }

        void UpdateEquity(PokerTable table, byte a, byte b, int opponents)
        {
            if (_equityHand != table.HandId)
            {
                _equityHand = table.HandId;
                _equity = -1f;
            }

            if (_equityTask != null && _equityTask.IsCompleted)
            {
                if (_equityTask.Status == TaskStatus.RanToCompletion)
                    _equity = _equityTask.Result;
                _equityTask = null;
            }

            var key = (table.HandId, (int)table.CommunityCount, opponents);
            if (key == _equityKey || _equityTask != null)
                return;

            _equityKey = key;
            var board = new byte[table.CommunityCount];
            for (var i = 0; i < board.Length; i++)
                board[i] = table.CommunityAt(i);
            var seed = Random.Range(1, int.MaxValue);
            _equityTask = Task.Run(() => PokerCoach.Equity(a, b, board, opponents, Iterations, seed));
        }

        string HandDescription(PokerTable table, byte a, byte b)
        {
            var cards = $"{CardText(a)} {CardText(b)}";
            if (table.CommunityCount < 3)
            {
                var score = PokerCoach.ChenScore(a, b);
                var tier = PokerCoach.Tier(score);
                var advice = tier switch
                {
                    StartingHandTier.Premium => "one of the best starts there is. Raise.",
                    StartingHandTier.Strong => "good enough to raise, or to call a raise.",
                    StartingHandTier.Playable => "worth playing if it's cheap; let it go against big raises.",
                    StartingHandTier.Marginal => "only worth playing if it's cheap or free.",
                    _ => "usually a fold, unless you can check for free.",
                };
                return $"Your cards: {cards}. A <b>{tier.ToString().ToLowerInvariant()}</b> starting hand: {advice}";
            }

            var value = Evaluate(table, a, b);
            var text = $"Your cards: {cards}. Best hand: <b>{value}</b>. {Context(value.Category)}";
            if (OnTheBoard(table, value))
                text += " But it's all on the shared cards, so every player has it too.";
            var draw = table.CommunityCount < 5 ? DrawText(table, a, b, value.Category) : null;
            return draw == null ? text : text + "\n" + draw;
        }

        /// <summary>
        /// True when the made hand (a pair, two pair...) comes entirely from the shared cards, e.g. a pair of kings on
        /// the board while holding 5 2: a classic beginner trap.
        /// </summary>
        static bool OnTheBoard(PokerTable table, PokerHandValue value)
        {
            if (value.Category is < PokerCategory.Pair or > PokerCategory.Quads or PokerCategory.Straight or PokerCategory.Flush)
                return false;

            Span<int> counts = stackalloc int[15];
            for (var i = 0; i < table.CommunityCount; i++)
                counts[PokerRules.Rank(table.CommunityAt(i))]++;

            // The main group of the hand (the pair, the trips...) and, for two pair and full houses, the second one.
            var need = value.Category switch
            {
                PokerCategory.Quads => 4,
                PokerCategory.Trips or PokerCategory.FullHouse => 3,
                _ => 2,
            };
            if (counts[value.Kicker(0)] < need)
                return false;
            return value.Category is not (PokerCategory.TwoPair or PokerCategory.FullHouse) || counts[value.Kicker(1)] >= 2;
        }

        static string Context(PokerCategory category) => category switch
        {
            PokerCategory.HighCard => "No pair yet, so only a lower high card loses to it.",
            PokerCategory.StraightFlush => "Almost nothing beats it.",
            PokerCategory.Quads => "Only a straight flush beats it.",
            _ => $"It beats {CategoryNames[(int)category - 1]}; it loses to {CategoryNames[(int)category + 1]} or better.",
        };

        /// <summary>Flush and straight draws, with the "rule of 4 and 2" to turn outs into a rough chance.</summary>
        static string DrawText(PokerTable table, byte a, byte b, PokerCategory made)
        {
            var community = table.CommunityCount;
            Span<int> suits = stackalloc int[4];
            Span<bool> ranks = stackalloc bool[15];
            Span<bool> holeRanks = stackalloc bool[15];
            Span<byte> holes = stackalloc byte[] { a, b };
            foreach (var hole in holes)
            {
                suits[Cards.Suit(hole)]++;
                MarkRank(hole, ranks);
                MarkRank(hole, holeRanks);
            }

            for (var i = 0; i < community; i++)
            {
                var card = table.CommunityAt(i);
                suits[Cards.Suit(card)]++;
                MarkRank(card, ranks);
            }

            var outs = 0;
            string name = null;
            if (made < PokerCategory.Flush)
            {
                for (var suit = 0; suit < 4; suit++)
                {
                    if (suits[suit] == 4 && (Cards.Suit(a) == suit || Cards.Suit(b) == suit))
                    {
                        outs = 9;
                        name = "a flush draw (four of one suit)";
                    }
                }
            }

            if (made < PokerCategory.Straight)
            {
                // Ranks that would complete a straight using at least one of our cards.
                Span<bool> completes = stackalloc bool[15];
                var completing = 0;
                for (var low = 1; low <= 10; low++)
                {
                    var present = 0;
                    var missing = -1;
                    var usesHole = false;
                    for (var r = low; r < low + 5; r++)
                    {
                        if (ranks[r])
                        {
                            present++;
                            usesHole |= holeRanks[r];
                        }
                        else
                        {
                            missing = r;
                        }
                    }

                    if (present == 4 && usesHole && !completes[missing == 1 ? 14 : missing])
                    {
                        completes[missing == 1 ? 14 : missing] = true;
                        completing++;
                    }
                }

                if (completing > 0)
                {
                    var straightOuts = Math.Min(2, completing) * 4;
                    var straightName = completing >= 2 ? "an open-ended straight draw" : "a gutshot straight draw (one card fills the gap)";
                    if (name == null)
                    {
                        outs = straightOuts;
                        name = straightName;
                    }
                    else
                    {
                        outs += straightOuts - 1;
                        name += $" and {straightName}";
                    }
                }
            }

            if (name == null)
                return null;
            var toCome = community == 3 ? 2 : 1;
            var chance = Math.Min(outs * (toCome == 2 ? 4 : 2), 70);
            return $"You have {name}: <b>{outs} cards</b> (\"outs\") improve you. With {toCome} card{(toCome == 2 ? "s" : "")} to come that's roughly <b>{chance}%</b> (outs × {(toCome == 2 ? 4 : 2)}).";
        }

        /// <summary>Marks a card's rank; an ace also counts as 1, for A-2-3-4-5.</summary>
        static void MarkRank(byte card, Span<bool> ranks)
        {
            var rank = PokerRules.Rank(card);
            ranks[rank] = true;
            if (rank == 14)
                ranks[1] = true;
        }

        static string AdviceText(PokerAdvice advice, int raiseTo, int toCall, int currentBet, int winPercent, int needPercent) => advice switch
        {
            PokerAdvice.Raise => $"Suggest: <b>{(currentBet == 0 ? "Bet" : "Raise to")} {raiseTo:N0}</b>. You're likely ahead, so build the pot while weaker hands still call.",
            PokerAdvice.AllIn => "Suggest: <b>All in</b>. You're a big favourite and a proper bet would use most of your chips anyway.",
            PokerAdvice.Check => "Suggest: <b>Check</b>. Seeing the next card costs nothing; no need to put chips in with this.",
            PokerAdvice.Call => $"Suggest: <b>Call {toCall:N0}</b>. You win about {winPercent}% and the call only needs {needPercent}%, so it pays off in the long run.",
            _ => $"Suggest: <b>Fold</b>. The call needs {needPercent}% but this hand wins about {winPercent}%. Save your chips for a better spot.",
        };

        static string StreetTitle(PokerPhase phase) => phase switch
        {
            PokerPhase.PreFlop => "Pre-flop",
            PokerPhase.Flop => "The flop",
            PokerPhase.Turn => "The turn",
            PokerPhase.River => "The river",
            PokerPhase.Showdown => "Showdown",
            _ => "Poker",
        };

        static string StreetExplanation(PokerPhase phase) => phase switch
        {
            PokerPhase.PreFlop => "Pre-flop: everyone holds two private cards. After this betting round come three shared cards, the flop.",
            PokerPhase.Flop => "The flop: three shared cards anyone can use. Next comes the turn, a fourth card.",
            PokerPhase.Turn => "The turn: a fourth shared card. One more to come, the river.",
            PokerPhase.River => "The river: the last shared card. After this betting round, the cards are shown.",
            _ => "",
        };

        static PokerHandValue Evaluate(PokerTable table, byte a, byte b)
        {
            var count = table.CommunityCount;
            Span<byte> cards = stackalloc byte[2 + count];
            cards[0] = a;
            cards[1] = b;
            for (var i = 0; i < count; i++)
                cards[2 + i] = table.CommunityAt(i);
            return PokerRules.Evaluate(cards);
        }

        static string BestHand(PokerTable table, byte a, byte b) => Evaluate(table, a, b).ToString();

        static string CardText(byte card) => Cards.IsRed(card) ? $"<color=#E0605A>{Cards.Label(card)}</color>" : Cards.Label(card);

        static int Percent(float value) => (int)Math.Round(value * 100f);
    }
}
