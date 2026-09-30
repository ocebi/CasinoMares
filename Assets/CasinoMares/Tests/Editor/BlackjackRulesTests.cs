using System.Linq;
using NUnit.Framework;

namespace CasinoMares.Rules.Tests
{
    public class BlackjackRulesTests
    {
        // Shorthand: rank by label, suit spades unless it matters.
        static byte C(string rank, int suit = 0)
        {
            var labels = new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };
            return Cards.Make(System.Array.IndexOf(labels, rank), suit);
        }

        static byte[] H(params string[] ranks) => ranks.Select(r => C(r)).ToArray();

        [Test]
        public void CardEncodingRoundTrips()
        {
            var card = Cards.Make(Cards.Queen, 2);
            Assert.AreEqual(Cards.Queen, Cards.Rank(card));
            Assert.AreEqual(2, Cards.Suit(card));
            Assert.IsTrue(Cards.IsRed(card));
            Assert.AreEqual("Q♦", Cards.Label(card));
        }

        [TestCase(new[] { "10", "7" }, 17, false)]
        [TestCase(new[] { "A", "6" }, 17, true)]
        [TestCase(new[] { "A", "6", "10" }, 17, false)]
        [TestCase(new[] { "A", "A" }, 12, true)]
        [TestCase(new[] { "A", "A", "9" }, 21, true)]
        [TestCase(new[] { "K", "Q", "5" }, 25, false)]
        public void EvaluatesTotalsAndSoftness(string[] ranks, int total, bool soft)
        {
            var value = BlackjackRules.Evaluate(H(ranks));
            Assert.AreEqual(total, value.Total);
            Assert.AreEqual(soft, value.IsSoft);
        }

        [Test]
        public void NaturalOnlyForTwoCardUnsplitTwentyOne()
        {
            Assert.IsTrue(BlackjackRules.IsNatural(H("A", "K"), false));
            Assert.IsFalse(BlackjackRules.IsNatural(H("A", "K"), true));
            Assert.IsFalse(BlackjackRules.IsNatural(H("7", "7", "7"), false));
        }

        [Test]
        public void DealerStandsOnSoft17AndHitsSixteen()
        {
            Assert.IsFalse(BlackjackRules.DealerShouldHit(H("A", "6")));
            Assert.IsFalse(BlackjackRules.DealerShouldHit(H("10", "7")));
            Assert.IsTrue(BlackjackRules.DealerShouldHit(H("10", "6")));
            Assert.IsTrue(BlackjackRules.DealerShouldHit(H("A", "5")));
        }

        [Test]
        public void DealerPeeksOnAceOrTen()
        {
            Assert.IsTrue(BlackjackRules.DealerShouldPeek(C("A")));
            Assert.IsTrue(BlackjackRules.DealerShouldPeek(C("K")));
            Assert.IsFalse(BlackjackRules.DealerShouldPeek(C("9")));
        }

        [Test]
        public void SplitRules()
        {
            Assert.IsTrue(BlackjackRules.CanSplit(H("8", "8"), 1, false));
            Assert.IsTrue(BlackjackRules.CanSplit(H("K", "Q"), 1, false), "equal value splits are allowed");
            Assert.IsFalse(BlackjackRules.CanSplit(H("8", "9"), 1, false));
            Assert.IsFalse(BlackjackRules.CanSplit(H("8", "8"), 4, false), "max 4 hands");
            Assert.IsFalse(BlackjackRules.CanSplit(H("A", "A"), 2, true), "no resplitting aces");
        }

        [Test]
        public void DoubleRules()
        {
            Assert.IsTrue(BlackjackRules.CanDouble(H("5", "6"), false));
            Assert.IsFalse(BlackjackRules.CanDouble(H("5", "6", "2"), false));
            Assert.IsFalse(BlackjackRules.CanDouble(H("A", "5"), true), "split aces take one card only");
        }

