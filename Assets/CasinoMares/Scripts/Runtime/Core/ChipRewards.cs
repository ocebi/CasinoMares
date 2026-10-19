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
    /// (game time restarts on a reload).
    /// </summary>
    public static class ChipRewards
    {
        /// <summary>Chips just earned, and whether it's a big reward (cat wall, welfare) that deserves more fuss.</summary>
        public static event Action<int, bool> Earned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Earned = null;

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
        /// cooling down. Returns whether it paid.
        /// </summary>
        public static bool TryEarn(string key, int amount, TimeSpan cooldown, bool big = false)
        {
            if (SecondsLeft(key, cooldown) > 0)
                return false;

            // Forget cooldowns that have run out, so the profile only ever holds the few still running.
            var now = Now;
            Cooldowns.RemoveAll(entry => entry == null || entry.ReadyAt <= now || entry.Key == key);
            Cooldowns.Add(new RewardCooldown { Key = key, ReadyAt = now + (long)cooldown.TotalSeconds });
            // Saves the profile (cooldowns included) and updates the networked balance.
            ProfileStore.SetBalance(ProfileStore.Current.Balance + amount);
            Earned?.Invoke(amount, big);
            return true;
        }

        /// <summary>Pays <paramref name="amount"/> chips with no cooldown (the tipsy bonus), with the same "+N" fuss.</summary>
        public static void Grant(int amount, bool big = false)
        {
            if (amount <= 0)
                return;
            ProfileStore.SetBalance(ProfileStore.Current.Balance + amount);
            Earned?.Invoke(amount, big);
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
