namespace CasinoMares.Slots
{
    /// <summary>
    /// The pace of a Yarn Tumble spin, shared by the machine (how long a spin lasts), its view and the HUD: the old
    /// symbols fall out and new ones drop in, then each chain plays out in turn.
    /// </summary>
    public static class YarnTumbleTiming
    {
        /// <summary>The old grid falls out and the new one drops in, column by column.</summary>
        public const float DropIn = 1.2f;
        /// <summary>One chain: the winners glow, get batted away, and the rest fall into the gaps.</summary>
        public const float Chain = 1.25f;
        /// <summary>Within a chain: how long the winners glow before the paw bats them.</summary>
        public const float Glow = 0.45f;
        /// <summary>Within a chain: how long the batted symbols take to fly off.</summary>
        public const float Bat = 0.25f;
        /// <summary>Within a chain: how long the rest take to fall (a settle follows until the chain ends).</summary>
        public const float Fall = 0.4f;
        /// <summary>A pause at rest before the spin counts as over.</summary>
        public const float End = 0.3f;

        public static float Duration(int cascades) => DropIn + cascades * Chain + End;

        /// <summary>
        /// The chain playing <paramref name="elapsed"/> seconds into a spin, and the time into it: -1 while the grid
        /// drops in, <paramref name="cascades"/> once the last chain is over.
        /// </summary>
        public static int ChainAt(float elapsed, int cascades, out float time)
        {
            if (elapsed < DropIn)
            {
                time = elapsed;
                return -1;
            }

            var chain = (int)((elapsed - DropIn) / Chain);
            if (chain >= cascades)
            {
                time = elapsed - DropIn - cascades * Chain;
                return cascades;
            }

            time = elapsed - DropIn - chain * Chain;
            return chain;
        }
    }
}
