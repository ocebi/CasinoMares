using System;
using Fusion;
using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// The wine a cat has drunk (GDD §4.3), next to <see cref="PlayerCat"/> on the player prefab. The owner stamps
    /// each glass and counts the ones still in effect; everyone reads <see cref="DrinkCount"/> for the sway, the
    /// owner for the visuals and the tipsy bonus.
    /// </summary>
    public class CatDrinking : NetworkBehaviour
    {
        /// <summary>
        /// Glasses of wine still in effect: the owner counts the ones drunk in the last <see cref="DrinkDuration"/>.
        /// </summary>
        [Networked]
        public byte DrinkCount { get; set; }

        /// <summary>The ticks the last <see cref="MaxDrinks"/> glasses were finished at (0 = empty slot), on the network clock like the cigar.</summary>
        [Networked, Capacity(MaxDrinks)]
        NetworkArray<int> DrinkTicks => default;

        /// <summary>Glasses at which a cat is drunk, and how long each glass counts. Not saved: a reload sobers you.</summary>
        public const int DrunkAt = 3;
        public const float DrinkDuration = 360f;
        /// <summary>Glasses remembered at once; a ninth replaces the oldest.</summary>
        const int MaxDrinks = 8;
        const float DrinkRecountInterval = 1f;

        /// <summary>Owner only: crossed the drunk line either way.</summary>
        public event Action<bool> DrunkChanged;

        public bool IsDrunk => DrinkCount >= DrunkAt;

        /// <summary>The local cat has spawned and is drunk.</summary>
        public static bool LocalIsDrunk
        {
            get
            {
                var local = PlayerCat.Local;
                return local != null && local.Object != null && local.Object.IsValid && local.Drinking != null && local.Drinking.IsDrunk;
            }
        }

        PlayerCat _cat;
        float _nextDrinkRecount;

        void Awake()
        {
            _cat = GetComponent<PlayerCat>();
        }

        public override void Spawned()
        {
            if (HasStateAuthority)
                RecountDrinks();
        }

        public override void Render()
        {
            if (HasStateAuthority && Time.time >= _nextDrinkRecount)
                RecountDrinks();
        }

        /// <summary>Owner only: a glass has just been finished at the bar.</summary>
        public void AddDrink()
        {
            if (!HasStateAuthority)
                return;
            var oldest = 0;
            for (var i = 1; i < MaxDrinks; i++)
            {
                if (DrinkTicks[i] < DrinkTicks[oldest])
                    oldest = i;
            }

            DrinkTicks.Set(oldest, Runner.Tick.Raw);
            RecountDrinks();
        }

        /// <summary>Drops the glasses that have worn off and publishes the count; about once a second.</summary>
        void RecountDrinks()
        {
            _nextDrinkRecount = Time.time + DrinkRecountInterval;
            var count = (byte)0;
            for (var i = 0; i < MaxDrinks; i++)
            {
                var tick = DrinkTicks[i];
                if (tick != 0 && (Runner.Tick.Raw - tick) * Runner.DeltaTime < DrinkDuration)
                    count++;
            }

            if (count == DrinkCount)
                return;

            var wasDrunk = IsDrunk;
            DrinkCount = count;
            if (IsDrunk == wasDrunk)
                return;
            if (IsDrunk && _cat != null && _cat.Social != null)
                _cat.Social.Announce($"{_cat.DisplayName} has had one too many.");
            DrunkChanged?.Invoke(IsDrunk);
        }
    }
}
