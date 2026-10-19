using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CasinoMares.Core
{
    /// <summary>
    /// Chips a cat earns around the casino rather than wins (GDD §7.2): pawing props and patrons, topping the cat
    /// wall, the cashier's Cat Welfare. Each reward key pays at most once per cooldown. The balance is client-side, so
    /// these cooldowns are the only guard against farming; they're saved in the profile as wall-clock UTC times
    /// (game time restarts on a reload). Every game's payout for a round or spin also comes through here
    /// (<see cref="PayRound"/>), so whatever watches for a big payout (the victory cigar) sees all games, new ones too.
    /// </summary>
    public static class ChipRewards
    {
        /// <summary>
        /// Chips just earned, whether it's a big reward (cat wall, welfare) that deserves more fuss, and whether it's a
        /// quiet one (a paw that landed, which happens often) that shows its "+N" without the coin sound.
        /// </summary>
        public static event Action<int, bool, bool> Earned;

        /// <summary>
        /// Chips just earned that count toward the session leaderboard (GDD §7.4): paws, the cat wall, the tipsy bonus.
        /// Charity (the Cat Welfare refill) doesn't.
        /// </summary>
        public static event Action<int> SessionEarned;

        /// <summary>
        /// The local player's gross payout for one settled round or spin, any game: what it hands back, stake included
        /// (a pushed 500 bet is 500). Only raised for a payout above zero.
        /// </summary>
        public static event Action<int> RoundPaid;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Earned = null;
            SessionEarned = null;
            RoundPaid = null;
        }

        static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        static List<RewardCooldown> Cooldowns => ProfileStore.Current.Cooldowns ??= new List<RewardCooldown>();

        /// <summary>Seconds until <paramref name="key"/> pays again, or 0 when it's ready.</summary>
        public static long SecondsLeft(string key, TimeSpan cooldown)
        {
            foreach (var entry in Cooldowns)
            {
                if (entry == null || entry.Key != key)
                    continue;
                var left = entry.ReadyAt - Now;
                // Longer than the cooldown itself means the device clock went back: don't make the cat wait that out.
                return left > (long)cooldown.TotalSeconds ? 0 : Math.Max(left, 0);
            }

            return 0;
        }

        /// <summary>
        /// Pays <paramref name="amount"/> chips and starts the cooldown of <paramref name="key"/>, unless it's still
        /// cooling down. Returns whether it paid. A <paramref name="quiet"/> reward makes no sound; one that isn't
        /// <paramref name="countsForSession"/> stays off the leaderboard.
        /// </summary>
        public static bool TryEarn(string key, int amount, TimeSpan cooldown, bool big = false, bool quiet = false, bool countsForSession = true)
        {
            if (SecondsLeft(key, cooldown) > 0)
                return false;

            // Forget cooldowns that have run out, so the profile only ever holds the few still running.
            var now = Now;
            Cooldowns.RemoveAll(entry => entry == null || entry.ReadyAt <= now || entry.Key == key);
            Cooldowns.Add(new RewardCooldown { Key = key, ReadyAt = now + (long)cooldown.TotalSeconds });
            // Saves the profile (cooldowns included) and updates the networked balance.
            ProfileStore.SetBalance(ProfileStore.Current.Balance + amount);
            Earned?.Invoke(amount, big, quiet);
            if (countsForSession)
                SessionEarned?.Invoke(amount);
            return true;
        }

        /// <summary>Pays <paramref name="amount"/> chips with no cooldown (the tipsy bonus), with the same "+N" fuss.</summary>
        public static void Grant(int amount, bool big = false)
        {
            if (amount <= 0)
                return;
            ProfileStore.SetBalance(ProfileStore.Current.Balance + amount);
            Earned?.Invoke(amount, big, false);
            SessionEarned?.Invoke(amount);
        }

        /// <summary>
        /// A game's payout to the local player for a settled round or spin, stake included: adds it to the balance and
        /// reports it (<see cref="RoundPaid"/>). Each game calls this once per round, before its own result event.
        /// </summary>
        public static void PayRound(int payout)
        {
            if (payout <= 0)
                return;
            ProfileStore.SetBalance(ProfileStore.Current.Balance + payout);
            RoundPaid?.Invoke(payout);
        }

        /// <summary>
        /// Reports a payout that stays at the table rather than going to the balance (a poker pot joins the seat's
        /// stack, cashed out on leaving).
        /// </summary>
        public static void ReportRound(int payout)
        {
            if (payout > 0)
                RoundPaid?.Invoke(payout);
        }

        /// <summary>
        /// Where an object sits in its scene, e.g. "CasinoSea/Props/Glass_3": unlike runtime network ids, the same
        /// after a reload and on every client of a build.
        /// </summary>
        public static string ScenePath(Transform target)
        {
            var path = new StringBuilder(target.name);
            for (var parent = target.parent; parent != null; parent = parent.parent)
                path.Insert(0, '/').Insert(0, parent.name);
            return $"{target.gameObject.scene.name}/{path}";
        }

        /// <summary>A wait as "42 min", or "30 s" under a minute.</summary>
        public static string FormatWait(long seconds) => seconds >= 60 ? $"{(seconds + 59) / 60} min" : $"{seconds} s";
    }
}