        [Test]
        public void SettlesHands()
        {
            Assert.AreEqual(HandOutcome.Blackjack, BlackjackRules.Settle(H("A", "K"), false, H("10", "9")));
            Assert.AreEqual(HandOutcome.Push, BlackjackRules.Settle(H("A", "K"), false, H("A", "Q")));
            Assert.AreEqual(HandOutcome.Lose, BlackjackRules.Settle(H("10", "10"), false, H("A", "Q")));
            Assert.AreEqual(HandOutcome.Win, BlackjackRules.Settle(H("A", "K"), true, H("10", "9")), "split 21 is a plain win");
            Assert.AreEqual(HandOutcome.Bust, BlackjackRules.Settle(H("10", "6", "9"), false, H("10", "6", "8")), "player bust loses even if dealer busts");
            Assert.AreEqual(HandOutcome.Win, BlackjackRules.Settle(H("10", "2"), false, H("10", "6", "8")));
            Assert.AreEqual(HandOutcome.Push, BlackjackRules.Settle(H("10", "8"), false, H("9", "9")));
            Assert.AreEqual(HandOutcome.Lose, BlackjackRules.Settle(H("10", "7"), false, H("9", "9")));
        }

        [Test]
        public void PaysThreeToTwo()
        {
            Assert.AreEqual(250, BlackjackRules.Payout(HandOutcome.Blackjack, 100));
            Assert.AreEqual(200, BlackjackRules.Payout(HandOutcome.Win, 100));
            Assert.AreEqual(100, BlackjackRules.Payout(HandOutcome.Push, 100));
            Assert.AreEqual(0, BlackjackRules.Payout(HandOutcome.Lose, 100));
            Assert.AreEqual(0, BlackjackRules.Payout(HandOutcome.Bust, 100));
            Assert.AreEqual(37, BlackjackRules.Payout(HandOutcome.Blackjack, 15), "odd 3:2 payouts round down");
        }

        [Test]
        public void ShoeHasSixOfEveryCardAndIsDeterministic()
        {
            var a = new Shoe(6, 1234);
            var b = new Shoe(6, 1234);
            var drawn = new int[Cards.DeckSize];
            for (var i = 0; i < a.Count; i++)
            {
                var card = a.Draw();
                Assert.AreEqual(card, b.Draw());
                drawn[card]++;
            }

            Assert.That(drawn, Has.All.EqualTo(6));
            Assert.AreEqual(0, a.Remaining);
        }

        [Test]
        public void ShoeResumesFromDrawIndex()
        {
            var full = new Shoe(6, 42);
            for (var i = 0; i < 100; i++)
                full.Draw();
            var resumed = new Shoe(6, 42, 100);
            Assert.AreEqual(full.Draw(), resumed.Draw());
        }

        [Test]
        public void CutCardAtPenetration()
        {
            var shoe = new Shoe(6, 7, 233);
            Assert.IsFalse(shoe.CutCardReached(BlackjackRules.Penetration));
            shoe.Draw();
            Assert.IsTrue(shoe.CutCardReached(BlackjackRules.Penetration));
        }

        [TestCase(new[] { "A", "A" }, "6", true, true, BlackjackAction.Split)]
        [TestCase(new[] { "8", "8" }, "10", true, true, BlackjackAction.Split)]
        [TestCase(new[] { "10", "10" }, "6", true, true, BlackjackAction.Stand)]
        [TestCase(new[] { "5", "6" }, "10", true, false, BlackjackAction.Double)]
        [TestCase(new[] { "5", "6" }, "A", true, false, BlackjackAction.Hit)]
        [TestCase(new[] { "10", "6" }, "6", true, false, BlackjackAction.Stand)]
        [TestCase(new[] { "10", "6" }, "7", true, false, BlackjackAction.Hit)]
        [TestCase(new[] { "10", "2" }, "3", true, false, BlackjackAction.Hit)]
        [TestCase(new[] { "A", "7" }, "4", true, false, BlackjackAction.Double)]
        [TestCase(new[] { "A", "7" }, "4", false, false, BlackjackAction.Stand)]
        [TestCase(new[] { "A", "7" }, "9", true, false, BlackjackAction.Hit)]
        [TestCase(new[] { "9", "9" }, "7", true, true, BlackjackAction.Stand)]
        public void BasicStrategySamples(string[] hand, string up, bool canDouble, bool canSplit, BlackjackAction expected)
        {
            Assert.AreEqual(expected, BasicStrategy.Decide(H(hand), C(up), canDouble, canSplit));
        }
    }
}
