using System;
using System.Linq;
using NUnit.Framework;

namespace CasinoMares.Rules.Tests
{
    public class PokerRulesTests
    {
        // "As" = ace of spades, "Td" = ten of diamonds, ...
        static byte C(string code)
        {
            const string ranks = "A23456789TJQK";
            const string suits = "shdc";
            return Cards.Make(ranks.IndexOf(code[0]), suits.IndexOf(code[1]));
        }

        static PokerHandValue Eval(string hand) => PokerRules.Evaluate(hand.Split(' ').Select(C).ToArray());

        [TestCase("As Ks Qs Js Ts 2d 3c", PokerCategory.StraightFlush)]
        [TestCase("9h 9d 9s 9c 2d 3c 4h", PokerCategory.Quads)]
        [TestCase("9h 9d 9s 2c 2d 3c 4h", PokerCategory.FullHouse)]
        [TestCase("2h 7h 9h Jh Kh 2d 3c", PokerCategory.Flush)]
        [TestCase("Ah 2d 3c 4s 5h 9d Kc", PokerCategory.Straight)]
        [TestCase("9h 9d 9s 2c 5d 7c Jh", PokerCategory.Trips)]
        [TestCase("9h 9d 2s 2c 5d 7c Jh", PokerCategory.TwoPair)]
        [TestCase("9h 9d 3s 2c 5d 7c Jh", PokerCategory.Pair)]
        [TestCase("9h Td 3s 2c 5d 7c Jh", PokerCategory.HighCard)]
        public void Categories(string hand, PokerCategory expected)
        {
            Assert.AreEqual(expected, Eval(hand).Category);
        }

        [Test]
        public void WheelIsFiveHighStraight()
        {
            var wheel = Eval("Ah 2d 3c 4s 5h");
            var sixHigh = Eval("2d 3c 4s 5h 6d");
            Assert.AreEqual(5, wheel.Kicker(0));
            Assert.IsTrue(sixHigh > wheel);
        }

        [Test]
        public void KickersBreakTies()
        {
            Assert.IsTrue(Eval("Ah Ad Kc 7s 4d 3c 2h") > Eval("As Ac Qd 7h 4c 3d 2s"));
            Assert.AreEqual(Eval("Ah Kd 9c 7s 4d").Score, Eval("As Kc 9d 7h 4c").Score, "suits don't matter");
            Assert.IsTrue(Eval("Kh Kd 2c 2s Ad") > Eval("Kc Ks 2d 2h Qd"));
        }

        [Test]
        public void FlushBeatsStraightAndBoatBeatsFlush()
        {
            Assert.IsTrue(Eval("2h 7h 9h Jh Kh") > Eval("9d Tc Js Qh Kd"));
            Assert.IsTrue(Eval("3h 3d 3s 2c 2d") > Eval("2h 7h 9h Jh Kh"));
        }

        [Test]
        public void Describes()
        {
            Assert.AreEqual("Royal flush", Eval("As Ks Qs Js Ts").ToString());
            Assert.AreEqual("Full house, 9s full of 2s", Eval("9h 9d 9s 2c 2d").ToString());
        }

        [Test]
        public void MainPotOnly()
        {
            var pots = PokerRules.BuildPots(new[] { 100, 100, 100 }, new[] { false, false, false });
            Assert.AreEqual(1, pots.Count);
            Assert.AreEqual(300, pots[0].Amount);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, pots[0].Eligible);
        }

        [Test]
        public void SidePotForShortAllIn()
        {
            // Seat 0 all-in for 50, seats 1 and 2 put in 200 each.
            var pots = PokerRules.BuildPots(new[] { 50, 200, 200 }, new[] { false, false, false });
            Assert.AreEqual(2, pots.Count);
            Assert.AreEqual(150, pots[0].Amount);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, pots[0].Eligible);
            Assert.AreEqual(300, pots[1].Amount);
            CollectionAssert.AreEqual(new[] { 1, 2 }, pots[1].Eligible);
        }

        [Test]
        public void FoldedChipsStayInThePot()
        {
            var pots = PokerRules.BuildPots(new[] { 80, 100, 100 }, new[] { true, false, false });
            Assert.AreEqual(280, pots.Sum(p => p.Amount));
            Assert.That(pots.All(p => !p.Eligible.Contains(0)));
        }

        [Test]
        public void FolderWhoPutInMostStillFundsThePot()
        {
            var pots = PokerRules.BuildPots(new[] { 300, 100, 50 }, new[] { true, false, false });
            Assert.AreEqual(450, pots.Sum(p => p.Amount));
        }
    }
}
