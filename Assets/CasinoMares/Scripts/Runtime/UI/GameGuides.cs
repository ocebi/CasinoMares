namespace CasinoMares.UI
{
    /// <summary>The rules pages behind the Rules button at the poker and baccarat tables (teaching mode, GDD §13).</summary>
    public static class GameGuides
    {
        const string Red = "<color=#E0605A>";
        const string End = "</color>";

        static string H(string rank) => $"{Red}{rank}♥{End}";
        static string D(string rank) => $"{Red}{rank}♦{End}";

        public static readonly (string heading, string body)[] Poker =
        {
            ("The goal",
                "Win chips. You win the pot either by holding the best five-card hand when the cards are shown, " +
                "or by betting so that everyone else gives up (folds) first."),

            ("How a hand goes",
                "1. <b>Blinds.</b> The two players left of the dealer button put in forced bets, the small and the big blind, so there's always something to play for.\n" +
                "2. <b>Hole cards.</b> Everyone gets two private cards.\n" +
                "3. <b>Pre-flop betting.</b> Starting left of the big blind, each player folds, calls or raises.\n" +
                "4. <b>The flop.</b> Three shared cards are turned face up. Another round of betting.\n" +
                "5. <b>The turn.</b> A fourth shared card. Betting.\n" +
                "6. <b>The river.</b> The fifth and last shared card. Final betting.\n" +
                "7. <b>Showdown.</b> The players still in show their cards. Each makes the best five-card hand from their two cards plus the five shared ones. Best hand wins; ties split the pot."),

            ("Your options on your turn",
                "<b>Fold</b>: give up the hand. Chips you already put in stay in the pot.\n" +
                "<b>Check</b>: pass without betting. Only possible when nobody has bet this round.\n" +
                "<b>Call</b>: match the current bet.\n" +
                "<b>Bet / Raise</b>: put in more. Everyone else must call, raise again or fold. A raise must be at least as big as the last raise (and at least one big blind).\n" +
                "<b>All in</b>: bet everything you have left. If others keep betting past your stack, that extra goes into a side pot you can't win."),

            ("Hand rankings, best first",
                $"1. <b>Royal flush</b>: A K Q J 10 of one suit. A♠ K♠ Q♠ J♠ 10♠\n" +
                $"2. <b>Straight flush</b>: five in a row, same suit. {H("9")} {H("8")} {H("7")} {H("6")} {H("5")}\n" +
                $"3. <b>Four of a kind</b>: 7♠ {H("7")} {D("7")} 7♣\n" +
                $"4. <b>Full house</b>: three of a kind plus a pair. K♠ {H("K")} {D("K")} 4♣ {H("4")}\n" +
                $"5. <b>Flush</b>: any five of one suit. {D("A")} {D("J")} {D("8")} {D("6")} {D("2")}\n" +
                $"6. <b>Straight</b>: five in a row, mixed suits. 10♣ {D("9")} 8♠ {H("7")} 6♣ (the ace can also be low: 5 4 3 2 A)\n" +
                $"7. <b>Three of a kind</b>: Q♠ {H("Q")} Q♣\n" +
                $"8. <b>Two pair</b>: J♠ {H("J")} {D("3")} 3♣\n" +
                $"9. <b>One pair</b>: {H("10")} 10♠\n" +
                "10. <b>High card</b>: none of the above; your highest card plays.\n" +
                "Same type of hand? Higher cards win (a pair of kings beats a pair of queens), then the leftover cards, the \"kickers\"."),

            ("Beginner tips",
                "• Fold most starting hands. Play pairs, two high cards (A K, A Q, K Q) and suited aces; fold hands like 7 2 or 9 3.\n" +
                "• <b>Pot odds</b>: if a call costs 20 and the pot would then hold 100, you need to win at least 20% of the time for calling to pay off. The coach does this sum for you.\n" +
                "• A big bet usually means a strong hand. Believe it more often than not.\n" +
                "• Don't chase: calling big bets hoping to hit a card loses chips over time.\n" +
                "• The dealer button acts last after the flop. Acting last, you see what everyone else did first.\n" +
                "• The NPC cats here play simply: they raise strong hands, call reasonable ones and bluff now and then."),
        };

        public static readonly (string heading, string body)[] Baccarat =
        {
            ("The goal",
                "You don't play a hand yourself. The croupier deals two hands, <b>Player</b> and <b>Banker</b>, and you bet on which one ends " +
                "closer to 9, or on a <b>Tie</b>. The drawing rules are fixed, so after betting there's nothing to decide: sit back and watch."),

            ("Card values",
                "A = 1 · 2 to 9 = face value · 10, J, Q, K = 0.\n" +
                "Only the last digit of the total counts: 7 + 8 = 15, which counts as <b>5</b>. A hand is always worth 0 to 9."),

            ("The deal",
                "1. Two cards each, in the order Player, Banker, Player, Banker.\n" +
                "2. <b>Natural</b>: if either hand has 8 or 9, nobody draws and the coup is over.\n" +
                "3. <b>Player</b> draws a third card on 0 to 5 and stands on 6 or 7.\n" +
                "4. If the Player stood, the <b>Banker</b> draws on 0 to 5 and stands on 6 or 7.\n" +
                "5. If the Player drew, the Banker looks at the value of the Player's third card:\n" +
                "    Banker 0 to 2: always draws\n" +
                "    Banker 3: draws unless the Player's third card is an 8\n" +
                "    Banker 4: draws if it's 2 to 7\n" +
                "    Banker 5: draws if it's 4 to 7\n" +
                "    Banker 6: draws if it's 6 or 7\n" +
                "    Banker 7: stands\n" +
                "6. The higher total wins. Equal totals are a Tie."),

            ("Payouts",
                "<b>Player</b>: 1 to 1.\n" +
                "<b>Banker</b>: 1 to 1 minus 5% commission (0.95 to 1), because the Banker wins a little more often.\n" +
                "<b>Tie</b>: 8 to 1. When it's a tie, Player and Banker bets are simply returned."),

            ("Odds and tips (8 decks)",
                "Banker wins 45.9% of coups, Player 44.6%, Tie 9.5%.\n" +
                "House edge: Banker 1.06% · Player 1.24% · Tie 14.4%.\n" +
                "• The Banker bet is the best bet in the house, even after the commission.\n" +
                "• Avoid the Tie: the 8 to 1 payout doesn't make up for how rarely it hits.\n" +
                "• The road (the row of past results) is fun to follow, but past coups don't change the next one. Streaks don't predict anything."),
        };
    }
}
