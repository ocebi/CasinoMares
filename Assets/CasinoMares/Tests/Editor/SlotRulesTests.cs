using NUnit.Framework;

namespace CasinoMares.Rules.Tests
{
    public class SlotRulesTests
    {
        [Test]
        public void RollsCoverEverySymbol()
        {
            var seen = new bool[SlotRules.SymbolCount];
            for (var roll = 0; roll < SlotRules.WeightTotal; roll++)
                seen[SlotRules.SymbolForRoll(roll)] = true;
            Assert.That(seen, Has.All.True);
            Assert.AreEqual(SlotRules.Fish, SlotRules.SymbolForRoll(0));
            Assert.AreEqual(SlotRules.Seven, SlotRules.SymbolForRoll(SlotRules.WeightTotal - 1));
        }

        [TestCase(SlotRules.Seven, SlotRules.Seven, SlotRules.Seven, 150)]
        [TestCase(SlotRules.Fish, SlotRules.Fish, SlotRules.Fish, 8)]
        [TestCase(SlotRules.Paw, SlotRules.Paw, SlotRules.Paw, 60)]
        [TestCase(SlotRules.Paw, SlotRules.Fish, SlotRules.Paw, 3)]
        [TestCase(SlotRules.Milk, SlotRules.Paw, SlotRules.Yarn, 1)]
        [TestCase(SlotRules.Milk, SlotRules.Fish, SlotRules.Yarn, 0)]
        public void Multipliers(int a, int b, int c, int expected)
        {
            Assert.AreEqual(expected, SlotRules.Multiplier(a, b, c));
        }

        [Test]
        public void ReturnToPlayerIsBelowOne()
        {
            // Exact expected value over every weighted combination; the house should keep a small edge.
            double total = SlotRules.WeightTotal;
            double expected = 0;
            for (var a = 0; a < SlotRules.SymbolCount; a++)
            for (var b = 0; b < SlotRules.SymbolCount; b++)
            for (var c = 0; c < SlotRules.SymbolCount; c++)
            {
                var p = SlotRules.Weights[a] / total * (SlotRules.Weights[b] / total) * (SlotRules.Weights[c] / total);
                expected += p * SlotRules.Multiplier(a, b, c);
            }

            Assert.That(expected, Is.InRange(0.9, 1.0), $"RTP was {expected:P1}");
        }
    }
}
