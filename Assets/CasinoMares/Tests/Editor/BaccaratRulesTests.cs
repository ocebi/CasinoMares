using System;
using System.Linq;
using NUnit.Framework;

namespace CasinoMares.Rules.Tests
{
    public class BaccaratRulesTests
    {
        static byte C(string rank)
        {
            var labels = new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };
            return Cards.Make(Array.IndexOf(labels, rank), 0);
        }

        [TestCase(new[] { "A", "2" }, 3)]
        [TestCase(new[] { "9", "9" }, 8)]
        [TestCase(new[] { "K", "Q" }, 0)]
        [TestCase(new[] { "7", "5", "10" }, 2)]
        public void Totals(string[] ranks, int expected)
        {
            Assert.AreEqual(expected, BaccaratRules.Total(ranks.Select(C).ToArray()));
        }

        [Test]
        public void PlayerDrawsOnFiveOrLess()
        {
            Assert.IsTrue(BaccaratRules.PlayerDraws(5));
            Assert.IsFalse(BaccaratRules.PlayerDraws(6));
        }

        [TestCase(5, -1, true)]
        [TestCase(6, -1, false)]
        [TestCase(2, 9, true)]
        [TestCase(3, 8, false)]
        [TestCase(3, 9, true)]
        [TestCase(4, 1, false)]
        [TestCase(4, 7, true)]
        [TestCase(5, 3, false)]
        [TestCase(5, 4, true)]
        [TestCase(6, 5, false)]
        [TestCase(6, 6, true)]
        [TestCase(7, 6, false)]
        public void BankerTableau(int bankerTotal, int playerThird, bool draws)
        {
            Assert.AreEqual(draws, BaccaratRules.BankerDraws(bankerTotal, playerThird));
        }

        [Test]
        public void Payouts()
        {
            Assert.AreEqual(200, BaccaratRules.Payout(BaccaratRules.SpotPlayer, 100, BaccaratOutcome.Player));
            Assert.AreEqual(195, BaccaratRules.Payout(BaccaratRules.SpotBanker, 100, BaccaratOutcome.Banker));
            Assert.AreEqual(900, BaccaratRules.Payout(BaccaratRules.SpotTie, 100, BaccaratOutcome.Tie));
            Assert.AreEqual(100, BaccaratRules.Payout(BaccaratRules.SpotPlayer, 100, BaccaratOutcome.Tie), "player bet pushes on a tie");
            Assert.AreEqual(0, BaccaratRules.Payout(BaccaratRules.SpotBanker, 100, BaccaratOutcome.Player));
        }

        [Test]
        public void DealsLegalCoupsFromAShoe()
        {
            var shoe = new Shoe(BaccaratRules.Decks, 99);
            Span<byte> player = stackalloc byte[3];
            Span<byte> banker = stackalloc byte[3];
            for (var coup = 0; coup < 60; coup++)
            {
                var (p, b) = BaccaratRules.DealCoup(shoe, player, banker);
                Assert.That(p, Is.InRange(2, 3));
                Assert.That(b, Is.InRange(2, 3));
                var p2 = BaccaratRules.Total(player.Slice(0, 2));
                var b2 = BaccaratRules.Total(banker.Slice(0, 2));
                if (BaccaratRules.IsNatural(p2) || BaccaratRules.IsNatural(b2))
                    Assert.IsTrue(p == 2 && b == 2, "naturals stand");
                else
                    Assert.AreEqual(BaccaratRules.PlayerDraws(p2), p == 3);
            }
        }
    }
}
