using System.Linq;
using NUnit.Framework;

namespace CasinoMares.Rules.Tests
{
    public class RouletteRulesTests
    {
        [Test]
        public void WheelHasEveryNumberOnce()
        {
            Assert.AreEqual(37, RouletteRules.WheelOrder.Length);
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 37), RouletteRules.WheelOrder);
        }

        [Test]
        public void EighteenRedEighteenBlack()
        {
            Assert.AreEqual(18, Enumerable.Range(0, 37).Count(RouletteRules.IsRed));
            Assert.AreEqual(18, Enumerable.Range(0, 37).Count(RouletteRules.IsBlack));
            Assert.IsFalse(RouletteRules.IsRed(0));
            Assert.IsFalse(RouletteRules.IsBlack(0));
        }

        [Test]
        public void RedAndBlackAlternateAroundTheWheel()
        {
            for (var i = 1; i < RouletteRules.WheelOrder.Length - 1; i++)
            {
                var a = RouletteRules.WheelOrder[i];
                var b = RouletteRules.WheelOrder[i + 1];
                Assert.AreNotEqual(RouletteRules.IsRed(a), RouletteRules.IsRed(b), $"{a} and {b} are neighbours");
            }
        }

        [TestCase(17, 17, 36 * 10)]
        [TestCase(17, 18, 0)]
        [TestCase(RouletteRules.Red, 1, 20)]
        [TestCase(RouletteRules.Red, 2, 0)]
        [TestCase(RouletteRules.Black, 0, 0)]
        [TestCase(RouletteRules.Odd, 0, 0)]
        [TestCase(RouletteRules.Even, 0, 0)]
        [TestCase(RouletteRules.Even, 36, 20)]
        [TestCase(RouletteRules.Low, 18, 20)]
        [TestCase(RouletteRules.High, 18, 0)]
        [TestCase(RouletteRules.Dozen1, 12, 30)]
        [TestCase(RouletteRules.Dozen1 + 1, 13, 30)]
        [TestCase(RouletteRules.Dozen1 + 2, 36, 30)]
        [TestCase(RouletteRules.Column1, 34, 30)]
        [TestCase(RouletteRules.Column1 + 1, 35, 30)]
        [TestCase(RouletteRules.Column1 + 2, 36, 30)]
        [TestCase(RouletteRules.Column1, 36, 0)]
        [TestCase(0, 0, 360)]
        public void PaysOut(int spot, int result, int expected)
        {
            Assert.AreEqual(expected, RouletteRules.Payout(spot, 10, result));
        }

        [Test]
        public void Labels()
        {
            Assert.AreEqual("19-36", RouletteRules.SpotLabel(RouletteRules.High));
            Assert.AreEqual("13-24", RouletteRules.SpotLabel(RouletteRules.Dozen1 + 1));
            Assert.AreEqual("Column 3", RouletteRules.SpotLabel(RouletteRules.Column1 + 2));
        }
    }
}
