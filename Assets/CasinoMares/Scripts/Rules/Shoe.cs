using System;

namespace CasinoMares.Rules
{
    /// <summary>
    /// A multi-deck shoe shuffled from a seed. The seed and draw position fully describe the shoe, so they can be
    /// networked and a new table authority can carry on with exactly the same cards.
    /// </summary>
    public sealed class Shoe
    {
        readonly byte[] _cards;

        public int Seed { get; }
        public int DrawIndex { get; private set; }
        public int Count => _cards.Length;
        public int Remaining => _cards.Length - DrawIndex;

        public Shoe(int decks, int seed, int drawIndex = 0)
        {
            if (decks < 1)
                throw new ArgumentOutOfRangeException(nameof(decks));

            Seed = seed;
            _cards = new byte[decks * Cards.DeckSize];
            for (var i = 0; i < _cards.Length; i++)
                _cards[i] = (byte)(i % Cards.DeckSize);

            // Fisher-Yates with our own PRNG so the order never depends on the runtime's Random implementation.
            var rng = new XorShift32(seed);
            for (var i = _cards.Length - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
            }

            DrawIndex = Math.Clamp(drawIndex, 0, _cards.Length);
        }

        /// <summary>True once the cut card (at <paramref name="penetration"/> of the shoe) has been reached.</summary>
        public bool CutCardReached(double penetration) => DrawIndex >= (int)(_cards.Length * penetration);

        public byte Draw()
        {
            if (DrawIndex >= _cards.Length)
                throw new InvalidOperationException("The shoe is empty.");
            return _cards[DrawIndex++];
        }

        public byte Peek(int offset = 0) => _cards[DrawIndex + offset];
    }

    /// <summary>Tiny deterministic PRNG (Marsaglia xorshift32).</summary>
    public struct XorShift32
    {
        uint _state;

        public XorShift32(int seed)
        {
            _state = (uint)seed;
            if (_state == 0)
                _state = 0x9E3779B9u;
        }

        public uint NextUInt()
        {
            var x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>Uniform-ish integer in [0, maxExclusive).</summary>
        public int Next(int maxExclusive) => (int)(NextUInt() % (uint)maxExclusive);
    }
}
