using NUnit.Framework;

namespace CasinoMares.Rules.Tests
{
    public class DrunkTotalsTests
    {
        [Test]
        public void DecoyIsNeverTheRealTotalAndNeverBust()
        {
            for (var total = DrunkTotals.MinTotal; total <= DrunkTotals.MaxTotal; total++)
            {
                for (var cards = 2; cards < 8; cards++)
                {
                    var hash = DrunkTotals.Hash(123u, 7, 2, 0, cards);
                    var (first, second) = DrunkTotals.Candidates(total, hash);
                    Assert.IsTrue(first == total || second == total, $"{total}: real total missing");
                    var decoy = first == total ? second : first;
                    Assert.AreNotEqual(total, decoy);
                    Assert.That(decoy, Is.InRange(DrunkTotals.MinTotal, DrunkTotals.MaxTotal));
                    Assert.That(System.Math.Abs(decoy - total), Is.LessThanOrEqualTo(DrunkTotals.Spread));
                }
            }
        }

        [Test]
        public void SameHandGivesSameCandidates()
        {
            var a = DrunkTotals.Candidates(17, DrunkTotals.Hash(5u, 3, 1, 0, 2));
            var b = DrunkTotals.Candidates(17, DrunkTotals.Hash(5u, 3, 1, 0, 2));
            Assert.AreEqual(a, b);
        }

        [Test]
        public void RealTotalComesFirstAboutHalfTheTime()
        {
            var firsts = 0;
            const int rounds = 400;
            for (var round = 0; round < rounds; round++)
            {
                var (first, _) = DrunkTotals.Candidates(16, DrunkTotals.Hash(9u, round, 0, 0, 2));
                if (first == 16)
                    firsts++;
            }

            Assert.That(firsts, Is.InRange(rounds / 4, rounds * 3 / 4));
        }
    }
}
