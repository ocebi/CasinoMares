using System;
using System.Linq;
using NUnit.Framework;

namespace CasinoMares.Rules.Tests
{
    public class PokerCoachTests
    {
        static byte C(string code)
        {
            const string ranks = "A23456789TJQK";
            const string suits = "shdc";
            return Cards.Make(ranks.IndexOf(code[0]), suits.IndexOf(code[1]));
        }

        static byte[] Board(string cards) => cards.Length == 0 ? Array.Empty<byte>() : cards.Split(' ').Select(C).ToArray();

        [Test]
        public void AcesWinAboutEightyFivePercentHeadsUp()
        {
            var equity = PokerCoach.Equity(C("As"), C("Ah"), Board(""), 1, 4000, 12345);
            Assert.AreEqual(0.85f, equity, 0.03f);
        }

        [Test]
        public void MoreOpponentsMeanLowerEquity()
        {
            var headsUp = PokerCoach.Equity(C("Kd"), C("Qd"), Board(""), 1, 3000, 7);
            var fiveWay = PokerCoach.Equity(C("Kd"), C("Qd"), Board(""), 4, 3000, 7);
            Assert.Greater(headsUp, fiveWay + 0.2f);
        }

        [Test]
        public void RoyalFlushNeverLoses()
        {
            var equity = PokerCoach.Equity(C("As"), C("Ks"), Board("Qs Js Ts 2d 3c"), 3, 500, 99);
            Assert.AreEqual(1f, equity, 0.0001f);
        }

        [TestCase("As", "Ah", 20)]
        [TestCase("As", "Ks", 12)]
        [TestCase("As", "Kd", 10)]
        [TestCase("2s", "2h", 5)]
        [TestCase("7s", "2d", -1)]
        [TestCase("Ts", "9s", 8)]
        public void ChenScores(string a, string b, int expected)
        {
            Assert.AreEqual(expected, PokerCoach.ChenScore(C(a), C(b)));
        }

        [Test]
        public void TiersFollowTheScore()
        {
            Assert.AreEqual(StartingHandTier.Premium, PokerCoach.Tier(PokerCoach.ChenScore(C("As"), C("Ks"))));
            Assert.AreEqual(StartingHandTier.Weak, PokerCoach.Tier(PokerCoach.ChenScore(C("7s"), C("2d"))));
        }

        [Test]
        public void WeakHandFoldsToABigBetButChecksForFree()
        {
            var facingBet = PokerCoach.Suggest(0.2f, 1, false, 100, 150, 400, 0, 100, 200, 10, out _);
            var free = PokerCoach.Suggest(0.2f, 1, false, 0, 150, 400, 0, 0, 10, 10, out _);
            Assert.AreEqual(PokerAdvice.Fold, facingBet);
            Assert.AreEqual(PokerAdvice.Check, free);
        }

        [Test]
        public void GoodPriceMeansCall()
        {
            // 10 to win 100: 9% needed, 30% to win.
            Assert.AreEqual(PokerAdvice.Call, PokerCoach.Suggest(0.3f, 2, false, 10, 90, 500, 0, 10, 20, 10, out _));
        }

        [Test]
        public void StrongHandRaisesAndCapsAtTheStack()
        {
            Assert.AreEqual(PokerAdvice.Raise, PokerCoach.Suggest(0.8f, 1, true, 10, 15, 500, 0, 10, 20, 10, out var raiseTo));
            Assert.AreEqual(30, raiseTo);
            Assert.AreEqual(PokerAdvice.AllIn, PokerCoach.Suggest(0.8f, 1, false, 0, 300, 100, 0, 0, 10, 10, out raiseTo));
            Assert.AreEqual(100, raiseTo);
        }
    }
}
