using NUnit.Framework;

namespace CasinoMares.Rules.Tests
{
    public class YarnTumbleRulesTests
    {
        [Test]
        public void SameSeedSameSpin()
        {
            for (var seed = -50; seed < 50; seed++)
            {
                var a = YarnTumbleRules.Play(seed);
                var b = YarnTumbleRules.Play(seed);
                Assert.AreEqual(a.Start, b.Start);
                Assert.AreEqual(a.Final, b.Final);
                Assert.AreEqual(a.Pay, b.Pay);
                Assert.AreEqual(a.Cascades.Count, b.Cascades.Count);
            }
        }

        [TestCase(YarnTumbleRules.BlueYarn, 4, 0)]
        [TestCase(YarnTumbleRules.BlueYarn, 5, 10)]
        [TestCase(YarnTumbleRules.BlueYarn, 6, 12)]
        [TestCase(YarnTumbleRules.GoldenPaw, 5, 20)]
        [TestCase(YarnTumbleRules.GoldenPaw, 7, 30)]
        [TestCase(YarnTumbleRules.GoldenPaw, 25, 45)]
        public void GroupPay(int symbol, int size, int expected)
        {
            Assert.AreEqual(expected, YarnTumbleRules.GroupPay(symbol, size));
        }

        [Test]
        public void CascadesAddUp()
        {
            var chained = 0;
            for (var seed = 0; seed < 3000; seed++)
            {
                var spin = YarnTumbleRules.Play(seed);
                var total = 0;
                for (var i = 0; i < spin.Cascades.Count; i++)
                {
                    var cascade = spin.Cascades[i];
                    var won = 0;
                    foreach (var cell in cascade.Won)
                        won += cell ? 1 : 0;
                    Assert.GreaterOrEqual(won, YarnTumbleRules.MinGroup);
                    Assert.Greater(cascade.Pay, 0);
                    Assert.AreEqual(YarnTumbleRules.ChainMultiplier(i), cascade.Multiplier);
                    total += cascade.Pay;

                    // Symbols that stayed fell straight down their column; new ones came from above the top.
                    var before = spin.Before(i);
                    for (var column = 0; column < YarnTumbleRules.Size; column++)
                    for (var row = 0; row < YarnTumbleRules.Size; row++)
                    {
                        var cell = YarnTumbleRules.Index(column, row);
                        var from = cascade.FromRow[cell];
                        Assert.GreaterOrEqual(from, row);
                        if (from < YarnTumbleRules.Size)
                        {
                            Assert.IsFalse(cascade.Won[YarnTumbleRules.Index(column, from)]);
                            Assert.AreEqual(before[YarnTumbleRules.Index(column, from)], cascade.After[cell]);
                        }
                    }
                }

                Assert.AreEqual(total, spin.Pay);
                Assert.AreEqual(total, spin.PayThrough(spin.Cascades.Count));
                if (spin.Cascades.Count > 1)
                    chained++;
            }

            Assert.Greater(chained, 0, "No spin chained in 3000 tries");
        }

        [TestCase("1", 10)]
        [TestCase("1.5", 15)]
        [TestCase("3.5", 35)]
        public void MultiplierText(string expected, int tenths)
        {
            Assert.AreEqual(expected, YarnTumbleRules.MultiplierText(tenths));
        }

        [Test]
        public void ReturnToPlayerIsBelowOne()
        {
            // Sampled: the cascades make an exact sum impractical. 100k spins land within about ±2% of the true value.
            const int spins = 100000;
            long tenths = 0;
            var wins = 0;
            var random = new System.Random(12345);
            for (var i = 0; i < spins; i++)
            {
                var spin = YarnTumbleRules.Play(random.Next(int.MinValue, int.MaxValue));
                tenths += spin.Pay;
                if (spin.Pay > 0)
                    wins++;
            }

            var rtp = tenths / 10.0 / spins;
            Assert.That(rtp, Is.InRange(0.92, 1.0), $"RTP was {rtp:P1}");
            Assert.That(wins / (double)spins, Is.InRange(0.34, 0.4));
        }
    }
}
